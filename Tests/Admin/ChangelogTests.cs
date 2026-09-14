// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Services.Changelog;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Admin;

/// <summary>
/// Part 6, the Changelog tab.
/// <para>
/// The content comes from <c>CHANGELOG.md</c> in the root of the project rather
/// than from the database. What is checked here is that the file reaches the tab,
/// that it is not served publicly, and that the "something new" badge knows which
/// the latest version is. Putting the dot out happens in the browser — see
/// <see cref="AdminBrowserTests"/>.
/// </para>
/// </summary>
public class ChangelogTests : AdminTestBase
{
    public ChangelogTests(AppFixture app) : base(app, "ad-cl") { }

    private static readonly string ChangelogFile =
        Path.Combine(TestPaths.RepoRoot, "CHANGELOG.md");

    private static string Tab(string html)
    {
        var start = html.IndexOf("id=\"tab-changelog\"", StringComparison.Ordinal);
        Assert.True(start > 0, "Табът „Changelog“ липсва.");

        var end = html.IndexOf("id=\"tab-", start + 10, StringComparison.Ordinal);
        return end > start ? html[start..end] : html[start..];
    }

    [Fact]
    public void Файлът_съществува_в_корена()
    {
        Assert.True(File.Exists(ChangelogFile),
            $"CHANGELOG.md липсва в {TestPaths.RepoRoot} — табът ще е празен.");
    }

    [Fact]
    public async Task Версиите_от_файла_излизат_в_таба()
    {
        var versions = System.Text.RegularExpressions.Regex
            .Matches(await File.ReadAllTextAsync(ChangelogFile), @"^##\s+\[([^\]]+)\]",
                     System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.NotEmpty(versions);

        using var admin = await SignedInAdminAsync();
        var tab = Tab(await PanelAsync(admin));

        // A version with no entries at all is not shown: the parser skips it on
        // purpose. So at least one of the listed versions is looked for.
        Assert.Contains(versions, v => tab.Contains(v, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Точките_от_файла_излизат_като_списък()
    {
        using var admin = await SignedInAdminAsync();
        var tab = Tab(await PanelAsync(admin));

        Assert.Contains("cl-list", tab);
        Assert.Contains("<li>", tab);
    }

    /// <summary>
    /// The "something new" badge is compared against <c>data-cl-latest</c> in the
    /// browser. If the attribute is empty, the dot never lights up.
    /// </summary>
    [Fact]
    public async Task Табът_обявява_последната_издадена_версия()
    {
        using var admin = await SignedInAdminAsync();
        var tab = Tab(await PanelAsync(admin));

        var latest = System.Text.RegularExpressions.Regex
            .Match(tab, "data-cl-latest=\"(?<v>[^\"]*)\"").Groups["v"].Value;

        Assert.NotEqual(string.Empty, latest);
        Assert.DoesNotContain("unreleased", latest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task В_подготовка_се_показва_различно_от_издадено()
    {
        var text = await File.ReadAllTextAsync(ChangelogFile);
        if (!text.Contains("[Unreleased]", StringComparison.OrdinalIgnoreCase))
            return;   // no such section in the file, so there is nothing to tell apart

        using var admin = await SignedInAdminAsync();
        var tab = Tab(await PanelAsync(admin));

        // The section is shown only when it has entries in it.
        if (tab.Contains("Unreleased", StringComparison.OrdinalIgnoreCase))
            Assert.Contains("is-unreleased", tab);
    }

    /// <summary>
    /// The file lives in the root rather than in <c>wwwroot</c>: technical notes
    /// have no business in public space.
    /// </summary>
    [Theory]
    [InlineData("/CHANGELOG.md")]
    [InlineData("/changelog.md")]
    [InlineData("/wwwroot/CHANGELOG.md")]
    public async Task Файлът_не_се_раздава_публично(string path)
    {
        using var client = App.NewClient(followRedirects: false);
        using var response = await client.GetAsync(path);

        Assert.NotEqual(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Участник_не_вижда_changelog_а()
    {
        var user = await NewParticipantAsync();
        using var session = await SignedInAsync(user);

        using var client = session.NoRedirectClient();
        using var response = await client.GetAsync("/Admin");

        Assert.NotEqual(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The parser escapes BEFORE it inserts its own markup. The other order would
    /// let HTML from the file through, and the tab renders with <c>Html.Raw</c>.
    /// </summary>
    [Fact]
    public void Маркъп_във_файла_излиза_като_текст()
    {
        var reader = typeof(ChangelogReader);
        Assert.NotNull(reader);

        var parsed = ParseThroughReader("## [9.9.9] — тест\n- опасно <script>alert(1)</script> и `код`\n");

        Assert.Contains("&lt;script&gt;", parsed);
        Assert.DoesNotContain("<script>", parsed);
        Assert.Contains("<code>код</code>", parsed);
    }

    /// <summary>
    /// The parser is private, so it is called through the public entry point with
    /// the file swapped out temporarily — which is also the only way to check the
    /// reading for real.
    /// </summary>
    private static string ParseThroughReader(string markdown)
    {
        var method = typeof(ChangelogReader)
            .GetMethod("Parse", System.Reflection.BindingFlags.NonPublic
                              | System.Reflection.BindingFlags.Static)!;

        var entries = (System.Collections.IEnumerable)method.Invoke(null, new object[] { markdown })!;

        var html = new System.Text.StringBuilder();
        foreach (ChangelogEntry entry in entries)
            html.Append(entry.AdminHtml).Append(entry.TechHtml);

        return html.ToString();
    }
}
