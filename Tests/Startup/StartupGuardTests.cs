// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Startup;

/// <summary>
/// Part 1, whether the application stops when the path to the keys or to
/// <c>App_Data</c> is wrong. That is deliberate: better not to start at all than
/// to start "successfully" and have every page answer 500.
/// <para>
/// The quality of the message is checked as well: it has to name the path and
/// the setting, because otherwise stopping is just as useless as failing
/// silently.
/// </para>
/// </summary>
public class StartupGuardTests
{
    /// <summary>
    /// A path that cannot be a directory: <c>/dev/null</c> is a file, so creating
    /// anything beneath it fails with ENOTDIR. More reliable than a folder
    /// without permissions, which succeeds when running as root.
    /// </summary>
    private const string UnusablePath = "/dev/null/confapp-tests";

    [Fact]
    public async Task Грешен_път_до_ключовете_спира_приложението_с_ясно_съобщение()
    {
        var probe = await StartupProbe.StartAsync("bad-keys", settings =>
        {
            settings["DataProtection:KeysPath"] = UnusablePath;
        }, expectFailure: true);

        Assert.NotNull(probe.StartupError);

        var message = Flatten(probe.StartupError!);

        // Which path.
        Assert.Contains(UnusablePath, message);
        // Which setting.
        Assert.Contains("DataProtection:KeysPath", message);

        // And "stops" means stops: nothing answers on the port.
        await AssertNothingListensAsync(probe);
    }

    [Fact]
    public async Task Грешен_път_до_качените_файлове_спира_приложението_с_ясно_съобщение()
    {
        var probe = await StartupProbe.StartAsync("bad-uploads", settings =>
        {
            settings["Uploads:PrivateRoot"] = UnusablePath;
        }, expectFailure: true);

        Assert.NotNull(probe.StartupError);

        var message = Flatten(probe.StartupError!);

        Assert.Contains(UnusablePath, message);
        Assert.Contains("Uploads:PrivateRoot", message);

        await AssertNothingListensAsync(probe);
    }

    [Fact]
    public async Task Правилните_пътища_не_спират_нищо()
    {
        // The other side of it: the same probe with sound paths has to start,
        // otherwise the two tests above only prove that something blows up.
        var probe = await StartupProbe.StartAsync("good-paths");

        Assert.Null(probe.StartupError);

        using var client = probe.NewClient();
        var response = await client.GetAsync("/");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// A failed start must not leave half an application answering requests;
    /// otherwise "stops" is just a word in the log.
    /// </summary>
    private static async Task AssertNothingListensAsync(StartupProbe probe)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        await Assert.ThrowsAnyAsync<Exception>(() => client.GetAsync(probe.BaseUrl + "/"));
    }

    /// <summary>The message plus everything nested inside it; the path may be in an inner exception.</summary>
    private static string Flatten(Exception ex)
    {
        var text = new System.Text.StringBuilder();

        for (Exception? current = ex; current != null; current = current.InnerException)
            text.AppendLine(current.Message);

        return text.ToString();
    }
}
