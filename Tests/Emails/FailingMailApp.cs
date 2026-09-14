// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Emails;

/// <summary>
/// A second start of the application with an SMTP sink of its own, for the
/// questions the main instance cannot answer without wrecking the other tests.
/// <para>
/// There are two of them: a message failing FOR GOOD — the failure counter is per
/// process and the Health tab stays red for 24 hours afterwards — and the
/// <c>EmailSettings:SaveToDisk</c> setting, which the main instance does not set.
/// </para>
/// <para>
/// The database, the uploads and the keys belong to the probe instance, so
/// nothing here touches the main suite or <c>conferenceapp.db</c>.
/// </para>
/// </summary>
public sealed class FailingMailApp : IAsyncLifetime
{
    public SmtpSink     Sink  { get; } = new();
    public StartupProbe Probe { get; private set; } = null!;
    public TestDb       Db    { get; private set; } = null!;

    /// <summary>Where TESTS_PROMPT.md says the copy of a message should land.</summary>
    public static string SentEmailsDir { get; } =
        Path.Combine(TestPaths.RepoRoot, "App_Data", "sent-emails");

    public string BaseUrl => Probe.BaseUrl;

    public async Task InitializeAsync()
    {
        Sink.Start();

        Probe = await StartupProbe.StartAsync("mail-fail", settings =>
        {
            settings["EmailSettings:Port"]     = Sink.Port.ToString();
            settings["EmailSettings:Password"] = "not-used-by-the-sink";
            settings["EmailSettings:From"]     = "conference.education@unwe.bg";

            // The key from TESTS_PROMPT.md. The application reads it nowhere today,
            // which is exactly what EmailDeliveryFailureTests checks.
            settings["EmailSettings:SaveToDisk"] = "true";
        },
        // The probe starts with an empty webroot so that it does not touch the live
        // uploads. The mail templates are read from there, though, and without them
        // the renderer throws — at which point "a failed message" would mean
        // something else entirely.
        prepareWebRoot: probe => CopyTemplates(probe.WebRoot));

        if (Probe.StartupError != null)
            throw new InvalidOperationException(
                "Пробното копие за писмата не тръгна: " + Probe.StartupError.Message,
                Probe.StartupError);

        Db = Probe.Db();
    }

    /// <summary>Copies <c>wwwroot/templates</c> into the probe's webroot.</summary>
    private static void CopyTemplates(string webRoot)
    {
        var source = Path.Combine(TestPaths.RepoRoot, "wwwroot", "templates");
        var target = Path.Combine(webRoot, "templates");

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    public HttpSession NewSession() => new(Probe.BaseUrl, Db);

    public async Task<HttpSession> SignedInAdminAsync()
    {
        var session = NewSession();
        await session.LoginAdminAsync(
            TestCredentials.Current.AdminEmail, TestCredentials.Current.AdminPassword);
        return session;
    }

    public Task DisposeAsync()
    {
        Db.Dispose();
        return Sink.DisposeAsync().AsTask();
    }
}
