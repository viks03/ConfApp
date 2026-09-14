// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text.Json;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Services;

/// <summary>
/// Part 10: "the mail queue survives a restart", through a real process and a
/// real <c>SIGTERM</c>.
/// <para>
/// <see cref="EmailQueueTests"/> checks the same thing through the service's
/// lifecycle. What is checked here is that the host gets that far at all: that
/// the shutdown signal leads to a drain rather than to a process cut off with mail
/// lost inside it.
/// </para>
/// </summary>
public class QueueShutdownTests : IAsyncLifetime
{
    private SmtpSink    _sink = null!;
    private AppProcess? _app;
    private TestDb?     _db;

    /// <summary>Four people, so as not to trip the limit of three codes per 30 minutes for one address.</summary>
    private static readonly string[] People =
    {
        "shutdown-1@example.test",
        "shutdown-2@example.test",
        "shutdown-3@example.test",
        "shutdown-4@example.test"
    };

    private const string Restarter = "shutdown-restart@example.test";

    public Task InitializeAsync()
    {
        _sink = new SmtpSink();
        _sink.Start();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_app != null) await _app.DisposeAsync();
        _db?.Dispose();
        await _sink.DisposeAsync();
    }

    /// <summary>
    /// [E-01] Queued tasks used to disappear on shutdown: the channel lives only in
    /// the process's memory and nothing was written down. For a one-time code that
    /// is expensive — the code is already in the database and already counts
    /// against the limit, while the user waits for a message that will never
    /// come.
    /// </summary>
    [Fact]
    public async Task Спирането_на_процеса_не_губи_чакащите_писма()
    {
        // Slow mail: the requests come back at once while the queue fills up.
        foreach (var person in People)
            _sink.DelayFor(person, TimeSpan.FromSeconds(2));

        _app = await AppProcess.StartAsync("queue-drain", MailSettings(),
        prepare: async app =>
        {
            // The participants have to exist before the application starts: the
            // first cleanup cycle runs immediately after startup, and the database
            // is migrated by the application itself.
            TestDb.Recreate(app.DbFile);
            var db = new TestDb(app.DbFile);
            await db.MigrateAsync();

            foreach (var person in People)
                await db.CreateParticipantAsync(person);

            db.Dispose();
        });

        _db = new TestDb(_app.DbFile);

        // Four requests for a code mean four tasks on the queue. The requests come
        // back at once even though the mail server takes two seconds to answer,
        // which is the very reason the queue exists.
        foreach (var person in People)
        {
            using var session = new HttpSession(_app.BaseUrl, _db);

            var token = await session.AntiforgeryTokenAsync("/Login");
            using var response = await session.PostFormAsync("/Login", new Dictionary<string, string>
            {
                ["Email"] = person,
                ["__RequestVerificationToken"] = token
            });

            response.EnsureSuccessStatusCode();
        }

        // The codes are in the database by now, so the application has promised a
        // message to each of the four.
        foreach (var person in People)
            Assert.NotNull(await _db.WaitForOtpAsync(person, "Login", TimeSpan.FromSeconds(20)));

        // Shut down while the queue is still working: the first message has gone
        // out, the last one certainly has not.
        await _sink.WaitForAttemptsAsync(People[0], 1, TimeSpan.FromSeconds(20));
        Assert.True(_sink.All.Count < People.Length,
            "Пощата прие всичко още преди спирането — тестът не проверява източване.");

        _app.SendSigTerm();

        Assert.True(await _app.WaitForExitAsync(TimeSpan.FromSeconds(60)),
            "Процесът не спря за 60 секунди след SIGTERM:\n" + _app.Output);

        // Everyone who was promised a message got it, shutdown notwithstanding.
        var delivered = _sink.All.Select(m => m.To).ToList();

        foreach (var person in People)
            Assert.Contains(delivered, to => to.Contains(person, StringComparison.OrdinalIgnoreCase));

        var log = _app.ReadLog();

        Assert.Contains("Източвам", log);
        Assert.Contains("Източване преди спиране", log);
        Assert.DoesNotContain("Спирането отряза опашката", log);
    }

    /// <summary>
    /// The restart: the old process stops and a new one starts against the same
    /// database and the same folders. The queue lives in memory, so the new process
    /// inherits nothing — and does not pretend it has.
    /// </summary>
    [Fact]
    public async Task След_рестарта_опашката_тръгва_на_чисто()
    {
        var settings = MailSettings();

        _app = await AppProcess.StartAsync("queue-restart", settings, prepare: async app =>
        {
            TestDb.Recreate(app.DbFile);
            var db = new TestDb(app.DbFile);
            await db.MigrateAsync();
            await db.CreateParticipantAsync(Restarter);
            db.Dispose();
        });

        _db = new TestDb(_app.DbFile);

        // One real message before the shutdown, so that the counters are not zero
        // simply because nothing ever went through the queue.
        using (var session = new HttpSession(_app.BaseUrl, _db))
        {
            var token = await session.AntiforgeryTokenAsync("/Login");
            using var response = await session.PostFormAsync("/Login", new Dictionary<string, string>
            {
                ["Email"] = Restarter,
                ["__RequestVerificationToken"] = token
            });
            response.EnsureSuccessStatusCode();
        }

        Assert.NotNull(await _sink.WaitForAsync(Restarter, TimeSpan.FromSeconds(30)));

        _app.SendSigTerm();
        Assert.True(await _app.WaitForExitAsync(TimeSpan.FromSeconds(60)),
            "Процесът не спря за 60 секунди след SIGTERM:\n" + _app.Output);

        var firstLog = _app.ReadLog();
        Assert.Contains("Background task queue started", firstLog);
        Assert.Contains("Background task queue stopped", firstLog);

        await _app.DisposeAsync();
        _app = null;

        // The second start uses the same name, and therefore the same folder and
        // the same database.
        _app = await AppProcess.StartAsync("queue-restart", settings);

        Assert.Equal(2, Occurrences(_app.ReadLog(), "Background task queue started"));

        var db2 = new TestDb(_app.DbFile);
        try
        {
            using var admin = new HttpSession(_app.BaseUrl, db2);
            await admin.LoginAdminAsync(
                TestCredentials.Current.AdminEmail, TestCredentials.Current.AdminPassword);

            using var health = await admin.Client.GetAsync("/Admin?handler=HealthCheck&service=emailQueue");
            health.EnsureSuccessStatusCode();

            var report = JsonDocument.Parse(await health.Content.ReadAsStringAsync()).RootElement;

            Assert.Equal("ok", report.GetProperty("status").GetString());

            var details = report.GetProperty("details").EnumerateArray()
                .ToDictionary(d => d.GetProperty("label").GetString()!,
                              d => d.GetProperty("value").GetString()!);

            Assert.Equal("работи", details["Консуматор"]);
            Assert.Equal("0", details["Чакащи задачи"]);
            // The counters are per process, so the new one inherits neither the
            // successes nor the failures of the old.
            Assert.StartsWith("0 / 0", details["Изпратени / провалени"]);
        }
        finally
        {
            db2.Dispose();
        }
    }

    private static int Occurrences(string text, string needle)
    {
        int count = 0, at = 0;

        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }

    private Dictionary<string, string> MailSettings() => new()
    {
        ["EmailSettings:Host"]      = "127.0.0.1",
        ["EmailSettings:Port"]      = _sink.Port.ToString(),
        ["EmailSettings:EnableSsl"] = "false",
        ["EmailSettings:UserName"]  = "tests@localhost",
        ["EmailSettings:Password"]  = "not-used-by-the-sink",
        ["EmailSettings:From"]      = "conference.education@unwe.bg"
    };
}
