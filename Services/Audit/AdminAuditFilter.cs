// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text;
using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ConferenceApp.Services.Audit
{
    /// <summary>
    /// Writes every administrative action to the audit log, automatically.
    ///
    /// <para>
    /// Why a filter rather than a call in each handler: the panel has 50
    /// handlers, of which only 8 logged. Adding the other 42 by hand fixes the
    /// problem once but not from then on — every new handler would again come
    /// without an audit trail, and nobody would notice until it was needed.
    /// </para>
    ///
    /// <para>
    /// The filter attaches to the page, sees which handler was called, with
    /// what arguments and to what result, and writes the row itself. Handlers
    /// that already log in more detail (verification approval, payment
    /// confirmation) keep doing so — the filter skips them so that one action
    /// does not produce two rows.
    /// </para>
    /// </summary>
    public sealed class AdminAuditFilter : IAsyncPageFilter
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<AdminAuditFilter> _logger;
        private readonly ConferenceApp.Services.AuditService _audit;

        public AdminAuditFilter(ApplicationDbContext db, ILogger<AdminAuditFilter> logger,
                                ConferenceApp.Services.AuditService audit)
        {
            _db = db;
            _logger = logger;
            _audit = audit;
        }

        /// <summary>
        /// Handlers that write their own, more detailed row. The filter stays
        /// quiet for these so that one action does not produce two rows.
        /// </summary>
        private static readonly HashSet<string> SelfLogging = new(StringComparer.OrdinalIgnoreCase)
        {
            "SaveRegistration", "DeleteUser", "ConfirmPayment", "CancelPayment",
            "ApproveVerification", "RejectVerification",
            "ClearInactiveCryptoOrders", "ToggleEmailNotification", "TogglePaymentGate",
            // The backup runner writes the row itself, with the name of the file
            // it produced. The filter's own row would only say "OK".
            "CreateBackup"
        };

        /// <summary>
        /// Reads without side effects. These are called often (the health check
        /// every 30 seconds, lookups when a modal opens) and would bury the real
        /// changes under noise.
        /// </summary>
        private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
        {
            "HealthCheck", "FetchRejectionReason", "FetchUserAudits"
        };

        /// <summary>
        /// Parameter names whose values do NOT go into the audit log. The log is
        /// read by people and exported to CSV — a password or token that lands
        /// there leaks at once to everyone with access to the panel.
        /// </summary>
        private static readonly string[] Sensitive =
        {
            "password", "pass", "token", "secret", "apikey", "api_key",
            "connectionstring", "__requestverificationtoken"
        };

        /// <summary>
        /// Fields that carry whole documents — the HTML of a privacy policy,
        /// terms of use, descriptions.
        ///
        /// <para>
        /// They used to be recorded in full and made the log unreadable: a
        /// single row for a policy change took up 900 characters in which
        /// nothing could be seen. For such a field what matters is THAT it
        /// changed and by how much, not what it now says — the content itself
        /// is in the database anyway.
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

            // Only mutating actions. A GET lookup is not an event.
            var isMutating = HttpMethods.IsPost(http.Request.Method)
                             || HttpMethods.IsPut(http.Request.Method)
                             || HttpMethods.IsDelete(http.Request.Method);

            var shouldLog = isMutating
                            && !string.IsNullOrEmpty(action)
                            && !SelfLogging.Contains(action)
                            && !Ignored.Contains(action);

            // The arguments are read BEFORE execution: a handler can change
            // them, and the audit has to record what was asked for, not what was
            // left afterwards.
            //
            // Bound properties are read as well as HandlerArguments. Some
            // handlers take no parameters at all and receive their data through
            // [BindProperty] properties on the model (OnPostEditTicketAsync is
            // one); for those HandlerArguments is empty and the row used to read
            // just "OK (redirect)", with no trace of what had changed.
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

                // UserId is null, not string.Empty: the panel identifies the
                // administrator by the signed-in name, and "nobody" has one
                // spelling throughout the table.
                await _audit.LogAsync(null,
                    http.User?.Identity?.Name ?? "unknown",
                    action,
                    Truncate(details.ToString(), 900));
            }
            catch (Exception ex)
            {
                // A failed audit write must not fail the action itself — that has
                // already run and been saved.
                _logger.LogError(ex, "Неуспешен одит запис за {Action}.", action);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────

        /// <summary>Turns a handler method name into an action name:
        /// OnPostSaveLecturerAsync → SaveLecturer.</summary>
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
        /// Reads the values of the page's [BindProperty] properties. Only those
        /// with the attribute — anything wider would dump the whole model,
        /// including every list OnGet has filled.
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

                // Objects are unfolded one level: a [BindProperty] usually
                // points at a model (EditTicket, StudentInput), and it is that
                // model's fields that the administrator changed.
                var text = value is string || value.GetType().IsPrimitive
                           || value is decimal || value is DateTime
                    ? DescribeValue(value)
                    : DescribeObject(value);

                if (!string.IsNullOrEmpty(text)) parts.Add($"{prop.Name}={{{text}}}");
            }
            return string.Join(", ", parts);
        }

        /// <summary>Unfolds a simple object into "field=value" pairs, one level
        /// deep.</summary>
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

                // Eight fields are enough to see what happened; more makes the
                // row unreadable.
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
                // Bulk text fields are summarised rather than recorded whole.
                // 120 characters is where a value stops being a value and starts
                // being a document.
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
            // Complex objects (whole models) are not unfolded here — the row has
            // to stay readable, and the name of the type says enough.
            _ when v.GetType().IsPrimitive || v is decimal || v is DateTime => v.ToString(),
            System.Collections.ICollection c => $"[{c.Count} елемента]",
            _                        => $"[{v.GetType().Name}]"
        };

        private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;

        private static string DescribeResult(Microsoft.AspNetCore.Mvc.IActionResult? result) => result switch
        {
            Microsoft.AspNetCore.Mvc.JsonResult j    => DescribeJson(j),
            Microsoft.AspNetCore.Mvc.RedirectToPageResult => "OK (redirect)",
            // PageResult lives in RazorPages, not in Mvc, unlike the rest here.
            Microsoft.AspNetCore.Mvc.RazorPages.PageResult => "OK (page)",
            Microsoft.AspNetCore.Mvc.FileResult      => "OK (file)",
            null                                     => "OK",
            _                                        => "OK"
        };

        /// <summary>
        /// Most handlers return <c>{ success = bool, message = string }</c>.
        /// The audit has to tell "clicked" apart from "worked".
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
