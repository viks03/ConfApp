using System.Diagnostics;
using ConferenceApp.Data;
using ConferenceApp.Services.Email;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Services.Health
{
    public interface IHealthCheckService
    {
        /// <summary>Ключовете на всички проверявани услуги, в реда на екрана.</summary>
        IReadOnlyList<string> Keys { get; }

        Task<HealthResult> CheckAsync(string key, CancellationToken ct = default);
        Task<HealthReport> CheckAllAsync(CancellationToken ct = default);
    }

    public sealed class HealthCheckService : IHealthCheckService
    {
        // Таван за всяка проверка поотделно. Без него бавен SMTP би държал
        // заявката отворена, докато HTTP стекът не я отреже — и картата
        // остава да се върти без обяснение.
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

        // Над това време услугата работи, но отговаря бавно → warn.
        private const long SlowMs = 2500;

        private readonly IServiceScopeFactory _scopes;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly IBackgroundTaskQueue _queue;
        private readonly IEmailTemplateRenderer _templates;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<HealthCheckService> _logger;

        public HealthCheckService(
            IServiceScopeFactory scopes,
            IConfiguration config,
            IWebHostEnvironment env,
            IBackgroundTaskQueue queue,
            IEmailTemplateRenderer templates,
            IHttpClientFactory httpFactory,
            ILogger<HealthCheckService> logger)
        {
            _scopes = scopes;
            _config = config;
            _env = env;
            _queue = queue;
            _templates = templates;
            _httpFactory = httpFactory;
            _logger = logger;
        }

        public IReadOnlyList<string> Keys { get; } = new[]
        {
            "database", "smtp", "stripe", "go28",
            "emailQueue", "disk", "backups", "templates"
        };

        private static string NameOf(string key) => key switch
        {
            "database"   => "База данни",
            "smtp"       => "SMTP (Office 365)",
            "stripe"     => "Stripe",
            "go28"       => "Go28 (крипто)",
            "emailQueue" => "Фонова опашка за имейли",
            "disk"       => "Дисково пространство",
            "backups"    => "Резервни копия",
            "templates"  => "Имейл темплейти",
            _            => key
        };

        // ═══════════════════════════════════════════════════════════════════
        //  Вход
        // ═══════════════════════════════════════════════════════════════════

        public async Task<HealthReport> CheckAllAsync(CancellationToken ct = default)
        {
            // Паралелно — иначе осем последователни проверки по 8 s таван
            // означават потенциално минута чакане.
            var tasks = Keys.Select(k => CheckAsync(k, ct)).ToArray();
            var results = await Task.WhenAll(tasks);
            return new HealthReport { Services = results.ToList() };
        }

        public async Task<HealthResult> CheckAsync(string key, CancellationToken ct = default)
        {
            var name = NameOf(key);
            var sw = Stopwatch.StartNew();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);

            try
            {
                var result = key switch
                {
                    "database"   => await CheckDatabaseAsync(cts.Token),
                    "smtp"       => await CheckSmtpAsync(cts.Token),
                    "stripe"     => await CheckStripeAsync(cts.Token),
                    "go28"       => await CheckGo28Async(cts.Token),
                    "emailQueue" => CheckEmailQueue(),
                    "disk"       => CheckDisk(),
                    "backups"    => CheckBackups(),
                    "templates"  => await CheckTemplatesAsync(cts.Token),
                    _            => HealthResult.Create(key, name, HealthState.Fail,
                                        "Няма такава проверка.",
                                        $"Ключът „{key}“ не е сред познатите: {string.Join(", ", Keys)}.")
                };

                sw.Stop();

                // Времето се мери тук, за да е еднакво за всички проверки, и
                // само ако самата проверка не е задала собствено.
                return result.ResponseMs is not null
                    ? result
                    : Clone(result, sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                sw.Stop();
                return HealthResult.Create(key, name, HealthState.Fail,
                    $"Проверката не приключи за {Timeout.TotalSeconds:0} секунди.",
                    "Услугата или не отговаря, или отговаря твърде бавно, за да е използваема.",
                    sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Health check за {Key} се провали неочаквано.", key);
                return HealthResult.Create(key, name, HealthState.Fail,
                    "Проверката се провали неочаквано.",
                    $"{ex.GetType().Name}: {ex.Message}",
                    sw.ElapsedMilliseconds);
            }
        }

        private static HealthResult Clone(HealthResult r, long ms) => new()
        {
            Key = r.Key, Name = r.Name, Status = r.Status, Message = r.Message,
            Hint = r.Hint, CheckedAt = r.CheckedAt, Details = r.Details, ResponseMs = ms
        };

        // ═══════════════════════════════════════════════════════════════════
        //  1. База данни
        // ═══════════════════════════════════════════════════════════════════

        private async Task<HealthResult> CheckDatabaseAsync(CancellationToken ct)
        {
            const string key = "database";
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (!await db.Database.CanConnectAsync(ct))
                return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                    "Приложението не може да се свърже с базата.",
                    "Провери ConnectionStrings:DefaultConnection и дали файлът на базата съществува и е достъпен за запис.");

            var users     = await db.Users.CountAsync(ct);
            var payments  = await db.Users.CountAsync(u => u.PaymentStatus == "Confirmed", ct);
            var pendingV  = await db.Users.CountAsync(u => u.VerificationStatus == "Pending", ct);

            // Незавършените миграции са тиха бомба: приложението работи,
            // докато някой не докосне липсваща колона.
            var pendingMigrations = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

            var details = new List<HealthDetail>
            {
                new("Регистрации", users.ToString()),
                new("Потвърдени плащания", payments.ToString()),
                new("Чакащи верификации", pendingV.ToString())
            };

            if (pendingMigrations.Count > 0)
            {
                details.Add(new("Неприложени миграции", pendingMigrations.Count.ToString()));
                return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                    $"Базата отговаря, но има {pendingMigrations.Count} неприложени миграции.",
                    "Пусни dotnet ef database update. Дотогава части от приложението може да гърмят при достъп до нови колони.",
                    details: details);
            }

            return HealthResult.Create(key, NameOf(key), HealthState.Ok,
                "Базата отговаря и схемата е актуална.",
                details: details);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  2. SMTP — свързване и удостоверяване, БЕЗ изпращане
        // ═══════════════════════════════════════════════════════════════════

        private async Task<HealthResult> CheckSmtpAsync(CancellationToken ct)
        {
            const string key = "smtp";

            var host = _config["EmailSettings:Host"];
            var user = _config["EmailSettings:UserName"];
            var pass = _config["EmailSettings:Password"];
            var portRaw = _config["EmailSettings:Port"];
            var ssl = !bool.TryParse(_config["EmailSettings:EnableSsl"], out var s) || s;

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user))
                return HealthResult.Create(key, NameOf(key), HealthState.Unconfigured,
                    "Липсва SMTP сървър или потребител.",
                    "Попълни EmailSettings:Host и EmailSettings:UserName в appsettings.");

            if (string.IsNullOrWhiteSpace(pass))
                return HealthResult.Create(key, NameOf(key), HealthState.Unconfigured,
                    "Паролата за SMTP е празна.",
                    "Попълни EmailSettings:Password. Без нея никакъв имейл не тръгва — нито кодове за вход, нито потвърждения.");

            if (!int.TryParse(portRaw, out var port) || port <= 0) port = 587;

            var sw = Stopwatch.StartNew();
            SmtpProbe.Probe probe;
            try
            {
                probe = await SmtpProbe.RunAsync(host, port, ssl, user, pass, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                sw.Stop();
                return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                    "Няма връзка със SMTP сървъра.",
                    $"{ex.GetType().Name}: {ex.Message}",
                    sw.ElapsedMilliseconds,
                    new List<HealthDetail> { new("Сървър", $"{host}:{port}") });
            }
            sw.Stop();

            var details = new List<HealthDetail>
            {
                new("Сървър", $"{host}:{port}"),
                new("Потребител", user),
                new("TLS", probe.TlsEstablished ? "да" : "не")
            };

            if (!probe.Authenticated)
            {
                // 535 е отказано удостоверяване — най-честият реален проблем.
                var isAuthReject = probe.FailureCode is "535" or "534" or "530";
                return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                    isAuthReject
                        ? "Сървърът отказва удостоверяване с тези данни."
                        : "Връзката се осъществи, но удостоверяването не приключи успешно.",
                    isAuthReject
                        ? "Паролата е сгрешена или изтекла. При Office 365 това обикновено значи, че трябва app password или че basic auth е изключен за акаунта."
                        : $"Отговор от сървъра: {probe.FailureText}",
                    sw.ElapsedMilliseconds, details);
            }

            if (!probe.TlsEstablished && ssl)
                return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                    "Удостоверяването мина, но връзката не е шифрована.",
                    "EnableSsl е включено, а STARTTLS не се осъществи. Данните пътуват в явен вид.",
                    sw.ElapsedMilliseconds, details);

            if (sw.ElapsedMilliseconds > SlowMs)
                return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                    $"Работи, но бавно: {sw.ElapsedMilliseconds / 1000.0:0.0} s за свързване и удостоверяване.",
                    "Изпращането на имейли ще се бави. Проверката минава през фонова опашка, така че потребителите не чакат — но писмата ще пристигат по-късно.",
                    sw.ElapsedMilliseconds, details);

            return HealthResult.Create(key, NameOf(key), HealthState.Ok,
                "Връзката и удостоверяването минават успешно.",
                "Проверката стига до отговора на сървъра за AUTH и приключва с QUIT. Никакво писмо не се изпраща.",
                sw.ElapsedMilliseconds, details);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  3. Stripe
        // ═══════════════════════════════════════════════════════════════════

        private async Task<HealthResult> CheckStripeAsync(CancellationToken ct)
        {
            const string key = "stripe";
            var secret = _config["Stripe:SecretKey"];
            var publishable = _config["Stripe:PublishableKey"];
            var webhook = _config["Stripe:WebhookSecret"];

            if (string.IsNullOrWhiteSpace(secret))
                return HealthResult.Create(key, NameOf(key), HealthState.Unconfigured,
                    "Тайният ключ за Stripe е празен.",
                    "Попълни Stripe:SecretKey. Без него плащането с карта не работи изобщо.");

            var sw = Stopwatch.StartNew();
            try
            {
                // Balance е най-леката удостоверена заявка в Stripe API —
                // само чете и НЕ създава нищо. CreatePaymentIntent би оставял
                // боклук в акаунта при всяко натискане на „Провери“.
                var svc = new Stripe.BalanceService(new Stripe.StripeClient(secret));
                var balance = await svc.GetAsync(cancellationToken: ct);
                sw.Stop();

                var live = balance.Livemode;
                var details = new List<HealthDetail>
                {
                    new("Режим", live ? "live" : "test"),
                    new("Publishable ключ", string.IsNullOrWhiteSpace(publishable) ? "липсва" : "зададен"),
                    new("Webhook тайна", string.IsNullOrWhiteSpace(webhook) ? "липсва" : "зададена")
                };

                // Ключът работи, но webhook тайната липсва: плащането ще мине,
                // а потвърждението от Stripe няма да може да се провери.
                if (string.IsNullOrWhiteSpace(webhook))
                    return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                        "Ключът работи, но webhook тайната липсва.",
                        "Без Stripe:WebhookSecret потвържденията от Stripe не могат да се проверят и плащанията ще остават непотвърдени.",
                        sw.ElapsedMilliseconds, details);

                // Тестов ключ в продукция е лесен за пропускане и скъп.
                if (!live && !_env.IsDevelopment())
                    return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                        "Работи, но с тестов ключ извън среда за разработка.",
                        "Реални плащания няма да минават. Смени Stripe:SecretKey с live ключ.",
                        sw.ElapsedMilliseconds, details);

                if (sw.ElapsedMilliseconds > SlowMs)
                    return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                        $"Отговаря, но бавно: {sw.ElapsedMilliseconds / 1000.0:0.0} s.",
                        "Ако се задържи, провери status.stripe.com преди да търсиш проблем при нас.",
                        sw.ElapsedMilliseconds, details);

                return HealthResult.Create(key, NameOf(key), HealthState.Ok,
                    "Ключът е валиден и API отговаря.",
                    responseMs: sw.ElapsedMilliseconds, details: details);
            }
            catch (OperationCanceledException) { throw; }
            catch (Stripe.StripeException ex)
            {
                sw.Stop();
                var auth = ex.StripeError?.Type == "invalid_request_error"
                           || ex.HttpStatusCode == System.Net.HttpStatusCode.Unauthorized;
                return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                    auth ? "Stripe отхвърля ключа." : "Stripe върна грешка.",
                    ex.StripeError?.Message ?? ex.Message,
                    sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                    "Няма връзка със Stripe.",
                    $"{ex.GetType().Name}: {ex.Message}",
                    sw.ElapsedMilliseconds);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  4. Go28 (крипто)
        // ═══════════════════════════════════════════════════════════════════

        private async Task<HealthResult> CheckGo28Async(CancellationToken ct)
        {
            const string key = "go28";
            var baseUrl = _config["Go28:BaseUrl"];
            var token = _config["Go28:ApiToken"];

            if (string.IsNullOrWhiteSpace(token))
                return HealthResult.Create(key, NameOf(key), HealthState.Unconfigured,
                    "API токенът за Go28 е празен.",
                    "Попълни Go28:ApiToken. Без него плащането с криптовалута не работи.");

            if (string.IsNullOrWhiteSpace(baseUrl))
                return HealthResult.Create(key, NameOf(key), HealthState.Unconfigured,
                    "Липсва адрес на Go28 API.",
                    "Попълни Go28:BaseUrl.");

            var sw = Stopwatch.StartNew();
            try
            {
                // GET gateway/currencies е същият, който Payment страницата вика
                // при зареждане — леко, само четене, без създаване на поръчка.
                // Не минаваме през Go28Service нарочно: той поглъща грешката и
                // връща празен списък, а тук трябва да различим 401 от 404 от
                // прекъсната връзка.
                using var http = _httpFactory.CreateClient();
                http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
                http.DefaultRequestHeaders.Add("x-api-token", token);
                http.DefaultRequestHeaders.Accept.Add(new("application/json"));

                using var resp = await http.GetAsync("gateway/currencies", ct);
                sw.Stop();

                var details = new List<HealthDetail>
                {
                    new("Адрес", baseUrl),
                    new("HTTP", ((int)resp.StatusCode).ToString())
                };

                if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                        "Go28 отхвърля API токена.",
                        "Токенът е невалиден или отнет. Провери Go28:ApiToken.",
                        sw.ElapsedMilliseconds, details);

                if (!resp.IsSuccessStatusCode)
                    return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                        $"Go28 върна {(int)resp.StatusCode}.",
                        await SafeBodyAsync(resp, ct),
                        sw.ElapsedMilliseconds, details);

                // БЪГ ФИКС: тук се броеше низът "currency" с регулярен израз.
                // Такова поле в отговора НЯМА — Go28 връща масив от обекти с
                // "iso" и "network" (виж Go28Currency в Go28Service.cs).
                // Затова броят излизаше 0 при всеки успешен отговор и
                // проверката винаги завършваше с предупреждение.
                //
                // Сега отговорът се десериализира със същия модел, който
                // ползва и самото приложение — ако някога се смени, двете ще
                // се счупят заедно и разминаването ще е видимо.
                var body = await resp.Content.ReadAsStringAsync(ct);

                List<Go28Currency> currencies;
                try
                {
                    currencies = System.Text.Json.JsonSerializer
                        .Deserialize<List<Go28Currency>>(body) ?? new();
                }
                catch (System.Text.Json.JsonException ex)
                {
                    return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                        "Отговаря, но отговорът не е в очаквания формат.",
                        $"Не можах да разчета списъка с валути: {ex.Message}. Възможно е Go28 да са сменили формата на API-то.",
                        sw.ElapsedMilliseconds, details);
                }

                if (currencies.Count > 0)
                {
                    details.Add(new("Налични валути", currencies.Count.ToString()));

                    // Кои точно — при авария при тях може да изчезне само една.
                    var isoList = string.Join(", ", currencies
                        .Select(x => x.Iso)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct()
                        .OrderBy(x => x));
                    if (isoList.Length > 0) details.Add(new("Символи", isoList));

                    // Приложението предлага точно тези четири. Ако някоя липсва
                    // от отговора, бутонът ѝ изчезва мълчаливо от страницата.
                    var expected = new[] { "BTC", "ETH", "EURC", "USDC" };
                    var missing = expected
                        .Where(e => !currencies.Any(x => string.Equals(x.Iso, e, StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    if (missing.Count > 0)
                        return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                            $"Работи, но {missing.Count} от предлаганите валути липсват в отговора.",
                            $"Няма ги: {string.Join(", ", missing)}. Бутоните им няма да се показват на страницата за плащане.",
                            sw.ElapsedMilliseconds, details);
                }

                if (currencies.Count == 0)
                    return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                        "Отговаря, но не връща нито една валута.",
                        "Плащането с крипто ще изглежда счупено за потребителя. Провери настройките на акаунта в Go28.",
                        sw.ElapsedMilliseconds, details);

                if (sw.ElapsedMilliseconds > SlowMs)
                    return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                        $"Отговаря, но бавно: {sw.ElapsedMilliseconds / 1000.0:0.0} s.",
                        "Създаването на крипто поръчка ще се бави за потребителя.",
                        sw.ElapsedMilliseconds, details);

                return HealthResult.Create(key, NameOf(key), HealthState.Ok,
                    "Токенът е валиден и API отговаря.",
                    responseMs: sw.ElapsedMilliseconds, details: details);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                sw.Stop();
                return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                    "Няма връзка с Go28.",
                    $"{ex.GetType().Name}: {ex.Message}",
                    sw.ElapsedMilliseconds);
            }
        }

        private static async Task<string> SafeBodyAsync(HttpResponseMessage r, CancellationToken ct)
        {
            try
            {
                var b = await r.Content.ReadAsStringAsync(ct);
                return b.Length > 300 ? b[..300] + "…" : b;
            }
            catch { return "(тялото на отговора не можа да се прочете)"; }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  5. Фонова опашка за имейли
        // ═══════════════════════════════════════════════════════════════════

        private HealthResult CheckEmailQueue()
        {
            const string key = "emailQueue";

            var pending = _queue.PendingCount;
            var running = _queue.ConsumerRunning;
            var last = _queue.LastActivityAt;

            var details = new List<HealthDetail>
            {
                new("Чакащи задачи", pending.ToString()),
                new("Консуматор", running ? "работи" : "спрян"),
                new("Последна активност", last is null ? "— (няма от старта)" : last.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"))
            };

            // Най-опасният случай: опашката приема работа мълчаливо, но никой
            // не я обработва. Няма грешка никъде — писмата просто не тръгват.
            if (!running)
                return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                    "Обработващата услуга не работи.",
                    "Задачите се трупат, но никой не ги изпълнява — нито един имейл няма да тръгне. Провери дали QueuedHostedService е регистриран в Program.cs и рестартирай приложението.",
                    details: details);

            if (pending > 50)
                return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                    $"Опашката е натрупала {pending} задачи.",
                    "Обработващата услуга работи, но не смогва — обикновено значи бавен или отказващ SMTP. Виж проверката за SMTP.",
                    details: details);

            if (pending > 0)
                return HealthResult.Create(key, NameOf(key), HealthState.Ok,
                    $"Работи, {pending} задачи чакат ред.",
                    details: details);

            return HealthResult.Create(key, NameOf(key), HealthState.Ok,
                "Опашката е празна и обработващата услуга върви.",
                details: details);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  6. Дисково пространство
        // ═══════════════════════════════════════════════════════════════════

        private HealthResult CheckDisk()
        {
            const string key = "disk";
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(_env.ContentRootPath));
                var drive = new DriveInfo(root!);

                var freeGb  = drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
                var totalGb = drive.TotalSize / 1024.0 / 1024 / 1024;
                var pct     = totalGb > 0 ? freeGb / totalGb * 100 : 0;

                var uploadsMb = FolderSizeMb(Path.Combine(_env.WebRootPath, "uploads"));

                var details = new List<HealthDetail>
                {
                    new("Свободно", $"{freeGb:0.0} GB от {totalGb:0.0} GB ({pct:0}%)"),
                    new("Качени файлове", $"{uploadsMb:0.0} MB")
                };

                // Праговете са консервативни нарочно: качването на доклад и
                // резервното копие на базата стават мълчаливо невъзможни,
                // когато дискът се напълни.
                if (freeGb < 1 || pct < 5)
                    return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                        $"Свободното място е критично малко: {freeGb:0.0} GB.",
                        "Качването на документи и резервните копия ще започнат да се провалят. Освободи място незабавно.",
                        details: details);

                if (freeGb < 3 || pct < 15)
                    return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                        $"Остават {freeGb:0.0} GB ({pct:0}%).",
                        "Има място, но е време да се изчистят стари резервни копия или качени файлове.",
                        details: details);

                return HealthResult.Create(key, NameOf(key), HealthState.Ok,
                    $"Свободни {freeGb:0.0} GB ({pct:0}%).",
                    details: details);
            }
            catch (Exception ex)
            {
                return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                    "Не можах да прочета данните за диска.",
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static double FolderSizeMb(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return 0;
                long bytes = 0;
                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { bytes += new FileInfo(f).Length; } catch { }
                }
                return bytes / 1024.0 / 1024;
            }
            catch { return 0; }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  7. Резервни копия
        // ═══════════════════════════════════════════════════════════════════

        private HealthResult CheckBackups()
        {
            const string key = "backups";
            try
            {
                var folder = Path.Combine(_env.ContentRootPath, "backups");
                var keepMax = int.TryParse(_config["BackupSettings:KeepMaxBackups"], out var k) ? k : 14;

                if (!Directory.Exists(folder))
                    return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                        "Папката с резервни копия не съществува.",
                        $"Очаква се {folder}. Копие още не е правено — при загуба на базата няма от какво да се възстанови.");

                var files = new DirectoryInfo(folder).GetFiles("*.db")
                                .OrderByDescending(f => f.LastWriteTimeUtc).ToList();

                if (files.Count == 0)
                    return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                        "Няма нито едно резервно копие.",
                        "При загуба на базата няма от какво да се възстанови. Провери дали DatabaseBackupService е регистриран и се изпълнява.");

                var newest = files[0];
                var ageH = (DateTime.UtcNow - newest.LastWriteTimeUtc).TotalHours;
                var totalMb = files.Sum(f => f.Length) / 1024.0 / 1024;

                var details = new List<HealthDetail>
                {
                    new("Брой копия", $"{files.Count} (пази се максимум {keepMax})"),
                    new("Последно", newest.LastWriteTime.ToString("dd.MM.yyyy HH:mm")),
                    new("Общ размер", $"{totalMb:0.0} MB")
                };

                // Копието се прави ежедневно; над два дни значи, че услугата
                // е спряла, без да се е оплакала.
                if (ageH > 48)
                    return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                        $"Последното копие е отпреди {ageH / 24:0} дни.",
                        "Копията се правят ежедневно — това значи, че услугата е спряла. Всичко, въведено след тази дата, е незащитено.",
                        details: details);

                if (ageH > 26)
                    return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                        $"Последното копие е отпреди {ageH:0} часа.",
                        "Очаква се ежедневно копие. Ако се задържи, провери логовете на DatabaseBackupService.",
                        details: details);

                if (newest.Length == 0)
                    return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                        "Последното копие е с нулев размер.",
                        "Файлът съществува, но е празен — възстановяване от него е невъзможно.",
                        details: details);

                return HealthResult.Create(key, NameOf(key), HealthState.Ok,
                    $"Последното копие е отпреди {ageH:0} часа.",
                    details: details);
            }
            catch (Exception ex)
            {
                return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                    "Не можах да прочета папката с копия.",
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  8. Имейл темплейти
        // ═══════════════════════════════════════════════════════════════════

        private async Task<HealthResult> CheckTemplatesAsync(CancellationToken ct)
        {
            const string key = "templates";

            var dir = Path.Combine(_env.WebRootPath, "templates");
            var layout = Path.Combine(dir, "_layout.html");
            var bodies = Path.Combine(dir, "bodies");

            var expected = new[]
            {
                "otp.html", "payment-confirmed.html", "payment-pending.html",
                "verification-approved.html", "verification-rejected.html", "status-changed.html"
            };

            var missing = new List<string>();
            if (!File.Exists(layout)) missing.Add("_layout.html");
            foreach (var f in expected)
                if (!File.Exists(Path.Combine(bodies, f))) missing.Add("bodies/" + f);

            var present = expected.Length - missing.Count(m => m.StartsWith("bodies/"));
            var details = new List<HealthDetail> { new("Тела", $"{present} / {expected.Length}") };

            if (missing.Count > 0)
                return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                    $"Липсват {missing.Count} файла.",
                    "Няма ги: " + string.Join(", ", missing),
                    details: details);

            // Реален рендер на един темплейт. Само наличието на файловете не
            // значи, че писмото ще се получи: renderer-ът отказва при повече от
            // един маркер за тяло и логва незаместени плейсхолдъри. По-добре
            // да гръмне тук, отколкото при истински имейл до потребител.
            try
            {
                var ph = new EmailPlaceholders()
                    .Set("EmailSubject", "health check")
                    .Set("Preheader", "health check")
                    .Set("FooterRights", "health check")
                    .SetRaw("BaseUrl", "https://example.invalid")
                    .Set("Greeting", "health check")
                    .SetRaw("MainText", "health check")
                    .Set("CodeLabel", "health check")
                    .Set("OtpCode", "000000")
                    .Set("WarningText", "health check");

                var html = await _templates.RenderAsync(EmailTemplate.Otp, ph, ct);
                details.Add(new("Пробен рендер", $"{html.Length / 1024} KB"));

                return HealthResult.Create(key, NameOf(key), HealthState.Ok,
                    "Всички темплейти са налични и се рендират.",
                    details: details);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                    "Файловете са налични, но рендерът се проваля.",
                    $"{ex.GetType().Name}: {ex.Message}",
                    details: details);
            }
        }
    }
}
