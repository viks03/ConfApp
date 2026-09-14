// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Globalization;
using ConferenceApp.Data;
using ConferenceApp.Models;
using ConferenceApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// The server version header helps nobody except someone looking for a
// version-specific vulnerability, so Kestrel does not send it.
//
// MaxRequestBodySize is set explicitly. The framework default happens to be
// the same 30 MB, but written down here it is a decision rather than a
// coincidence. The largest upload the application accepts is the paper —
// 10 MB in both /Register and /Profile ([T-16]) — plus multipart overhead
// and the remaining form fields. The headroom is deliberate: this limit cuts
// the request off before the application ever sees it, so a ceiling set too
// tight would return an empty response instead of the "file is too large"
// message.
const long MaxRequestBodyBytes = 30L * 1024 * 1024;

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader        = false;
    options.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
});

// The second limit, this one for multipart forms. Its default is 128 MB —
// four times the Kestrel ceiling. The two must stay in step, so that raising
// one does not silently raise the other as well.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MaxRequestBodyBytes;
});

// ---------------- 0. LOGGING ----------------
// Without a provider registered here only the default console logger runs:
// under systemd everything goes to journald, under `dotnet run` in tmux it is
// lost on restart, and the admin panel reads no log at all. Any "something
// failed in a background service" ([S-04], [E-01], [E-02]) then has nowhere
// it can be found the next day.
//
// The console stays — the file is a second sink, not a replacement. Settings
// live under Logging:File (see README, "Configuration & secrets").
builder.Services.Configure<ConferenceApp.Services.Logging.FileLoggerOptions>(
    builder.Configuration.GetSection("Logging:File"));

{
    var fileLogOptions = new ConferenceApp.Services.Logging.FileLoggerOptions();
    builder.Configuration.GetSection("Logging:File").Bind(fileLogOptions);

    if (fileLogOptions.Enabled)
    {
        try
        {
            builder.Logging.AddProvider(new ConferenceApp.Services.Logging.FileLoggerProvider(
                Microsoft.Extensions.Options.Options.Create(fileLogOptions),
                builder.Environment.ContentRootPath));
        }
        catch (Exception ex)
        {
            // Missing permissions on the log folder must not stop the site,
            // but must not pass unnoticed either, so the line goes to the console.
            Console.Error.WriteLine(
                "ВНИМАНИЕ: файловият лог не можа да бъде включен ({0}: {1}). " +
                "Остава само конзолата. Задай друг път в Logging:File:Path.",
                ex.GetType().Name, ex.Message);
        }
    }
}

// [E-01]: the mail queue is drained on shutdown (QueuedHostedService.StopAsync).
// The default 30 seconds are shared by every hosted service; 45 leave room for
// the drain (15 s) while staying inside the systemd TimeoutStopSec.
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(45);
});

// ---------------- 1. DATABASE ----------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

// ---------------- 2. IDENTITY ----------------
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // OTP verification is handled by application code, so Identity's own
    // confirmation gates stay off and never block an unverified sign-in twice.
    options.SignIn.RequireConfirmedAccount = false;
    options.SignIn.RequireConfirmedEmail   = false;
    options.User.RequireUniqueEmail        = true;

    options.Password.RequireDigit           = true;
    options.Password.RequiredLength         = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase       = true;
    options.Password.RequireLowercase       = false; 

    options.Lockout.DefaultLockoutTimeSpan  = TimeSpan.FromHours(12);
    options.Lockout.MaxFailedAccessAttempts = 3;
    options.Lockout.AllowedForNewUsers      = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// [T-05] How often the sign-in cookie is re-checked against the security stamp
// stored on the user row. The default is 30 minutes, which means an invalidated
// cookie (sign-out, password change) keeps working for up to half an hour. Zero
// re-checks on every request, so signing out means what the button says.
//
// The cost is one user lookup per request and a refreshed cookie in the
// response. At this size of site that is unnoticeable; if it ever matters,
// TimeSpan.FromMinutes(1) is the only change needed here.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.Zero;
});

// ---------------- 3. LOCALIZATION & RAZOR PAGES ----------------
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.AddRazorPages(options =>
    {
        // Registered once here instead of a line in each of the 20+ public
        // pages: filling it in by hand gets forgotten when a new page is added
        // and the background then silently stops working there. The filter
        // disables itself under /Admin.
        options.Conventions.ConfigureFilter(
            new Microsoft.AspNetCore.Mvc.ServiceFilterAttribute(
                typeof(ConferenceApp.Services.Styles.GlobalStylesFilter)));

        // [A-05] /Register accepts a file in phase 2 — before a user exists
        // and before the per-IP counter in Register.cshtml.cs, which only
        // counts completed registrations. The "register" policy is the only
        // thing that limits that phase. It is attached here because an
        // attribute on the PageModel is not read by the rate limiter.
        options.Conventions.AddPageApplicationModelConvention("/Register",
            model => model.EndpointMetadata.Add(new EnableRateLimitingAttribute("register")));
    })
    .AddViewLocalization();

// AddDataAnnotationsLocalization() used to sit here and translated nothing:
// the default provider looks for a resource file named after the containing
// type (Pages.RegisterModel+InputModel), while all 52 files in Resources/ are
// named after the view (Pages.Register.bg.resx). The three pages that carry
// validation attributes translate explicitly through
// ModelState.LocalizeErrors(...) — see Helpers/ModelStateLocalizer.cs.

builder.Services.AddControllersWithViews();

// ---------------- 4. SERVICES ----------------
builder.Services.AddHttpContextAccessor();

// ── Upload paths (Services/Files/) ────────────────────────────────────
// The single place that turns a relative path into a physical one ([F-05])
// and knows which two folders live outside wwwroot ([F-02]). Singleton: it
// holds configuration only.
builder.Services.AddSingleton<ConferenceApp.Services.Files.IUploadPaths,
                              ConferenceApp.Services.Files.UploadPathService>();

// Singleton, not Scoped: EmailSender is also called from background tasks
// (see IBackgroundTaskQueue) that run after the request scope has been
// disposed. The class holds no per-request state, only configuration and a
// logger.
builder.Services.AddSingleton<EmailSender>();

// ── Mail subsystem (Services/Email/) ──────────────────────────────────
builder.Services.AddSingleton<ConferenceApp.Services.Email.IEmailTemplateRenderer,
                              ConferenceApp.Services.Email.EmailTemplateRenderer>();
builder.Services.AddSingleton<ConferenceApp.Services.Email.IEmailNotificationSettings,
                              ConferenceApp.Services.Email.EmailNotificationSettings>();
// ── Payment Control (Services/PaymentGateSettings.cs) ─────────────────
builder.Services.AddSingleton<ConferenceApp.Services.IPaymentGateSettings,
                              ConferenceApp.Services.PaymentGateSettings>();
// Audit trail for administrative actions. Scoped, because it uses
// ApplicationDbContext — the filter lives as long as the request.
builder.Services.AddScoped<ConferenceApp.Services.Audit.AdminAuditFilter>();

// Fills the ViewData the visual layer of every public page reads. Scoped,
// because it queries ApplicationDbContext.
builder.Services.AddScoped<ConferenceApp.Services.Styles.GlobalStylesFilter>();

// Singleton: the changelog file does not change between restarts and the
// reader keeps a cache.
builder.Services.AddSingleton<ConferenceApp.Services.Changelog.ChangelogReader>();

// Singleton with a cache: the theme changes rarely and is read on every
// request.
builder.Services.AddSingleton<ConferenceApp.Services.Theming.ThemeProvider>();

// The factory is for HealthCheckService, which creates a named client of its
// own. Go28 gets its own typed client further down.
builder.Services.AddHttpClient();

// ── Health check (Services/Health/) ───────────────────────────────────
// Singleton: the service holds no per-request state and reads configuration,
// the disk and the queue, which are singletons too. For the database it opens
// a scope of its own (see IServiceScopeFactory inside).
builder.Services.AddSingleton<ConferenceApp.Services.Health.IHealthCheckService,
                              ConferenceApp.Services.Health.HealthCheckService>();

builder.Services.AddSingleton<ConferenceApp.Services.Email.IMailComposer,
                              ConferenceApp.Services.Email.MailComposer>();
builder.Services.AddScoped<AuditService>();

// Queue for slow work (SMTP) so that it does not hold up the HTTP response —
// see the comment in Services/IBackgroundTaskQueue.cs.
builder.Services.AddSingleton<ConferenceApp.Services.IBackgroundTaskQueue, ConferenceApp.Services.BackgroundTaskQueue>();
builder.Services.AddHostedService<ConferenceApp.Services.QueuedHostedService>();

// [S-04]: what ConsumerRunning/LastActivityAt are for the queue, this is for
// the cleanup service — the difference between "no error" and "running". The
// ninth health check reads it. Singleton, because the background service
// writes it and the health service, itself a singleton, reads it.
builder.Services.AddSingleton<ConferenceApp.Services.ICleanupStatus,
                              ConferenceApp.Services.CleanupStatus>();

// The same thing for the backups, and for the same reason: the Backups card in
// Health used to judge only by what was in the folder, so a running service that
// had not reached its first window (03:00 / 15:00 UTC) was reported as broken.
// The status also carries the next window, so the card and the scheduler cannot
// disagree about it.
builder.Services.AddSingleton<ConferenceApp.Services.IBackupStatus,
                              ConferenceApp.Services.BackupStatus>();

// The copying itself, apart from the schedule: the button in the Health tab and
// the 03:00 / 15:00 window call this same runner, so a manual copy is made the
// same consistent way, rotated the same way and audited the same way. Singleton
// — it holds the gate that stops two copies running at once.
builder.Services.AddSingleton<ConferenceApp.Services.IDatabaseBackupRunner,
                              ConferenceApp.Services.DatabaseBackupRunner>();

// Go28 crypto gateway — a typed HttpClient.
builder.Services.AddHttpClient<Go28Service>(client =>
{
    // Gives up after 5 seconds so that page load is not held hostage when
    // outbound traffic to the gateway is blocked.
    client.Timeout = TimeSpan.FromSeconds(5);
});

// Stripe — card payments, Apple Pay, Google Pay.
builder.Services.AddScoped<ConferenceApp.Services.StripeService>();

// Background services
builder.Services.AddHostedService<CleanupService>();
builder.Services.AddHostedService<DatabaseBackupService>();

// ── Remote accessibility switch (Services/RemoteControl/) ─────────────
// A switch for the whole site, held on another machine: the conference can be
// taken down and put back up without access to the server this runs on.
//
// An empty RemoteControl:Url — the default — registers nothing at all. No
// background service, no middleware below, not one outbound request. The
// feature costs nothing until somebody fills in an address.
//
// An address that is not an address counts as empty too, with a warning further
// down. A typo in a setting must not become a hosted service that throws on its
// first line, because an unhandled exception there stops the host — a mistyped
// URL would take the site down, which is the exact opposite of the point.
//
// The key never comes from the file. RemoteControl__Key in the environment maps
// onto RemoteControl:Key and takes precedence over appsettings.json.
var remoteControl = new ConferenceApp.Services.RemoteControl.RemoteControlOptions();
builder.Configuration
    .GetSection(ConferenceApp.Services.RemoteControl.RemoteControlOptions.SectionName)
    .Bind(remoteControl);

if (remoteControl.Enabled)
{
    builder.Services.Configure<ConferenceApp.Services.RemoteControl.RemoteControlOptions>(
        builder.Configuration.GetSection(
            ConferenceApp.Services.RemoteControl.RemoteControlOptions.SectionName));

    // Singleton: one object, written by the background service and read by the
    // middleware on every request. The clock is injected so that the rules about
    // silence can be tested without waiting an hour and a half.
    builder.Services.AddSingleton(
        _ => new ConferenceApp.Services.RemoteControl.RemoteControlState(TimeProvider.System));

    builder.Services.AddHttpClient(
        ConferenceApp.Services.RemoteControl.RemoteControlPoller.HttpClientName,
        client =>
        {
            // Five seconds and no more. The background service waits, but it
            // waits on a leash — a control server that hangs must not turn into
            // a thread that never comes back.
            client.Timeout = remoteControl.Timeout;

            // A status answer is a few hundred bytes. Anything trying to be
            // larger is cut off rather than buffered.
            client.MaxResponseContentBufferSize =
                ConferenceApp.Services.RemoteControl.RemoteControlResponse.MaxBodyBytes;
        });

    builder.Services.AddHostedService<ConferenceApp.Services.RemoteControl.RemoteControlPoller>();
}

// ---------------- 4.4. RATE LIMITING ----------------
// Nothing used to limit request rates: the anonymous crypto webhook ([P-01])
// and the file write in phase 2 of registration ([A-05]) were accepted an
// unlimited number of times.
//
// Partitions are keyed by IP address. Since [C-01] RemoteIpAddress is the
// client's address rather than the proxy's — X-Forwarded-For is honoured only
// from KnownProxies.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // The global ceiling is deliberately generous. Opening the admin panel
    // once costs 31 requests ([AD-08]), and dozens of people can sit behind a
    // single NAT address — this stops flooding, it does not meter normal use.
    // Static files never count: UseStaticFiles runs before UseRateLimiter.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window      = TimeSpan.FromMinutes(1),
                QueueLimit  = 0
            }));

    // [P-01] Webhooks are anonymous. Go28 and Stripe send a handful of
    // requests per payment; anything beyond that is either a broken sender or
    // an attempt to guess a ReferenceNumber.
    options.AddPolicy("webhook", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window      = TimeSpan.FromMinutes(1),
                QueueLimit  = 0
            }));

    // [A-05] Only POSTs are counted — they are the ones that write a file to
    // disk. Loading the page writes nothing and stays under the global ceiling
    // alone; otherwise, behind one shared address (a university network),
    // several people merely looking at the form would eat the quota of the one
    // submitting it.
    //
    // A full registration is three POSTs, plus one for every failed validation.
    // Thirty per five minutes is enough even on a shared address and leaves
    // room for the existing ceiling of 20 completed registrations per hour.
    options.AddPolicy("register", httpContext =>
        !HttpMethods.IsPost(httpContext.Request.Method)
            ? RateLimitPartition.GetNoLimiter<string>("register-read")
            : RateLimitPartition.GetFixedWindowLimiter<string>(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window      = TimeSpan.FromMinutes(5),
                    QueueLimit  = 0
                }));

    // A rejection has to show up in the log — otherwise "the site is down"
    // leaves no trace.
    options.OnRejected = async (context, cancellationToken) =>
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("RateLimiter");

        logger.LogWarning("Rate limit hit. Ip={Ip} Path={Path} Method={Method}",
            context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            context.HttpContext.Request.Path,
            context.HttpContext.Request.Method);

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);

        await Task.CompletedTask;
    };
});

// ---------------- 4.5. DATA PROTECTION ----------------
// Persists the keys on disk — without this every restart invalidates all
// cookies. The path comes from configuration (DataProtection:KeysPath) so that
// a different server layout needs no recompilation. A missing value falls back
// to the paths below.
var configuredKeysPath = builder.Configuration["DataProtection:KeysPath"];
var keysPath = !string.IsNullOrWhiteSpace(configuredKeysPath)
    ? configuredKeysPath
    : builder.Environment.IsProduction()
        ? "/var/www/conferenceapp/DataProtection-Keys"
        : Path.Combine(builder.Environment.ContentRootPath, "DataProtection-Keys");

// The directory is created and probed for write access here rather than on the
// first request. Otherwise, with permissions missing, the application starts
// "successfully", every page returns 500, and the only trace is a single line
// in the console log.
try
{
    Directory.CreateDirectory(keysPath);
    var probe = Path.Combine(keysPath, ".write-probe");
    File.WriteAllText(probe, string.Empty);
    File.Delete(probe);
}
catch (Exception ex)
{
    throw new InvalidOperationException(
        $"Data Protection: не може да се пише в '{keysPath}'. Провери правата на " +
        "потребителя, под който върви услугата, или задай друг път в " +
        "DataProtection:KeysPath.", ex);
}

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("ConferenceApp");

// ---------------- 5. COOKIES ----------------
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath       = "/Login";
    options.LogoutPath      = "/Logout";
    options.AccessDeniedPath = "/AccessDenied";

    options.ExpireTimeSpan    = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;

    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;

    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

var app = builder.Build();

// ── Secrets check at startup ──────────────────────────────────────────
// These six keys are not stored in appsettings.json (empty strings there).
// Locally they come from user-secrets, on the server from environment
// variables (Stripe__SecretKey and so on); see README, "Configuration &
// secrets". Without this check a missing key surfaces much later and in a
// different way each time: the admin is not created, the payment page throws,
// mail throws on send.
{
    string[] requiredSecrets =
    [
        "Stripe:SecretKey",
        "Stripe:PublishableKey",
        "Stripe:WebhookSecret",
        "Go28:ApiToken",
        "EmailSettings:Password",
        "AdminSettings:SystemAdminPassword"
    ];

    var missingSecrets = requiredSecrets
        .Where(key => string.IsNullOrWhiteSpace(app.Configuration[key]))
        .ToArray();

    if (missingSecrets.Length > 0)
    {
        app.Logger.LogWarning(
            "Липсващи настройки ({Count}): {Keys}. Задай ги с `dotnet user-secrets set` " +
            "локално или като променливи на средата на сървъра (двойна долна черта " +
            "вместо двоеточие). Виж README, раздел \"Configuration & secrets\".",
            missingSecrets.Length, string.Join(", ", missingSecrets));
    }
}

// ── Private root for uploaded personal files ──────────────────────────
// Papers and verification documents no longer live under wwwroot ([F-02]), so
// the static file middleware cannot serve them. The directory is created and
// probed for write access at startup for the same reason as the Data
// Protection keys: otherwise the first upload fails in front of a user.
//
// Files left in the old location by an earlier version are moved once. The
// relative paths stored in the database do not change, so after the move they
// still point at the same file, just under a different root.
{
    var uploadPaths = app.Services.GetRequiredService<ConferenceApp.Services.Files.IUploadPaths>();

    try
    {
        Directory.CreateDirectory(uploadPaths.PrivateRoot);
        var probe = Path.Combine(uploadPaths.PrivateRoot, ".write-probe");
        File.WriteAllText(probe, string.Empty);
        File.Delete(probe);
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException(
            $"Качени файлове: не може да се пише в '{uploadPaths.PrivateRoot}'. Провери " +
            "правата на потребителя, под който върви услугата, или задай друг път в " +
            "Uploads:PrivateRoot.", ex);
    }

    foreach (var relativeFolder in new[] { "uploads/papers26", "uploads/submitted-documents" })
    {
        var legacyFolder = Path.Combine(app.Environment.WebRootPath,
            relativeFolder.Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(legacyFolder)) continue;

        var target = uploadPaths.EnsureDirectory(relativeFolder);
        var moved  = 0;

        foreach (var source in Directory.EnumerateFiles(legacyFolder, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(source);
            if (name == ".gitkeep") continue;

            var destination = Path.Combine(target,
                Path.GetRelativePath(legacyFolder, source));

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                // A file already at the destination means an earlier startup
                // moved it; the one in wwwroot is then a leftover, not the
                // current copy, so it is deleted rather than overwriting.
                if (File.Exists(destination)) File.Delete(source);
                else File.Move(source, destination);

                moved++;
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex,
                    "Не можах да преместя {Source} в частния корен. Файлът остава в wwwroot " +
                    "и се раздава анонимно — премести го на ръка.", source);
            }
        }

        if (moved > 0)
            app.Logger.LogInformation(
                "Преместени {Count} файла от wwwroot/{Folder} в {Target}.",
                moved, relativeFolder, target);
    }
}

// ---------------- 6. LOCALIZATION OPTIONS ----------------
var supportedCultures = new[] { new CultureInfo("bg"), new CultureInfo("en") };
var localizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture  = new RequestCulture("bg"),
    SupportedCultures      = supportedCultures,
    SupportedUICultures    = supportedCultures
};

localizationOptions.RequestCultureProviders = new List<IRequestCultureProvider>
{
    new CookieRequestCultureProvider(),
    new QueryStringRequestCultureProvider(),
    new AcceptLanguageHeaderRequestCultureProvider()
};

// ---------------- 7. HTTP PIPELINE ----------------
// Forwarded headers behind a reverse proxy (Nginx / Docker / Cloudflare).
//
// KnownProxies and KnownNetworks are an allowlist of the neighbours trusted to
// tell the truth about the client's address. An empty list does NOT mean
// "accept from every proxy" — it means "do not check the sender", so any
// client can pick its own IP through a single header. They are therefore
// filled from configuration: ForwardedHeaders:KnownProxies (addresses) and
// ForwardedHeaders:KnownNetworks (CIDR). The default is loopback only, which
// covers a proxy or tunnel on the same machine.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};

forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();

var knownProxyAddresses = app.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>();
if (knownProxyAddresses is null || knownProxyAddresses.Length == 0)
    knownProxyAddresses = ["127.0.0.1", "::1"];

var trustedProxies = new List<System.Net.IPAddress>();
foreach (var candidate in knownProxyAddresses)
{
    if (System.Net.IPAddress.TryParse(candidate, out var proxyIp))
    {
        trustedProxies.Add(proxyIp);
        forwardedHeadersOptions.KnownProxies.Add(proxyIp);
    }
    else
    {
        app.Logger.LogWarning("ForwardedHeaders:KnownProxies съдържа невалиден адрес: {Value}", candidate);
    }
}

var trustedNetworks = new List<(System.Net.IPAddress Prefix, int Length)>();
foreach (var cidr in app.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
{
    var parts = cidr.Split('/');
    if (parts.Length == 2 &&
        System.Net.IPAddress.TryParse(parts[0], out var networkIp) &&
        int.TryParse(parts[1], out var prefixLength))
    {
        trustedNetworks.Add((networkIp, prefixLength));
        forwardedHeadersOptions.KnownNetworks.Add(new IPNetwork(networkIp, prefixLength));
    }
    else
    {
        app.Logger.LogWarning("ForwardedHeaders:KnownNetworks съдържа невалиден CIDR: {Value}", cidr);
    }
}

// A request from an untrusted neighbour has no business carrying address and
// scheme headers. They are stripped BEFORE UseForwardedHeaders, so that
// neither it, nor the Cloudflare layer below, nor BugReportController — which
// reads X-Forwarded-For directly — ever sees them. Without this, the per-IP
// limit on sign-in is bypassed with a single header, and the IpAddress column
// in AuditLogs is written by the very person being audited.
app.Use(async (context, next) =>
{
    var peer = context.Connection.RemoteIpAddress;
    var trusted = peer is not null &&
        (trustedProxies.Any(p => p.Equals(peer)) ||
         trustedNetworks.Any(n => IsInNetwork(peer, n.Prefix, n.Length)));

    if (!trusted)
    {
        context.Request.Headers.Remove("CF-Connecting-IP");
        context.Request.Headers.Remove(ForwardedHeadersDefaults.XForwardedForHeaderName);
        context.Request.Headers.Remove(ForwardedHeadersDefaults.XForwardedProtoHeaderName);
    }

    await next();
});

app.UseForwardedHeaders(forwardedHeadersOptions);

// ── Cloudflare real IP ────────────────────────────────────────────────
// When the request comes through Cloudflare, CF-Connecting-IP carries the
// client's real address. The header reaches this point only if the sender is
// in the allowlist above.
app.Use(async (context, next) =>
{
    if (context.Request.Headers.TryGetValue("CF-Connecting-IP", out var cloudflareIp))
    {
        if (System.Net.IPAddress.TryParse(cloudflareIp.ToString(), out var ipAddress))
        {
            context.Connection.RemoteIpAddress = ipAddress;
        }
    }
    await next();
});
// --------------------------------------------------------------------

static bool IsInNetwork(System.Net.IPAddress address, System.Net.IPAddress prefix, int prefixLength)
{
    if (address.AddressFamily != prefix.AddressFamily) return false;

    var addressBytes = address.GetAddressBytes();
    var prefixBytes  = prefix.GetAddressBytes();
    if (prefixLength < 0 || prefixLength > addressBytes.Length * 8) return false;

    var fullBytes = prefixLength / 8;
    var remainingBits = prefixLength % 8;

    for (var i = 0; i < fullBytes; i++)
        if (addressBytes[i] != prefixBytes[i]) return false;

    if (remainingBits == 0) return true;

    var mask = (byte)(0xFF << (8 - remainingBits));
    return (addressBytes[fullBytes] & mask) == (prefixBytes[fullBytes] & mask);
}

// [C-17] Development has no branch of its own: UseMigrationsEndPoint was
// dropped because context.Database.Migrate() below runs on every start — an
// unapplied migration cannot exist while the app is running, so there was
// nothing for it to react to.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Error");
app.UseHttpsRedirection();

// ── Security headers ──────────────────────────────────────────────────
// Before UseStaticFiles, so they cover wwwroot/uploads/ as well, which is
// served anonymously. nosniff is the second line of defence for uploaded
// files — without it the browser decides the type from the content rather than
// the extension. frame-ancestors stops the panel being embedded in someone
// else's <iframe> (clickjacking on "Confirm payment" and "Delete user");
// X-Frame-Options covers browsers that do not read CSP yet.
// A full CSP with script-src is deliberately not set here — the frontend has
// inline onclick handlers in several places and one external CDN without
// integrity, so it would break pages.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"]   = "nosniff";
    headers["X-Frame-Options"]          = "DENY";
    headers["Referrer-Policy"]          = "strict-origin-when-cross-origin";
    headers["Content-Security-Policy"]  = "frame-ancestors 'none'";
    await next();
});

app.UseStaticFiles();

// ── Remote accessibility switch ───────────────────────────────────────
// Immediately after UseStaticFiles and before UseRouting: the files the
// maintenance page might need are already served, and while the site is off
// nothing past this line runs — no route, no rate limiter, no authentication,
// no page, no database.
//
// Added only when the switch is configured, so with an empty Url the pipeline
// is byte for byte the one that was here before.
if (remoteControl.Enabled)
{
    app.UseMiddleware<ConferenceApp.Services.RemoteControl.MaintenanceMiddleware>();
}
else if (remoteControl.HasUrl)
{
    // Configured, but with something no HttpClient can fetch. The site works;
    // the switch does not, and that has to be readable rather than silent.
    app.Logger.LogWarning(
        "RemoteControl:Url не е годен адрес и превключвателят не е пуснат: {Value}. " +
        "Очаква се http:// или https:// адрес на контролния сървър.",
        remoteControl.Url);
}

app.UseRequestLocalization(localizationOptions);

app.UseRouting();

// After UseRouting, because the policies are read from endpoint metadata.
// Before the Stripe body is buffered, so a rejection happens without reading
// the body.
app.UseRateLimiter();

// ── Raw body for the Stripe webhook ───────────────────────────────────
// The signature is verified over the exact bytes received, so the stream has
// to stay re-readable after model binding.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/stripe/webhook"))
        context.Request.EnableBuffering();
    await next();
});

app.UseAuthentication();
app.UseAuthorization();

// ---------------- 8. SEED DATA & MIGRATIONS ----------------
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    
    // Order matters: the schema has to exist before the seed writes into it.
    var context = services.GetRequiredService<ApplicationDbContext>();
    context.Database.Migrate(); 
    
    await DbInitializer.SeedUsersAsync(services, builder.Configuration);
}

// ---------------- 9. ENDPOINTS & REDIRECTS ----------------
app.MapControllers();
app.MapRazorPages();

app.Run();