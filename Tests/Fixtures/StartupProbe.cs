// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Tests.Fixtures;

/// <summary>
/// A second start of the application with settings of its own, for the
/// questions in part 1 that the main start cannot answer: what happens when the
/// path to the keys is wrong, what the application does when the neighbour is
/// not trusted, and what it writes to the log on a first run.
/// <para>
/// Every probe start gets its OWN database, its own webroot and its own key
/// folder, so it touches neither the main suite nor the repository.
/// </para>
/// </summary>
public sealed class StartupProbe
{
    public AppHost   Host    { get; } = new();
    public string    Scratch { get; }
    public string    DbFile  { get; }
    public string    WebRoot { get; }
    public string    LogDir  { get; }
    public Exception? StartupError { get; private set; }

    public string BaseUrl => Host.BaseUrl;

    private StartupProbe(string name)
    {
        Scratch = Path.Combine(TestPaths.RunScratch, "startup-" + name);
        DbFile  = Path.Combine(Scratch, "probe.db");
        WebRoot = Path.Combine(Scratch, "wwwroot");
        LogDir  = Path.Combine(Scratch, "logs");

        Directory.CreateDirectory(Scratch);
        Directory.CreateDirectory(WebRoot);
        Directory.CreateDirectory(LogDir);
    }

    /// <summary>
    /// Starts a probe instance. <paramref name="configure"/> may deliberately
    /// break whatever the test needs broken.
    /// </summary>
    /// <param name="expectFailure">
    /// When a failure is expected, the full minute and a half is not waited out.
    /// </param>
    /// <param name="prepareWebRoot">
    /// Called AFTER the folders are created and BEFORE the application starts:
    /// this is where the files the startup is meant to find are put.
    /// </param>
    public static async Task<StartupProbe> StartAsync(
        string name,
        Action<Dictionary<string, string>>? configure = null,
        bool expectFailure = false,
        Action<StartupProbe>? prepareWebRoot = null)
    {
        var probe = new StartupProbe(name);
        prepareWebRoot?.Invoke(probe);

        var settings = new Dictionary<string, string>
        {
            ["ConnectionStrings:DefaultConnection"] = $"Data Source={probe.DbFile}",
            ["BackupSettings:DbFileName"]           = probe.DbFile,

            // A webroot of its own: the migration at startup reads
            // wwwroot/uploads/... and MOVES what it finds. With the real webroot
            // the test would be touching live uploaded files.
            ["Uploads:PrivateRoot"]     = Path.Combine(probe.Scratch, "private"),
            ["DataProtection:KeysPath"] = Path.Combine(probe.Scratch, "dp-keys"),

            ["Logging:File:Enabled"]      = "true",
            ["Logging:File:Path"]         = probe.LogDir,
            ["Logging:File:MinimumLevel"] = "Information",

            // The external services are not called during startup, but the
            // configuration has to be there for the start to be realistic.
            ["Go28:BaseUrl"]          = "http://127.0.0.1:1/api/v1",
            ["Go28:ApiToken"]         = "probe-token",
            ["Stripe:SecretKey"]      = AppFixture.StripeSecretKey,
            ["Stripe:PublishableKey"] = AppFixture.StripePublishable,
            ["Stripe:WebhookSecret"]  = AppFixture.StripeWebhookSecret,

            ["EmailSettings:Host"]      = "127.0.0.1",
            ["EmailSettings:Port"]      = "1",
            ["EmailSettings:EnableSsl"] = "false",
            ["EmailSettings:UserName"]  = "probe@localhost",
            ["EmailSettings:Password"]  = "not-used",

            ["AdminSettings:SystemAdminPassword"] = TestCredentials.Current.AdminPassword
        };

        // The webroot belongs to the probe instance and is set before configure,
        // so that a test can override it when it needs a different one.
        settings["webroot"] = probe.WebRoot;

        configure?.Invoke(settings);
        probe.Host.Reserve();

        if (expectFailure)
            probe.Host.StartupTimeout = TimeSpan.FromSeconds(25);

        probe.StartupError = await probe.Host.TryStartAsync(settings);
        return probe;
    }

    /// <summary>The whole log of the probe instance, concatenated.</summary>
    public string ReadLog()
    {
        if (!Directory.Exists(LogDir)) return string.Empty;

        var text = new System.Text.StringBuilder();

        foreach (var file in Directory.EnumerateFiles(LogDir))
        {
            try
            {
                // The provider still holds the file open, so the read is shared.
                using var stream = new FileStream(file, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                text.AppendLine(reader.ReadToEnd());
            }
            catch (IOException) { }
        }

        return text.ToString();
    }

    /// <summary>Waits for a line in the log; writing goes through a buffer and is not instant.</summary>
    public async Task<string?> WaitForLogLineAsync(string contains, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var line = ReadLog()
                .Split('\n')
                .FirstOrDefault(l => l.Contains(contains, StringComparison.Ordinal));

            if (line != null) return line;
            await Task.Delay(200);
        }

        return null;
    }

    public HttpClient NewClient(bool followRedirects = true)
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = followRedirects,
            UseCookies = true,
            CookieContainer = new System.Net.CookieContainer()
        })
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        return client;
    }

    public TestDb Db() => new(DbFile);
}
