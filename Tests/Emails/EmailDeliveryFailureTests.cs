// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text.Json;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Emails;

/// <summary>
/// Part 8: "a failed message is retried and shows up in Health", and "with
/// SaveToDisk the file appears in App_Data/sent-emails".
/// <para>
/// Everything here needs a message that fails for good. That leaves a mark on
/// the whole process — the failure counter and the "last failure" row in the
/// Health tab live until the next restart — so these tests run against an
/// instance of their own; see <see cref="FailingMailApp"/>.
/// </para>
/// </summary>
public class EmailDeliveryFailureTests : IClassFixture<FailingMailApp>, IAsyncLifetime
{
    private readonly FailingMailApp _app;
    private readonly List<string> _users = new();

    public EmailDeliveryFailureTests(FailingMailApp app) => _app = app;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var email in _users)
        {
            _app.Sink.StopFailingFor(email);
            await _app.Db.DeleteParticipantAsync(email);
            await _app.Db.DeleteOtpCodesAsync(email);
        }
    }

    private async Task<ConferenceApp.Models.ApplicationUser> NewParticipantAsync()
    {
        var email = $"mail-fail-{Guid.NewGuid():N}@example.test";
        _users.Add(email);
        return await _app.Db.CreateParticipantAsync(email);
    }

    private static async Task RequestCodeAsync(HttpSession session, string email)
    {
        var token = await session.AntiforgeryTokenAsync("/Login");
        var response = await session.PostFormAsync("/Login", new Dictionary<string, string>
        {
            ["Email"] = email,
            ["__RequestVerificationToken"] = token
        });
        response.EnsureSuccessStatusCode();
    }

    private async Task<JsonElement> HealthAsync(string service)
    {
        using var admin = await _app.SignedInAdminAsync();
        using var response = await admin.Client.GetAsync($"/Admin?handler=HealthCheck&service={service}");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    // ════════════════════════════════════════════════════════════════════
    // Failing for good
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Three attempts: not two, and not endlessly. Retrying forever would block
    /// the single consumer and no later message would ever go out.
    /// </summary>
    [Fact]
    public async Task Писмо_което_не_тръгва_се_опитва_три_пъти_и_спира()
    {
        var user = await NewParticipantAsync();
        _app.Sink.AlwaysFailFor(user.Email!);

        using var session = _app.NewSession();
        await RequestCodeAsync(session, user.Email!);

        // The pauses are 3 and 15 seconds.
        var attempts = await _app.Sink.WaitForAttemptsAsync(user.Email!, 3, TimeSpan.FromSeconds(60));
        Assert.Equal(3, attempts);

        // And it does not go on trying afterwards.
        await Task.Delay(TimeSpan.FromSeconds(5));
        Assert.Equal(3, _app.Sink.AttemptsFor(user.Email!));

        // None of the three reached the mailbox.
        Assert.DoesNotContain(_app.Sink.All,
            m => m.To.Contains(user.Email!, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The code is already in the database and already counts against the ceiling
    /// of three per 30 minutes, and no message exists. That is the price of a
    /// silent failure, which is why the failure has to reach the Health tab and
    /// not only the log.
    /// </summary>
    [Fact]
    public async Task Окончателният_провал_се_вижда_в_Health()
    {
        var user = await NewParticipantAsync();
        _app.Sink.AlwaysFailFor(user.Email!);

        using var session = _app.NewSession();
        await RequestCodeAsync(session, user.Email!);

        var attempts = await _app.Sink.WaitForAttemptsAsync(user.Email!, 3, TimeSpan.FromSeconds(60));
        Assert.Equal(3, attempts);

        // The code is issued and spent against the ceiling without the user
        // receiving anything.
        Assert.NotEmpty(await _app.Db.OtpCodesAsync(user.Email!, "Login"));

        var queue = await WaitForHealthAsync("emailQueue", "fail", TimeSpan.FromSeconds(30));

        Assert.Equal("fail", queue.GetProperty("status").GetString());

        var message = queue.GetProperty("message").GetString()!;
        Assert.Contains("не тръгнаха", message, StringComparison.OrdinalIgnoreCase);

        // The "last failure" row has to give the reason too; otherwise the
        // administrator sees red and nothing more.
        var lastFailure = queue.GetProperty("details").EnumerateArray()
            .First(d => d.GetProperty("label").GetString()!
                .Contains("Последен провал", StringComparison.OrdinalIgnoreCase))
            .GetProperty("value").GetString()!;

        Assert.NotEqual("— (няма)", lastFailure);
        Assert.Contains("Exception", lastFailure, StringComparison.Ordinal);

        // And the hint points at the SMTP probe rather than leaving them to guess.
        var hint = queue.GetProperty("hint").GetString() ?? string.Empty;
        Assert.Contains("SMTP", hint, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A failed message must not bring the consumer down: the next one, to an
    /// address the sink does NOT reject, has to go out normally.
    /// </summary>
    [Fact]
    public async Task Провалено_писмо_не_спира_следващите()
    {
        var doomed = await NewParticipantAsync();
        var fine   = await NewParticipantAsync();

        _app.Sink.AlwaysFailFor(doomed.Email!);

        using var one = _app.NewSession();
        await RequestCodeAsync(one, doomed.Email!);

        using var two = _app.NewSession();
        await RequestCodeAsync(two, fine.Email!);

        var mail = await _app.Sink.WaitForAsync(fine.Email!, TimeSpan.FromSeconds(90));

        Assert.True(mail != null,
            $"След провалено писмо до {doomed.Email} опашката не изпрати нищо до {fine.Email}.");
    }

    // ════════════════════════════════════════════════════════════════════
    // The copy on disk — [T-30]
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// [T-30] TESTS_PROMPT.md describes <c>EmailSettings:SaveToDisk = true</c>,
    /// under which a message is also written to
    /// <c>App_Data/sent-emails/*.html</c>. No such setting exists in the
    /// application.
    /// <para>
    /// The test pins down today's position: the message goes out and no folder
    /// appears. If it fails, the feature has been built and the finding is
    /// closed.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Настройката_SaveToDisk_днес_не_записва_нищо()
    {
        var before = FilesInSentEmails();

        var user = await NewParticipantAsync();

        using var session = _app.NewSession();
        await RequestCodeAsync(session, user.Email!);

        // The message really does go out; otherwise the test would pass for the
        // wrong reason.
        var mail = await _app.Sink.WaitForAsync(user.Email!, TimeSpan.FromSeconds(30));
        Assert.True(mail != null, $"До {user.Email} не тръгна писмо — тестът не мери каквото твърди.");

        var after = FilesInSentEmails();

        Assert.True(after.Count == before.Count,
            $"В {FailingMailApp.SentEmailsDir} се появиха файлове: " +
            string.Join(", ", after.Except(before)));
    }

    /// <summary>
    /// The other side of [T-30]: the key is read nowhere in the application's
    /// code. Without this check the test above would also pass because the name
    /// of the setting had been mistyped.
    /// </summary>
    [Fact]
    public void Приложението_никъде_не_чете_SaveToDisk()
    {
        var hits = SourceFiles()
            .Where(f => File.ReadAllText(f).Contains("SaveToDisk", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(TestPaths.RepoRoot, f))
            .ToList();

        Assert.True(hits.Count == 0,
            "Ключът SaveToDisk вече се чете в: " + string.Join(", ", hits) +
            " — [T-30] е затворена, махни тези два теста.");
    }

    // ════════════════════════════════════════════════════════════════════

    private static List<string> FilesInSentEmails() =>
        Directory.Exists(FailingMailApp.SentEmailsDir)
            ? Directory.GetFiles(FailingMailApp.SentEmailsDir).Select(Path.GetFileName).ToList()!
            : new List<string>();

    /// <summary>The application's source, excluding the tests and the build output.</summary>
    private static IEnumerable<string> SourceFiles()
    {
        string[] folders = ["Services", "Controllers", "Pages", "Areas", "Data", "Helpers", "Models"];

        foreach (var folder in folders)
        {
            var full = Path.Combine(TestPaths.RepoRoot, folder);
            if (!Directory.Exists(full)) continue;

            foreach (var file in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
                yield return file;
        }

        yield return Path.Combine(TestPaths.RepoRoot, "Program.cs");
        yield return Path.Combine(TestPaths.RepoRoot, "appsettings.json");
    }

    private async Task<JsonElement> WaitForHealthAsync(string service, string status, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        JsonElement last = default;

        while (DateTime.UtcNow < deadline)
        {
            last = await HealthAsync(service);
            if (last.GetProperty("status").GetString() == status) return last;
            await Task.Delay(500);
        }

        return last;
    }
}
