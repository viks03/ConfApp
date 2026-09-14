// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text;
using ConferenceApp.Models;
using ConferenceApp.Services.Theming;
using ConferenceApp.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Tests.Admin;

/// <summary>
/// Part 6, the Themes tab: the built-in themes, uploading one's own, activating
/// and deleting.
/// <para>
/// What was asked for explicitly here is that a switch must take effect at once,
/// without a restart. The test therefore does not stop at the row in the database
/// but asks a public page what it actually renders.
/// </para>
/// </summary>
public class ThemesTests : AdminTestBase
{
    public ThemesTests(AppFixture app) : base(app, "ad-thm") { }

    private static readonly string ThemesDir =
        Path.Combine(TestPaths.RepoRoot, "wwwroot", "themes");

    private Task<List<SiteTheme>> ThemesAsync() =>
        App.Db.ReadAsync(db => db.SiteThemes.AsNoTracking().OrderBy(t => t.Id).ToListAsync());

    private Task<SiteTheme?> ThemeAsync(string key) =>
        App.Db.ReadAsync(db => db.SiteThemes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.ThemeKey == key));

    /// <summary>Switches every theme off: the site's default state.</summary>
    private async Task DeactivateAllAsync(HttpSession admin)
    {
        var reply = await PostAsync(admin, "ActivateTheme",
            new Dictionary<string, string> { ["themeKey"] = string.Empty });
        Assert.True(reply.Success, reply.Message);
    }

    public override async Task DisposeAsync()
    {
        // The theme is global: left active, it is visible to every later test and to
        // the next run of the application.
        using (var admin = await SignedInAdminAsync())
            await DeactivateAllAsync(admin);

        await base.DisposeAsync();
    }

    /// <summary>A valid theme, built from the default value of every token.</summary>
    private static string ValidThemeJson(string name, string accent = "#3366FF")
    {
        var tokens = new StringBuilder();
        foreach (var spec in ThemeTokens.All)
        {
            if (tokens.Length > 0) tokens.Append(",\n");
            var value = spec.Name == "--accent" ? accent : spec.Default;
            tokens.Append($"    \"{spec.Name}\": \"{value.Replace("\"", "\\\"")}\"");
        }

        return "{\n  \"name\": \"" + name + "\",\n  \"tokens\": {\n" + tokens + "\n  }\n}";
    }

    private static UploadFile ThemeFile(string json, string name = "theme.json") =>
        new("file", name, Encoding.UTF8.GetBytes(json), "application/json");

    // ════════════════════════════════════════════════════════════════════
    // The built-in ones
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Вградените_теми_се_засяват_от_файловете_в_wwwroot()
    {
        using var admin = await SignedInAdminAsync();
        await PanelAsync(admin);      // opening it is what seeds them

        var onDisk = Directory.GetFiles(ThemesDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(onDisk);

        var seeded = (await ThemesAsync()).Where(t => t.IsBuiltIn)
            .Select(t => t.ThemeKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(onDisk, seeded);
    }

    [Fact]
    public async Task Второто_отваряне_не_удвоява_вградените()
    {
        using var admin = await SignedInAdminAsync();

        await PanelAsync(admin);
        var first = (await ThemesAsync()).Count(t => t.IsBuiltIn);

        await PanelAsync(admin);
        var second = (await ThemesAsync()).Count(t => t.IsBuiltIn);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Нищо_не_се_активира_само()
    {
        using var admin = await SignedInAdminAsync();
        await PanelAsync(admin);
        await DeactivateAllAsync(admin);

        Assert.DoesNotContain(await ThemesAsync(), t => t.IsActive);
    }

    [Fact]
    public async Task Шаблонът_за_нова_тема_се_сваля()
    {
        using var admin = await SignedInAdminAsync();

        using var response = await admin.Client.GetAsync("/Admin?handler=ThemeTemplate");
        response.EnsureSuccessStatusCode();

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        // The template has to explain every token the validator knows about.
        foreach (var spec in ThemeTokens.All)
            Assert.Contains(spec.Name, json);
    }

    // ════════════════════════════════════════════════════════════════════
    // Activating
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Активирането_вдига_точно_една_тема()
    {
        using var admin = await SignedInAdminAsync();
        await PanelAsync(admin);

        var builtIn = (await ThemesAsync()).First(t => t.IsBuiltIn);

        var reply = await PostAsync(admin, "ActivateTheme",
            new Dictionary<string, string> { ["themeKey"] = builtIn.ThemeKey });

        Assert.True(reply.Success, reply.Message);
        Assert.Contains(builtIn.Name, reply.Message);

        var all = await ThemesAsync();
        Assert.Single(all.Where(t => t.IsActive));
        Assert.Equal(builtIn.ThemeKey, all.First(t => t.IsActive).ThemeKey);
    }

    [Fact]
    public async Task Втора_тема_гаси_първата()
    {
        using var admin = await SignedInAdminAsync();
        await PanelAsync(admin);

        var themes = (await ThemesAsync()).Where(t => t.IsBuiltIn).Take(2).ToList();
        Assert.Equal(2, themes.Count);

        foreach (var theme in themes)
            Assert.True((await PostAsync(admin, "ActivateTheme",
                new Dictionary<string, string> { ["themeKey"] = theme.ThemeKey })).Success);

        var active = (await ThemesAsync()).Where(t => t.IsActive).ToList();
        Assert.Single(active);
        Assert.Equal(themes[1].ThemeKey, active[0].ThemeKey);
    }

    [Fact]
    public async Task Празен_ключ_изключва_темата()
    {
        using var admin = await SignedInAdminAsync();
        await PanelAsync(admin);

        var builtIn = (await ThemesAsync()).First(t => t.IsBuiltIn);
        await PostAsync(admin, "ActivateTheme",
            new Dictionary<string, string> { ["themeKey"] = builtIn.ThemeKey });

        var reply = await PostAsync(admin, "ActivateTheme",
            new Dictionary<string, string> { ["themeKey"] = string.Empty });

        Assert.True(reply.Success, reply.Message);
        Assert.DoesNotContain(await ThemesAsync(), t => t.IsActive);
    }

    [Fact]
    public async Task Непозната_тема_не_се_активира()
    {
        using var admin = await SignedInAdminAsync();
        await PanelAsync(admin);

        var reply = await PostAsync(admin, "ActivateTheme",
            new Dictionary<string, string> { ["themeKey"] = "no-such-theme" });

        Assert.False(reply.Success);
        Assert.DoesNotContain(await ThemesAsync(), t => t.IsActive);
    }

    [Fact]
    public async Task Активирането_оставя_запис_в_одита()
    {
        using var admin = await SignedInAdminAsync();
        await PanelAsync(admin);

        var lastId = await App.Db.LastAuditIdAsync();
        var builtIn = (await ThemesAsync()).First(t => t.IsBuiltIn);

        await PostAsync(admin, "ActivateTheme",
            new Dictionary<string, string> { ["themeKey"] = builtIn.ThemeKey });

        var since = await App.Db.AuditSinceAsync(lastId);
        var entry = since.FirstOrDefault(a => a.Action == "Theme Activated");

        Assert.NotNull(entry);
        Assert.Equal(builtIn.ThemeKey, entry!.Details);
    }

    /// <summary>
    /// What was asked for explicitly: an activation has to be visible at once,
    /// without a restart. The active theme's tokens have to appear in the HTML of a
    /// public page.
    /// </summary>
    [Fact]
    public async Task Активираната_тема_се_вижда_на_публична_страница()
    {
        using var admin = await SignedInAdminAsync();
        await PanelAsync(admin);

        var before = await PublicPageAsync("/");

        var builtIn = (await ThemesAsync()).First(t => t.IsBuiltIn);
        Assert.True((await PostAsync(admin, "ActivateTheme",
            new Dictionary<string, string> { ["themeKey"] = builtIn.ThemeKey })).Success);

        var after = await PublicPageAsync("/");

        var expected = ThemeTokens.Parse(builtIn.TokensJson);
        Assert.True(expected.Ok, "Вградената тема не минава собствената си проверка.");

        Assert.NotEqual(before, after);
        Assert.Contains(ThemeTokens.BuildCss(expected.Values), after);
    }

    /// <summary>The same for an uploaded theme, and for switching it off again.</summary>
    [Fact]
    public async Task Изключването_на_темата_връща_страницата_каквато_беше()
    {
        using var admin = await SignedInAdminAsync();
        await PanelAsync(admin);

        var name = Unique("theme");
        var upload = await PostMultipartAsync(admin, "UploadTheme",
            new Dictionary<string, string>(), new[] { ThemeFile(ValidThemeJson(name, "#123456")) });
        Assert.True(upload.Success, upload.Message);

        var mine = (await ThemesAsync()).First(t => t.Name == name);
        TrackTheme(mine.ThemeKey);

        var plain = await PublicPageAsync("/");
        Assert.DoesNotContain(ThemeBlockStart, plain);

        await PostAsync(admin, "ActivateTheme",
            new Dictionary<string, string> { ["themeKey"] = mine.ThemeKey });
        var themed = await PublicPageAsync("/");
        Assert.Contains("--accent:#123456", themed);

        await DeactivateAllAsync(admin);
        var again = await PublicPageAsync("/");

        Assert.DoesNotContain("--accent:#123456", again);

        // "As it was" is judged by the absence of the block rather than by the
        // length of the page: _Layout.cshtml deliberately picks five random quick
        // links for the footer on EVERY load, so two consecutive loads of the same
        // page legitimately differ in length.
        Assert.DoesNotContain(ThemeBlockStart, again);
    }

    /// <summary>The start of the block ThemeProvider puts into &lt;head&gt;.</summary>
    private const string ThemeBlockStart = "<style>:root{";

    private async Task<string> PublicPageAsync(string path)
    {
        using var client = App.NewClient();
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.ReadPageAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // Uploading
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Валидна_тема_се_качва_но_не_се_активира()
    {
        using var admin = await SignedInAdminAsync();
        await PanelAsync(admin);
        await DeactivateAllAsync(admin);

        var name = Unique("theme");
        var reply = await PostMultipartAsync(admin, "UploadTheme",
            new Dictionary<string, string>(), new[] { ThemeFile(ValidThemeJson(name)) });

        Assert.True(reply.Success, reply.Message);

        var saved = (await ThemesAsync()).FirstOrDefault(t => t.Name == name);
        Assert.NotNull(saved);
        TrackTheme(saved!.ThemeKey);

        Assert.False(saved.IsBuiltIn);
        Assert.False(saved.IsActive);
        Assert.Equal(App.Credentials.AdminEmail, saved.UpdatedBy);
        Assert.DoesNotContain(await ThemesAsync(), t => t.IsActive);
    }

    [Fact]
    public async Task Две_теми_с_едно_име_не_се_презаписват()
    {
        using var admin = await SignedInAdminAsync();
        await PanelAsync(admin);

        var name = Unique("theme");

        for (var i = 0; i < 2; i++)
            Assert.True((await PostMultipartAsync(admin, "UploadTheme",
                new Dictionary<string, string>(),
                new[] { ThemeFile(ValidThemeJson(name)) })).Success);

        var both = (await ThemesAsync()).Where(t => t.Name == name).ToList();
        foreach (var theme in both) TrackTheme(theme.ThemeKey);

        Assert.Equal(2, both.Count);
        Assert.Equal(2, both.Select(t => t.ThemeKey).Distinct().Count());
    }

    [Fact]
    public async Task Заявка_без_файл_се_отказва()
    {
        using var admin = await SignedInAdminAsync();

        var reply = await PostAsync(admin, "UploadTheme");
        Assert.False(reply.Success);
        Assert.Contains("файл", reply.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Файл_който_не_е_JSON_се_отказва()
    {
        using var admin = await SignedInAdminAsync();

        var reply = await PostMultipartAsync(admin, "UploadTheme",
            new Dictionary<string, string>(),
            new[] { ThemeFile("това не е тема", "theme.json") });

        Assert.False(reply.Success);
        Assert.Contains("errors", reply.Raw);
    }

    [Fact]
    public async Task Тема_с_невалидна_стойност_се_отказва_с_обяснение()
    {
        using var admin = await SignedInAdminAsync();

        var broken = "{\"name\":\"" + Unique("bad") + "\",\"tokens\":{\"--bg\":\"не-е-цвят\"}}";

        var reply = await PostMultipartAsync(admin, "UploadTheme",
            new Dictionary<string, string>(), new[] { ThemeFile(broken) });

        Assert.False(reply.Success);
        Assert.Contains("--bg", reply.Raw);
    }

    [Fact]
    public async Task Твърде_голям_файл_за_тема_се_отказва()
    {
        using var admin = await SignedInAdminAsync();

        var big = ThemeFile(ValidThemeJson(Unique("big"))) with
        {
            Content = new byte[201 * 1024]
        };

        var reply = await PostMultipartAsync(admin, "UploadTheme",
            new Dictionary<string, string>(), new[] { big });

        Assert.False(reply.Success);
        Assert.Contains("голям", reply.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Качването_оставя_запис_в_одита()
    {
        using var admin = await SignedInAdminAsync();
        var lastId = await App.Db.LastAuditIdAsync();

        var name = Unique("theme");
        await PostMultipartAsync(admin, "UploadTheme",
            new Dictionary<string, string>(), new[] { ThemeFile(ValidThemeJson(name)) });

        var saved = (await ThemesAsync()).FirstOrDefault(t => t.Name == name);
        if (saved != null) TrackTheme(saved.ThemeKey);

        var since = await App.Db.AuditSinceAsync(lastId);
        Assert.Contains(since, a => a.Action == "Theme Uploaded" && a.Details.Contains(name));
    }

    // ════════════════════════════════════════════════════════════════════
    // Deleting
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Качена_тема_се_изтрива()
    {
        using var admin = await SignedInAdminAsync();

        var name = Unique("theme");
        await PostMultipartAsync(admin, "UploadTheme",
            new Dictionary<string, string>(), new[] { ThemeFile(ValidThemeJson(name)) });

        var mine = (await ThemesAsync()).First(t => t.Name == name);

        var reply = await PostAsync(admin, "DeleteTheme",
            new Dictionary<string, string> { ["themeKey"] = mine.ThemeKey });

        Assert.True(reply.Success, reply.Message);
        Assert.Null(await ThemeAsync(mine.ThemeKey));
    }

    [Fact]
    public async Task Изтриването_на_активна_тема_връща_сайта_към_стандартното()
    {
        using var admin = await SignedInAdminAsync();

        var name = Unique("theme");
        await PostMultipartAsync(admin, "UploadTheme",
            new Dictionary<string, string>(), new[] { ThemeFile(ValidThemeJson(name, "#654321")) });

        var mine = (await ThemesAsync()).First(t => t.Name == name);
        await PostAsync(admin, "ActivateTheme",
            new Dictionary<string, string> { ["themeKey"] = mine.ThemeKey });

        Assert.Contains("--accent:#654321", await PublicPageAsync("/"));

        var reply = await PostAsync(admin, "DeleteTheme",
            new Dictionary<string, string> { ["themeKey"] = mine.ThemeKey });

        Assert.True(reply.Success, reply.Message);
        Assert.DoesNotContain("--accent:#654321", await PublicPageAsync("/"));
    }

    [Fact]
    public async Task Вградена_тема_не_се_трие()
    {
        using var admin = await SignedInAdminAsync();
        await PanelAsync(admin);

        var builtIn = (await ThemesAsync()).First(t => t.IsBuiltIn);

        var reply = await PostAsync(admin, "DeleteTheme",
            new Dictionary<string, string> { ["themeKey"] = builtIn.ThemeKey });

        Assert.False(reply.Success);
        Assert.NotNull(await ThemeAsync(builtIn.ThemeKey));
    }

    [Fact]
    public async Task Изтриване_на_несъществуваща_тема_казва_какво_има()
    {
        using var admin = await SignedInAdminAsync();

        var reply = await PostAsync(admin, "DeleteTheme",
            new Dictionary<string, string> { ["themeKey"] = "no-such-theme" });

        Assert.False(reply.Success);
        Assert.Contains("Няма", reply.Message);
    }
}
