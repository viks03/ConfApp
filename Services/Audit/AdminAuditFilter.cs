using System.Text;
using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ConferenceApp.Services.Audit
{
    /// <summary>
    /// Записва в одита всяко административно действие — автоматично.
    ///
    /// <para>
    /// Защо филтър, а не повикване във всеки handler: панелът има 50 handler-а,
    /// от които само 8 логваха. Ръчното добавяне на останалите 42 решава
    /// проблема еднократно, но не и за напред — всеки нов handler отново би
    /// бил без одит и никой не би забелязал, докато не потрябва.
    /// </para>
    ///
    /// <para>
    /// Филтърът се закача за страницата, вижда кой handler е извикан, с какви
    /// параметри и какъв е резултатът, и пише сам. Handler-ите, които вече
    /// логват подробно (одобрение на верификация, потвърждение на плащане),
    /// продължават да го правят — филтърът ги пропуска, за да няма два записа
    /// за едно действие.
    /// </para>
    /// </summary>
    public sealed class AdminAuditFilter : IAsyncPageFilter
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<AdminAuditFilter> _logger;

        public AdminAuditFilter(ApplicationDbContext db, ILogger<AdminAuditFilter> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Handler-и, които пишат собствен, по-подробен запис. Филтърът мълчи
        /// за тях, за да не се получат два реда за едно действие.
        /// </summary>
        private static readonly HashSet<string> SelfLogging = new(StringComparer.OrdinalIgnoreCase)
        {
            "SaveRegistration", "DeleteUser", "ConfirmPayment", "CancelPayment",
            "ApproveVerification", "RejectVerification",
            "ClearInactiveCryptoOrders", "ToggleEmailNotification", "TogglePaymentGate"
        };

        /// <summary>
        /// Четене без странични ефекти. Тези се извикват често (Health Check
        /// на всеки 30 секунди, справки при отваряне на модал) и биха затрупали
        /// одита със шум, в който истинските промени се губят.
        /// </summary>
        private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
        {
            "HealthCheck", "FetchRejectionReason", "FetchUserAudits"
        };

        /// <summary>
        /// Имена на параметри, чиито стойности НЕ влизат в одита. Одитът се
        /// чете от хора и се изнася в CSV — парола или токен, попаднали там,
        /// изтичат наведнъж към всички с достъп до панела.
        /// </summary>
        private static readonly string[] Sensitive =
        {
            "password", "pass", "token", "secret", "apikey", "api_key",
            "connectionstring", "__requestverificationtoken"
        };

        /// <summary>
        /// Полета, които носят цяло съдържание — HTML на политика за
        /// поверителност, условия за ползване, описания.
        ///
        /// <para>
        /// Записваха се цели и одитът ставаше нечетим: един ред за смяна на
        /// политиката заемаше 900 знака base64, в които нищо не се вижда.
        /// За такова поле има значение ЧЕ е сменено и с колко, не какво точно
        /// пише вътре — самото съдържание и без това е в базата.
        /// </para>
        /// </summary>
        private static readonly string[] BulkContent =
        {
            "content", "html", "body", "description", "policy", "notice",
            "terms", "answer", "text"
        };

        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context)
            => Task.CompletedTask;

        public async Task OnPageHandlerExecutionAsync(
            PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            var handlerName = context.HandlerMethod?.Name ?? string.Empty;
            var action = CleanHandlerName(handlerName);
            var http = context.HttpContext;

            // Само променящи действия. GET заявките за справки не са събитие.
            var isMutating = HttpMethods.IsPost(http.Request.Method)
                             || HttpMethods.IsPut(http.Request.Method)
                             || HttpMethods.IsDelete(http.Request.Method);

            var shouldLog = isMutating
                            && !string.IsNullOrEmpty(action)
                            && !SelfLogging.Contains(action)
                            && !Ignored.Contains(action);

            // Аргументите се четат ПРЕДИ изпълнението: handler-ът може да ги
            // промени, а одитът трябва да пази какво е поискано, не какво е
            // останало след това.
            //
            // БЪГ ФИКС: четеше се само HandlerArguments. Част от handler-ите
            // обаче нямат параметри — приемат данните през [BindProperty]
            // пропъртита на модела (OnPostEditTicketAsync() е такъв). За тях
            // HandlerArguments е празен и записът излизаше само „OK (redirect)“,
            // без никаква следа какво е променено.
            var argsText = shouldLog
                ? Join(DescribeArguments(context.HandlerArguments),
                       DescribeBoundProperties(context.HandlerInstance))
                : null;

            var executed = await next();

            if (!shouldLog) return;

            try
            {
                var ok = executed.Exception is null;
                var outcome = ok ? DescribeResult(executed.Result) : "FAILED";

                var details = new StringBuilder();
                details.Append(outcome);
                if (!string.IsNullOrEmpty(argsText)) details.Append(" | ").Append(argsText);

                if (executed.Exception is not null)
                    details.Append(" | Error: ")
                           .Append(executed.Exception.GetType().Name)
                           .Append(": ")
                           .Append(Truncate(executed.Exception.Message, 200));

                _db.Set<AuditLog>().Add(new AuditLog
                {
                    UserId    = string.Empty,
                    UserEmail = http.User?.Identity?.Name ?? "unknown",
                    Action    = action,
                    IpAddress = http.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                    Details   = Truncate(details.ToString(), 900),
                    Timestamp = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Провален одит не бива да проваля самото действие — то вече е
                // изпълнено и записано.
                _logger.LogError(ex, "Неуспешен одит запис за {Action}.", action);
            }
        }

        // ── помощни ───────────────────────────────────────────────────────

        /// <summary>OnPostSaveLecturerAsync → SaveLecturer</summary>
        private static string CleanHandlerName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var s = name;
            foreach (var p in new[] { "OnPost", "OnGet", "OnPut", "OnDelete" })
                if (s.StartsWith(p, StringComparison.Ordinal)) { s = s[p.Length..]; break; }
            if (s.EndsWith("Async", StringComparison.Ordinal)) s = s[..^5];
            return s;
        }

        private static string Join(params string?[] parts)
            => string.Join(", ", parts.Where(p => !string.IsNullOrEmpty(p)));

        /// <summary>
        /// Чете стойностите на [BindProperty] пропъртитата на страницата.
        /// Само тези с BindProperty — иначе би се изсипал целият модел с
        /// всички списъци, които OnGet е напълнил.
        /// </summary>
        private static string DescribeBoundProperties(object? page)
        {
            if (page is null) return string.Empty;

            var parts = new List<string>();
            foreach (var prop in page.GetType().GetProperties())
            {
                var bind = prop.GetCustomAttributes(
                    typeof(Microsoft.AspNetCore.Mvc.BindPropertyAttribute), true);
                if (bind.Length == 0) continue;

                object? value;
                try { value = prop.GetValue(page); } catch { continue; }
                if (value is null) continue;

                if (Sensitive.Any(s => prop.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
                {
                    parts.Add($"{prop.Name}=***");
                    continue;
                }

                // Обектите се разгъват едно ниво: [BindProperty] обикновено
                // сочи към модел (EditTicket, StudentInput), а точно неговите
                // полета са това, което администраторът е променил.
                var text = value is string || value.GetType().IsPrimitive
                           || value is decimal || value is DateTime
                    ? DescribeValue(value)
                    : DescribeObject(value);

                if (!string.IsNullOrEmpty(text)) parts.Add($"{prop.Name}={{{text}}}");
            }
            return string.Join(", ", parts);
        }

        /// <summary>Разгъва прост обект до „поле=стойност“, едно ниво надълбоко.</summary>
        private static string DescribeObject(object o)
        {
            var parts = new List<string>();
            foreach (var p in o.GetType().GetProperties())
            {
                if (!p.CanRead) continue;
                if (Sensitive.Any(s => p.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
                { parts.Add($"{p.Name}=***"); continue; }

                object? v;
                try { v = p.GetValue(o); } catch { continue; }
                if (v is null) continue;

                if (v is string big && big.Length > 120 &&
                    BulkContent.Any(b => p.Name.Contains(b, StringComparison.OrdinalIgnoreCase)))
                { parts.Add($"{p.Name}=[{big.Length} знака]"); continue; }

                var t = DescribeValue(v);
                if (t is not null) parts.Add($"{p.Name}={t}");

                // Достатъчно, за да се разбере какво е станало; повече прави
                // реда нечетим.
                if (parts.Count >= 8) { parts.Add("…"); break; }
            }
            return string.Join(", ", parts);
        }

        private static string DescribeArguments(IDictionary<string, object?> args)
        {
            if (args.Count == 0) return string.Empty;

            var parts = new List<string>();
            foreach (var (name, value) in args)
            {
                if (Sensitive.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase)))
                {
                    parts.Add($"{name}=***");
                    continue;
                }
                // Едрите текстови полета се обобщават, не се записват цели.
                if (value is string big && big.Length > 120 &&
                    BulkContent.Any(b => name.Contains(b, StringComparison.OrdinalIgnoreCase)))
                {
                    parts.Add($"{name}=[{big.Length} знака]");
                    continue;
                }

                var text = DescribeValue(value);
                if (text is not null) parts.Add($"{name}={text}");
            }
            return string.Join(", ", parts);
        }

        private static string? DescribeValue(object? v) => v switch
        {
            null                     => null,
            string s                 => s.Length == 0 ? null : Quote(Truncate(s, 120)),
            bool b                   => b ? "true" : "false",
            IFormFile f              => $"[file {f.FileName}, {f.Length / 1024} KB]",
            // Сложните обекти (цели модели) не се разгъват — записът трябва да
            // остане четим. Достатъчно е, че действието е станало.
            _ when v.GetType().IsPrimitive || v is decimal || v is DateTime => v.ToString(),
            System.Collections.ICollection c => $"[{c.Count} елемента]",
            _                        => $"[{v.GetType().Name}]"
        };

        private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;

        private static string DescribeResult(Microsoft.AspNetCore.Mvc.IActionResult? result) => result switch
        {
            Microsoft.AspNetCore.Mvc.JsonResult j    => DescribeJson(j),
            Microsoft.AspNetCore.Mvc.RedirectToPageResult => "OK (redirect)",
            // PageResult е в RazorPages, не в Mvc — за разлика от останалите тук.
            Microsoft.AspNetCore.Mvc.RazorPages.PageResult => "OK (page)",
            Microsoft.AspNetCore.Mvc.FileResult      => "OK (file)",
            null                                     => "OK",
            _                                        => "OK"
        };

        /// <summary>
        /// Повечето handler-и връщат <c>{ success = bool, message = string }</c>.
        /// Одитът трябва да различи „натиснато“ от „успяло“.
        /// </summary>
        private static string DescribeJson(Microsoft.AspNetCore.Mvc.JsonResult j)
        {
            if (j.Value is null) return "OK";
            var t = j.Value.GetType();

            var success = t.GetProperty("success")?.GetValue(j.Value);
            if (success is bool ok)
            {
                if (ok) return "OK";
                var msg = t.GetProperty("message")?.GetValue(j.Value)?.ToString();
                return string.IsNullOrEmpty(msg) ? "REJECTED" : $"REJECTED: {Truncate(msg, 150)}";
            }
            return "OK";
        }

        private static string Truncate(string s, int max)
            => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "…");
    }
}
