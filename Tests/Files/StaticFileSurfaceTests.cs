// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Files;

/// <summary>
/// Part 5, what the static-file middleware serves at all.
/// <para>
/// It does not ask who you are: everything under <c>wwwroot</c> is public by
/// definition. The question is therefore not whether this visitor is allowed,
/// but what is left under <c>wwwroot</c>. The test walks the boundary from both
/// sides: the old folders for personal files, and the folders that have to stay
/// public.
/// </para>
/// </summary>
[Collection(AppCollection.Name)]
public class StaticFileSurfaceTests
{
    private readonly AppFixture _app;

    public StaticFileSurfaceTests(AppFixture app) => _app = app;

    private async Task<HttpStatusCode> GetAsync(string url)
    {
        using var client = _app.NewClient(followRedirects: false);
        using var response = await client.GetAsync(url);
        return response.StatusCode;
    }

    // ════════════════════════════════════════════════════════════════════
    // The old addresses
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("/uploads/papers26/")]
    [InlineData("/uploads/papers26/nyama-go.pdf")]
    [InlineData("/uploads/submitted-documents/")]
    [InlineData("/uploads/submitted-documents/students/nyama-go.png")]
    [InlineData("/uploads/submitted-documents/journalists/nyama-go.jpg")]
    public async Task Старите_папки_за_лични_файлове_не_раздават_нищо(string url) =>
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync(url));

    /// <summary>
    /// There is no directory listing either: without one a file name has to be
    /// guessed, with one the whole folder can simply be read off.
    /// </summary>
    [Theory]
    [InlineData("/uploads/")]
    [InlineData("/uploads/documents/")]
    public async Task Папките_не_показват_списък_на_съдържанието_си(string url) =>
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync(url));

    /// <summary>
    /// Climbing out of the folder through the address itself. Kestrel normalises
    /// before routing, but the check is cheap and guards exactly that.
    /// </summary>
    [Theory]
    [InlineData("/uploads/documents/../../appsettings.json")]
    [InlineData("/uploads/documents/..%2f..%2fappsettings.json")]
    [InlineData("/uploads/documents/..%252f..%252fappsettings.json")]
    [InlineData("/css/../appsettings.json")]
    [InlineData("/appsettings.json")]
    [InlineData("/appsettings.Development.json")]
    [InlineData("/conferenceapp.db")]
    [InlineData("/Program.cs")]
    [InlineData("/App_Data/test.db")]
    [InlineData("/DataProtection-Keys/")]
    public async Task Извън_wwwroot_не_се_раздава_нищо(string url)
    {
        var status = await GetAsync(url);

        Assert.True(status is HttpStatusCode.NotFound or HttpStatusCode.BadRequest
                            or HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect,
            $"{url} върна {(int)status} — това не бива да се раздава.");

        if (status is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect)
        {
            // A redirect is acceptable only if it leads nowhere.
            using var client = _app.NewClient();
            using var followed = await client.GetAsync(url);
            Assert.NotEqual(HttpStatusCode.OK, followed.StatusCode);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // And the other way round: what is public stays public
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("/css/authStyle.css")]
    [InlineData("/js/validation.js")]
    public async Task Публичните_ресурси_продължават_да_се_раздават(string url)
    {
        // If wwwroot ever stopped being served altogether, the checks above would
        // pass for the wrong reason. This test guards the other side.
        Assert.Equal(HttpStatusCode.OK, await GetAsync(url));
    }

    [Fact]
    public async Task Каченото_от_панела_си_остава_публично()
    {
        // The folder holding the documents for authors lives under wwwroot
        // deliberately: the programme and the templates are public documents, not
        // personal data.
        var folder = Path.Combine(TestPaths.RepoRoot, "wwwroot", "uploads", "documents");
        Assert.True(Directory.Exists(folder),
            "wwwroot/uploads/documents липсва — таб Downloads няма къде да пише.");

        var probe = Path.Combine(folder, $"test-probe-{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(probe, "%PDF-1.4\n%%EOF\n");

        try
        {
            Assert.Equal(HttpStatusCode.OK,
                await GetAsync("/uploads/documents/" + Path.GetFileName(probe)));
        }
        finally
        {
            File.Delete(probe);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // The headers on what is served
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Раздаваните_файлове_носят_nosniff()
    {
        // The second layer of protection around uploaded files: without nosniff the
        // browser decides the type by the content rather than by the extension.
        using var client = _app.NewClient();
        using var response = await client.GetAsync("/css/authStyle.css");

        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));
    }
}
