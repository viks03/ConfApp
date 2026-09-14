// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Diagnostics;
using ConferenceApp.Data;
using ConferenceApp.Services.Email;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Services.Health
{
    public interface IHealthCheckService
    {
        /// <summary>The keys of every checked service, in the order they appear
        /// on screen.</summary>
        IReadOnlyList<string> Keys { get; }

        Task<HealthResult> CheckAsync(string key, CancellationToken ct = default);
        Task<HealthReport> CheckAllAsync(CancellationToken ct = default);
    }

    public sealed class HealthCheckService : IHealthCheckService
    {
        // A ceiling for each check on its own. Without it a slow SMTP server
        // would hold the request open until the HTTP stack cut it off, and the
        // card in the panel would spin with no explanation.
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

        // Above this the service works but answers slowly → warn.
        private const long SlowMs = 2500;

        private readonly IServiceScopeFactory _scopes;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly IBackgroundTaskQueue _queue;
        private readonly IEmailTemplateRenderer _templates;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<HealthCheckService> _logger;
        private readonly ConferenceApp.Services.Files.IUploadPaths _uploadPaths;
        private readonly ICleanupStatus _cleanup;
        private readonly IBackupStatus _backups;

        public HealthCheckService(
            IServiceScopeFactory scopes,
            IConfiguration config,
            IWebHostEnvironment env,
            IBackgroundTaskQueue queue,
            IEmailTemplateRenderer templates,
            IHttpClientFactory httpFactory,
            ConferenceApp.Services.Files.IUploadPaths uploadPaths,
            ICleanupStatus cleanup,
            IBackupStatus backups,
            ILogger<HealthCheckService> logger)
        {
            _uploadPaths = uploadPaths;
            _scopes = scopes;
            _config = config;
            _env = env;
            _queue = queue;
            _templates = templates;
            _httpFactory = httpFactory;
            _cleanup = cleanup;
            _backups = backups;
            _logger = logger;
        }

        // The names are shown in the panel and stay in Bulgarian.
        // [S-04]: "cleanup" is the ninth check. Before it, a stopped cleanup
        // service and a quiet week looked the same — the service left a trace
        // only when it had deleted something, Health had no such key, and the
        // panel read that very row.
        public IReadOnlyList<string> Keys { get; } = new[]
        {
            "database", "smtp", "stripe", "go28",
            "emailQueue", "cleanup", "disk", "backups", "templates"
        };

        private static string NameOf(string key) => key switch
        {
            "database"   => "База данни",
            "smtp"       => "SMTP (Office 365)",
            "stripe"     => "Stripe",
            "go28"       => "Go28 (крипто)",
            "emailQueue" => "Фонова опашка за имейли",
            "cleanup"    => "Автоматично чистене",
            "disk"       => "Дисково пространство",
            "backups"    => "Резервни копия",
            "templates"  => "Имейл темплейти",
            _            => key
        };

        // ═══════════════════════════════════════════════════════════════════
        //  Entry points
        // ═══════════════════════════════════════════════════════════════════

        public async Task<HealthReport> CheckAllAsync(CancellationToken ct = default)
        {
            // In parallel — nine checks in sequence, each with an 8 s ceiling,
            // would mean a potential minute of waiting.
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
                    "cleanup"    => CheckCleanup(),
                    "disk"       => CheckDisk(),
                    "backups"    => CheckBackups(),
                    "templates"  => await CheckTemplatesAsync(cts.Token),
                    _            => HealthResult.Create(key, name, HealthState.Fail,
                                        "Няма такава проверка.",
                                        $"Ключът „{key}“ не е сред познатите: {string.Join(", ", Keys)}.")
                };

                sw.Stop();

                // The elapsed time is measured here so that it means the same
                // for every check — unless the check has already set its own,
                // which the network ones do to exclude their setup.
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
        //  1. Database
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

            // Unapplied migrations are a quiet bomb: the application runs until
            // somebody touches a column that is not there.
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
        //  2. SMTP — connect and authenticate, WITHOUT sending
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
                // 535 and its neighbours mean the credentials were refused —
                // the most common real problem, and worth its own message.
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
                // Balance is the lightest authenticated call in the Stripe API:
                // it only reads and creates NOTHING. CreatePaymentIntent would
                // leave rubbish in the account on every press of "Check".
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

                // The key works but the webhook secret is missing: the payment
                // will go through and Stripe's confirmation of it cannot be
                // verified.
                if (string.IsNullOrWhiteSpace(webhook))
                    return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                        "Ключът работи, но webhook тайната липсва.",
                        "Без Stripe:WebhookSecret потвържденията от Stripe не могат да се проверят и плащанията ще остават непотвърдени.",
                        sw.ElapsedMilliseconds, details);

                // A test key in production is easy to miss and expensive: every
                // payment appears to work and no money moves.
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
        //  4. Go28 (crypto)
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
                // GET gateway/currencies is the same call the Payment page makes
                // when it loads — light, read-only, and it creates no order.
                // Go28Service is deliberately bypassed: it swallows the error and
                // returns an empty list, while here a 401 has to be told apart
                // from a 404 and from a broken connection.
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

                // This used to count occurrences of the string "currency" with a
                // regular expression. The response has no such field — the
                // gateway returns an array of objects with "iso" and "network"
                // (see Go28Currency in Go28Service.cs). The count therefore came
                // out as 0 for every successful response and the check always
                // ended in a warning.
                //
                // The response is now deserialized with the same model the
                // application itself uses: if it ever changes, the two break
                // together and the mismatch is visible.
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

                    // Which ones exactly: a fault on their side can remove a
                    // single currency rather than all of them.
                    var isoList = string.Join(", ", currencies
                        .Select(x => x.Iso)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct()
                        .OrderBy(x => x));
                    if (isoList.Length > 0) details.Add(new("Символи", isoList));

                    // The application offers exactly these four. If one is missing
                    // from the response, its button disappears from the page
                    // without a word.
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
        //  5. The background mail queue
        // ═══════════════════════════════════════════════════════════════════

        private HealthResult CheckEmailQueue()
        {
            const string key = "emailQueue";

            var pending = _queue.PendingCount;
            var running = _queue.ConsumerRunning;
            var last = _queue.LastActivityAt;

            // [E-02]: a failed mail was logged and forgotten — this check read
            // only whether the queue was moving, so the card said "all fine" in
            // exactly the case where nothing had gone out.
            var failed    = _queue.FailedCount;
            var succeeded = _queue.SucceededCount;
            var lastFail  = _queue.LastFailureAt;
            var lastFailMsg = _queue.LastFailureMessage;

            var details = new List<HealthDetail>
            {
                new("Чакащи задачи", pending.ToString()),
                new("Консуматор", running ? "работи" : "спрян"),
                new("Последна активност", last is null ? "— (няма от старта)" : last.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")),
                new("Изпратени / провалени", $"{succeeded} / {failed} (от старта на процеса)"),
                new("Последен провал", lastFail is null
                        ? "— (няма)"
                        : $"{lastFail.Value.ToLocalTime():dd.MM.yyyy HH:mm:ss} — {lastFailMsg}")
            };

            // The most dangerous case: the queue accepts work silently while
            // nobody consumes it. There is no error anywhere — the mail simply
            // never goes out.
            if (!running)
                return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                    "Обработващата услуга не работи.",
                    "Задачите се трупат, но никой не ги изпълнява — нито един имейл няма да тръгне. Провери дали QueuedHostedService е регистриран в Program.cs и рестартирай приложението.",
                    details: details);

            // A failure after every retry means somebody did not get their
            // mail — a participant without a confirmation, or a user without an
            // OTP code. The threshold is deliberately zero: this is not noise to
            // be tolerated. Only the last 24 hours count, so that one bad night
            // does not leave the card red for good.
            if (failed > 0 && lastFail is not null && (DateTime.UtcNow - lastFail.Value) < TimeSpan.FromHours(24))
                return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                    $"{failed} писма не тръгнаха (последното в {lastFail.Value.ToLocalTime():HH:mm}).",
                    "Задачата е опитала три пъти и се е отказала. Получателят няма писмо, а действието му е записано като успешно. " +
                    $"Причина на последния провал: {lastFailMsg}. Виж и проверката за SMTP.",
                    details: details);

            if (failed > 0)
                return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                    $"{failed} писма не тръгнаха, но не в последните 24 часа.",
                    $"Последен провал: {lastFail?.ToLocalTime():dd.MM.yyyy HH:mm} — {lastFailMsg}. " +
                    "Броячът се нулира при рестарт на приложението.",
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
        //  6. The cleanup service ([S-04])
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// CleanupService used to leave a trace only when it had deleted
        /// something, so a stopped service and a quiet week looked the same. This
        /// check reads the state straight from the service (the way emailQueue
        /// reads ConsumerRunning), without going through the database.
        /// </summary>
        private HealthResult CheckCleanup()
        {
            const string key = "cleanup";

            var running   = _cleanup.Running;
            var interval  = _cleanup.Interval;
            var lastRun   = _cleanup.LastRunAt;
            var lastOk    = _cleanup.LastSuccessAt;
            var lastError = _cleanup.LastError;

            var details = new List<HealthDetail>
            {
                new("Услуга", running ? "работи" : "спряна"),
                new("Интервал", $"на всеки {interval.TotalHours:0.#} ч."),
                new("Последен цикъл", lastRun is null
                        ? "— (няма от старта)"
                        : lastRun.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")),
                new("Последен успешен", lastOk is null
                        ? "— (няма от старта)"
                        : lastOk.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")),
                new("Последният цикъл хвана", $"{_cleanup.LastDeletedAccounts} профила, {_cleanup.LastExpiredOrders} крипто поръчки"),
                new("Цикли / провали", $"{_cleanup.TotalCycles} / {_cleanup.TotalFailures} (от старта на процеса)")
            };

            if (!running)
                return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                    "Услугата за чистене не работи.",
                    "Изтеклите крипто поръчки остават „InProcess“, а изоставените профили се трупат без ограничение. " +
                    "Провери дали CleanupService е регистриран в Program.cs и виж лога за изключение при старта ѝ.",
                    details: details);

            if (lastOk is null)
            {
                // The service has started but no cycle has finished successfully
                // yet. The first one runs immediately after startup, so this
                // state lasts seconds — unless that first cycle threw.
                if (lastError is not null)
                    return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                        "Нито един цикъл не е приключил успешно.",
                        $"Последната грешка: {lastError}",
                        details: details);

                return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                    "Услугата върви, но още не е приключила цикъл.",
                    "Нормално е веднага след рестарт. Ако се задържи, виж лога.",
                    details: details);
            }

            var since = DateTime.UtcNow - lastOk.Value;

            // One missed cycle is a warning, two are a failure. The threshold is
            // computed from the service's own interval, so that changing the
            // number cannot leave the two disagreeing — which is what [S-03] was
            // about elsewhere.
            if (since > interval + interval)
                return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                    $"Последният успешен цикъл е отпреди {since.TotalHours:0.#} ч.",
                    $"Очаква се цикъл на всеки {interval.TotalHours:0.#} ч. Услугата се води работеща, но не приключва. " +
                    (lastError is null ? "В лога няма записана грешка — виж дали не виси на заявка към базата."
                                       : $"Последна грешка: {lastError}"),
                    details: details);

            if (lastError is not null)
                return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                    "Последният цикъл се провали.",
                    $"{lastError} Предишният е минал в {lastOk.Value.ToLocalTime():dd.MM.yyyy HH:mm}.",
                    details: details);

            if (since > interval)
                return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                    $"Последният цикъл е отпреди {since.TotalMinutes:0} мин.",
                    $"Очаква се на всеки {interval.TotalHours:0.#} ч. Още не е закъснял критично.",
                    details: details);

            return HealthResult.Create(key, NameOf(key), HealthState.Ok,
                $"Последният цикъл е отпреди {since.TotalMinutes:0} мин.",
                details: details);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  7. Disk space
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

                // The two folders with personal data now live outside wwwroot
                // ([F-02]), so they are measured separately — otherwise the
                // figure would appear to drop for no reason.
                var uploadsMb = FolderSizeMb(Path.Combine(_env.WebRootPath, "uploads"))
                              + FolderSizeMb(Path.Combine(_uploadPaths.PrivateRoot, "uploads"));

                var details = new List<HealthDetail>
                {
                    new("Свободно", $"{freeGb:0.0} GB от {totalGb:0.0} GB ({pct:0}%)"),
                    new("Качени файлове", $"{uploadsMb:0.0} MB")
                };

                // The thresholds are deliberately cautious: uploading a paper and
                // backing the database up both become silently impossible once
                // the disk fills.
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
        //  8. Backups
        // ═══════════════════════════════════════════════════════════════════

        private HealthResult CheckBackups()
        {
            const string key = "backups";
            try
            {
                // [S-02]: the path comes from the same source as in
                // DatabaseBackupService — otherwise the two could be looking at
                // different files.
                var dbPath  = DatabaseLocation.ResolveDatabaseFile(_config);
                var folder  = DatabaseLocation.ResolveBackupFolder(_env);
                var keepMax = int.TryParse(_config["BackupSettings:KeepMaxBackups"], out var k) ? k : 14;

                // [T-36]: the card used to judge by the folder alone, so
                // "no copies" always read as "the service is broken". It is now
                // asked directly whether it is alive and when it next runs —
                // the same thing the cleanup check does through ICleanupStatus.
                var running   = _backups.Running;
                var nextRun   = _backups.NextRunAt;
                var lastError = _backups.LastError;

                // The state of the service, which the folder cannot answer. It
                // goes on every branch below, not only the empty ones.
                var serviceDetails = new List<HealthDetail>
                {
                    new("Услуга", running ? "работи" : "спряна"),
                    new("Следващо копие", NextBackupText(running, nextRun))
                };

                if (!Directory.Exists(folder))
                    return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                        "Папката с резервни копия не съществува.",
                        $"Очаква се {folder}. Папката се създава при старта на приложението, " +
                        (running
                            ? "а услугата върви — значи създаването ѝ се е провалило. Провери правата върху папката и свободното място."
                            : "но услугата не върви. Провери дали DatabaseBackupService е регистриран в Program.cs и виж лога за изключение при старта ѝ.") +
                        " Дотогава при загуба на базата няма от какво да се възстанови.",
                        details: serviceDetails);

                var dir = new DirectoryInfo(folder);

                // [S-01]: the pattern used to be "*.db" across the whole folder.
                // Only the copies the service made itself are counted now
                // (<database>_yyyyMMdd_HHmm.db) — a copy left by hand before a
                // migration does not count as automatic, and an unfinished
                // .db.tmp does not count at all.
                var files = DatabaseLocation.ListAutomaticBackups(folder, dbPath);

                if (files.Count == 0)
                {
                    // [T-36]: three different situations used to share one
                    // message, and that message named a culprit.
                    //
                    // A failed attempt is a fault whether the loop is still
                    // turning or not: the service tried and could not.
                    if (lastError is not null)
                        return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                            "Нито едно копие не е направено успешно.",
                            $"Последният опит се провали: {lastError} " +
                            "При загуба на базата няма от какво да се възстанови.",
                            details: serviceDetails);

                    // The service is not running. This is the one case the old
                    // message was right about.
                    if (!running)
                        return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                            "Няма нито едно резервно копие, а услугата не работи.",
                            "При загуба на базата няма от какво да се възстанови. " +
                            "Провери дали DatabaseBackupService е регистриран в Program.cs и виж лога за изключение при старта ѝ.",
                            details: serviceDetails);

                    // The service is running, nothing has failed, the first
                    // window simply has not come yet — the normal state of a
                    // freshly started application, since the windows are 03:00
                    // and 15:00 UTC and a restart can be up to twelve hours
                    // before either.
                    //
                    // A warning rather than a problem: the data really is
                    // unprotected until the first copy, so the card must not go
                    // green — but there is nothing broken to go looking for.
                    return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                        nextRun is null
                            ? "Услугата върви, но още не е правила копие."
                            : $"Услугата върви; първото копие е след {WaitHours(nextRun.Value):0.#} ч. " +
                              $"(в {nextRun.Value.ToLocalTime():HH:mm}).",
                        "Копия се правят в 03:00 и 15:00 UTC, така че след рестарт се чака до първия прозорец — " +
                        "това не е повреда. Дотогава при загуба на базата няма от какво да се възстанови; " +
                        "бутонът „Копие сега“ прави едно веднага.",
                        details: serviceDetails);
                }

                var newest = files[0];
                var ageH = (DateTime.UtcNow - newest.LastWriteTimeUtc).TotalHours;
                var totalMb = files.Sum(f => f.Length) / 1024.0 / 1024;

                long liveBytes = 0;
                try { if (File.Exists(dbPath)) liveBytes = new FileInfo(dbPath).Length; } catch { }

                // Unfinished copies from an interrupted cycle. The service clears
                // them at the next backup, but if it has stopped they stay — and
                // that is precisely the signal that it stopped halfway.
                var partials = dir.GetFiles("*.db" + DatabaseLocation.PartialExtension).Length;

                // The state of the service leads here too: a green card still
                // has to say when the next copy is due.
                List<HealthDetail> details =
                [
                    .. serviceDetails,
                    new("Брой копия", $"{files.Count} (пази се максимум {keepMax})"),
                    new("Последно", $"{newest.Name} — {newest.LastWriteTime:dd.MM.yyyy HH:mm}"),
                    new("Размер на последното", $"{newest.Length / 1024.0 / 1024:0.0} MB"),
                    new("Размер на живата база", liveBytes > 0 ? $"{liveBytes / 1024.0 / 1024:0.0} MB" : "— (не е намерена)"),
                    new("Общ размер", $"{totalMb:0.0} MB"),
                    new("Недовършени файлове", partials.ToString())
                ];

                // [S-01]: the content check used to come AFTER the age checks and
                // was only `== 0`, so a truncated 40 MB copy passed as valid,
                // while a file that was both old and empty reported only "old
                // backup". The size is checked first now, against the live
                // database.
                if (newest.Length == 0)
                    return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                        "Последното копие е с нулев размер.",
                        "Файлът съществува, но е празен — възстановяване от него е невъзможно.",
                        details: details);

                if (liveBytes > 0 && newest.Length < liveBytes / 2)
                    return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                        $"Последното копие е {newest.Length / 1024.0 / 1024:0.0} MB при база от {liveBytes / 1024.0 / 1024:0.0} MB.",
                        "Копието е чувствително по-малко от базата — почти сигурно е прекъснато по средата (спрян процес или пълен диск). " +
                        "Възстановяване от него ще даде непълни данни. Провери свободното място и лога на DatabaseBackupService.",
                        details: details);

                // Backups run twice a day; more than two days old means the
                // service has stopped without complaining about it.
                if (ageH > 48)
                    return HealthResult.Create(key, NameOf(key), HealthState.Fail,
                        $"Последното копие е отпреди {ageH / 24:0} дни.",
                        "Копията се правят два пъти дневно. " +
                        (running
                            ? "Услугата се води работеща, но не е направила копие — " +
                              (lastError is null
                                  ? "виж логовете на DatabaseBackupService."
                                  : $"последна грешка: {lastError}")
                            : "Услугата е спряла.") +
                        " Всичко, въведено след тази дата, е незащитено.",
                        details: details);

                if (ageH > 26)
                    return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                        $"Последното копие е отпреди {ageH:0} часа.",
                        "Очаква се ежедневно копие. " +
                        (running
                            ? "Ако се задържи, провери логовете на DatabaseBackupService."
                            : "Услугата не работи — провери дали DatabaseBackupService е регистриран в Program.cs."),
                        details: details);

                if (partials > 0)
                    return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                        $"В папката стоят {partials} недовършени файла.",
                        "Копие е било прекъснато по средата. Валидното последно копие не е засегнато, но причината (спиране по време на цикъла или пълен диск) си остава.",
                        details: details);

                // [T-36] The other half of the finding. A stopped service with a
                // fresh copy in the folder used to leave the card green until
                // the file itself grew old — 26 hours before so much as a
                // warning. Now that the service can be asked, the moment it
                // stops is the moment the card says so: the copies on disk are
                // fine, but there will not be another one.
                if (!running)
                    return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                        $"Последното копие е отпреди {ageH:0} часа, но услугата не работи.",
                        "Наличните копия са наред; ново няма да бъде направено. " +
                        "Провери дали DatabaseBackupService е регистриран в Program.cs и виж лога за изключение при старта ѝ.",
                        details: details);

                if (lastError is not null)
                    return HealthResult.Create(key, NameOf(key), HealthState.Warn,
                        "Последният опит за копие се провали.",
                        $"{lastError} Последното успешно копие е отпреди {ageH:0} часа и е наред.",
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

        /// <summary>Hours until the next backup window; never negative, so that
        /// a window a second in the past does not read as "-0 ч.".</summary>
        private static double WaitHours(DateTime nextRunUtc)
            => Math.Max(0, (nextRunUtc - DateTime.UtcNow).TotalHours);

        /// <summary>
        /// "Следващо копие" as a person reads it. A stopped service has no next
        /// copy at all, and saying so is the point of the row.
        /// </summary>
        private static string NextBackupText(bool running, DateTime? nextRunUtc)
        {
            if (!running) return "— (услугата не работи)";
            if (nextRunUtc is null) return "— (още не е изчислено)";

            var local = nextRunUtc.Value.ToLocalTime();
            return $"{local:dd.MM HH:mm} (след {WaitHours(nextRunUtc.Value):0.#} ч.)";
        }

        // ═══════════════════════════════════════════════════════════════════
        //  9. Mail templates
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

            // One template is actually rendered. The files merely being present
            // does not mean a mail will come out: the renderer refuses when the
            // body marker appears more than once, and logs placeholders left
            // unsubstituted. Better that it throws here than on a real mail to a
            // user.
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
