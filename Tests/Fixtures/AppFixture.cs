// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using Microsoft.Playwright;

namespace ConferenceApp.Tests.Fixtures;

/// <summary>
/// One start of the application for the whole suite: a separate database, local
/// stand-ins for the three external services (Go28, Stripe and SMTP), and a
/// browser.
/// <para>
/// Nothing the tests do leaves the machine, and nothing touches
/// <c>conferenceapp.db</c>.
/// </para>
/// </summary>
public sealed class AppFixture : IAsyncLifetime
{
    public AppHost     App    { get; } = new();
    public Go28Stub    Go28   { get; } = new();
    public StripeStub  Stripe { get; } = new();
    public SmtpSink    Smtp   { get; } = new();
    public TestDb      Db     { get; private set; } = null!;

    public TestCredentials Credentials => TestCredentials.Current;

    public string BaseUrl => App.BaseUrl;

    /// <summary>The root for uploads under test, which is not App_Data/uploads.</summary>
    public static string PrivateUploadsRoot { get; } =
        Path.Combine(TestPaths.RunScratch, "uploads");

    /// <summary>The test webhook key. The live one lives in user secrets and is never used here.</summary>
    public const string StripeWebhookSecret = "whsec_confapp_test_only_do_not_use_live";
    public const string StripeSecretKey     = "sk_test_confapp_local_stub";
    public const string StripePublishable   = "pk_test_confapp_local_stub";

    private IPlaywright? _playwright;
    public  IBrowser?    Browser { get; private set; }

    public async Task InitializeAsync()
    {
        // Fails early and clearly if the credentials are missing.
        _ = Credentials.AdminEmail;

        Directory.CreateDirectory(TestPaths.RunScratch);

        TestDb.Recreate(TestPaths.TestDbFile);
        Db = new TestDb(TestPaths.TestDbFile);

        Go28.Start();
        Stripe.Start();
        Smtp.Start();

        // The Stripe SDK is pointed at the stub. ApiBase is not a setting on
        // StripeConfiguration but on the client itself, so the client is replaced
        // wholesale. ApiKey is set to exactly the value the application will set
        // in StripeService, so that the setter there does not rebuild the client
        // and send the address back to api.stripe.com.
        global::Stripe.StripeConfiguration.ApiKey = StripeSecretKey;
        global::Stripe.StripeConfiguration.StripeClient =
            new global::Stripe.StripeClient(apiKey: StripeSecretKey, apiBase: Stripe.BaseUrl);

        App.Reserve();

        await App.StartAsync(new Dictionary<string, string>
        {
            // ── A database of its own ───────────────────────────────────
            ["ConnectionStrings:DefaultConnection"] = $"Data Source={TestPaths.TestDbFile}",
            ["BackupSettings:DbFileName"]           = TestPaths.TestDbFile,

            // ── Files and keys outside the repository ───────────────────
            ["Uploads:PrivateRoot"]      = PrivateUploadsRoot,
            ["DataProtection:KeysPath"]  = Path.Combine(TestPaths.RunScratch, "dp-keys"),
            ["Logging:File:Enabled"]     = "false",

            // ── The crypto gateway points at the local stub ─────────────
            ["Go28:BaseUrl"]  = Go28.BaseUrl + "/api/v1",
            ["Go28:ApiToken"] = "test-token-not-a-real-one",

            // ── Stripe points at the local stub, with test keys ─────────
            ["Stripe:SecretKey"]      = StripeSecretKey,
            ["Stripe:PublishableKey"] = StripePublishable,
            ["Stripe:WebhookSecret"]  = StripeWebhookSecret,

            // ── The mail goes to the local sink ─────────────────────────
            ["EmailSettings:Host"]      = "127.0.0.1",
            ["EmailSettings:Port"]      = Smtp.Port.ToString(),
            ["EmailSettings:EnableSsl"] = "false",
            ["EmailSettings:UserName"]  = "tests@localhost",
            ["EmailSettings:Password"]  = "not-used-by-the-sink",
            ["EmailSettings:From"]      = "conference.education@unwe.bg",

            // ── The links in the mail point at the application itself ───
            ["AppSettings:BaseUrl"]      = App.BaseUrl,
            ["AppSettings:ForceBaseUrl"] = "1",

            // ── The administrator is seeded by the application itself ───
            ["AdminSettings:SystemAdminPassword"] = Credentials.AdminPassword
        });

        PlaywrightBrowsers.EnsureInstalled();

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    /// <summary>A new HTTP session: its own cookies, and therefore its own signed-in user.</summary>
    public HttpSession NewSession() => new(App.BaseUrl, Db);

    /// <summary>
    /// A client with no cookies and no sign-in, for the webhooks and for the
    /// assertions about what an outside attacker can see. It gets an address of
    /// its own so that it does not eat the others' rate-limit quota (see
    /// <see cref="HttpSession.ClientIp"/>).
    /// </summary>
    public HttpClient NewClient(bool followRedirects = true)
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = followRedirects,
            UseCookies = false
        })
        {
            BaseAddress = new Uri(App.BaseUrl),
            Timeout = TimeSpan.FromSeconds(60)
        };

        client.DefaultRequestHeaders.Add("X-Forwarded-For", HttpSession.NextClientIp());
        return client;
    }

    /// <summary>A new browser context: its own session and its own language.</summary>
    public async Task<IBrowserContext> NewBrowserContextAsync(string locale = "bg-BG")
    {
        if (Browser == null) throw new InvalidOperationException("Браузърът не е вдигнат.");

        return await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL     = App.BaseUrl,
            Locale      = locale,
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            IgnoreHTTPSErrors = true,
            // A rate-limit quota of its own: one page makes dozens of requests.
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["X-Forwarded-For"] = HttpSession.NextClientIp()
            }
        });
    }

    /// <summary>
    /// A browser in which a given participant is already signed in. The sign-in
    /// goes the real way — a code taken from the database — in an HTTP session,
    /// and the cookies are carried across: signing in through the six code boxes
    /// is a test in part 3, not setup for every payment test.
    /// </summary>
    public async Task<IBrowserContext> NewLoggedInContextAsync(string email, string locale = "bg-BG")
    {
        using var session = NewSession();
        await session.LoginParticipantAsync(email);

        var context = await NewBrowserContextAsync(locale);
        await context.AddCookiesAsync(session.PlaywrightCookies());
        return context;
    }

    public async Task DisposeAsync()
    {
        if (Browser != null) await Browser.CloseAsync();
        _playwright?.Dispose();

        await Go28.DisposeAsync();
        await Stripe.DisposeAsync();
        await Smtp.DisposeAsync();

        Db.Dispose();
    }
}

[CollectionDefinition(Name)]
public sealed class AppCollection : ICollectionFixture<AppFixture>
{
    public const string Name = "ConferenceApp";
}
