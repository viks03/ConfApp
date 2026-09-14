// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Services.Theming;
using ConferenceApp.Tests.Fixtures;
using ConferenceApp.Tests.Pages;
using Microsoft.Playwright;

namespace ConferenceApp.Tests.Theming;

/// <summary>
/// Part 9, the themes seen through a browser.
/// <para>
/// Part 6 checked that activating a theme writes a row and that the CSS reaches
/// the body of the response. This is a different question: does what was saved
/// reach the rendered page — <c>getComputedStyle</c>, the colour of the text, the
/// contrast between the two — and on more than one page.
/// </para>
/// </summary>
public class ThemeVisualTests : ThemingTestBase
{
    public ThemeVisualTests(AppFixture app) : base(app, "th-vis") { }

    /// <summary>The value mainStyle.css holds when there is no theme.</summary>
    private static string Fallback(string token) =>
        ThemeTokens.All.First(s => s.Name == token).Default;

    // ════════════════════════════════════════════════════════════════════
    // Every theme, on three pages each
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The theme's tokens reach <c>:root</c> on each of the three pages and are
    /// really used: the top bar takes its background from <c>--bg</c> and the
    /// page's text from <c>--text</c>.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeCatalog.Each), MemberType = typeof(ThemeCatalog))]
    public async Task Темата_стига_до_всяка_от_трите_страници(string themeKey)
    {
        var theme = ThemeCatalog.Of(themeKey);
        await ActivateThemeAsync(themeKey);

        foreach (var path in ThemeCatalog.ThreePages)
        {
            await using var visit = await OpenAsync(PublicPages.Of(path));
            var page = visit.Page;

            foreach (var token in new[] { "--bg", "--text", "--accent", "--ink" })
                Assert.Equal(theme.Token(token), await VarAsync(page, token));

            // It is not enough for the variable to be set; it has to be used.
            Assert.Equal(ToRgb(theme.Token("--bg")),
                await StyleAsync(page, ".topbar", "background-color"));

            Assert.Equal(ToRgb(theme.Token("--text")),
                await StyleAsync(page, "body", "color"));

            Assert.True(visit.Errors.Count == 0,
                $"{theme} на {path} изкара в конзолата: {string.Join(" | ", visit.Errors)}");
        }
    }

    /// <summary>
    /// The contrast the guidance for themes demands: 7:1 for the main text, 4.5:1
    /// for the quieter text and 3:1 for the accent. The figures are measured
    /// against the values the browser computed, that is, against what a person
    /// really reads.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeCatalog.Each), MemberType = typeof(ThemeCatalog))]
    public async Task Текстът_на_темата_се_чете_върху_фона_ѝ(string themeKey)
    {
        var theme = ThemeCatalog.Of(themeKey);
        await ActivateThemeAsync(themeKey);

        await using var visit = await OpenAsync(PublicPages.Of("/"));
        var page = visit.Page;

        var bg = await VarAsync(page, "--bg");

        foreach (var (token, required) in new[]
                 {
                     ("--text",     7.0),
                     ("--muted",    4.5),
                     ("--text-dim", 4.5),
                     ("--accent",   3.0)
                 })
        {
            var value    = await VarAsync(page, token);
            var contrast = Contrast(value, bg);

            Assert.True(contrast >= required,
                $"{theme}: {token} ({value}) върху --bg ({bg}) дава контраст " +
                $"{contrast:0.00}:1, а иска поне {required:0.0}:1.");
        }
    }

    /// <summary>
    /// A light theme has to be light EVERYWHERE in the visible area, not only
    /// where the page paints something of its own.
    /// <para>
    /// It is measured at twenty-five points on screen, taking for each the colour
    /// actually visible at that spot. That is exactly how [T-33] was caught: six
    /// pages deliberately paint no background of their own and show through to the
    /// background of <c>body</c>, which was hard-coded in <c>mainStyle.css</c>.
    /// </para>
    /// <para>
    /// It walks the WHOLE catalogue rather than the three pages: the answer "which
    /// pages" is more useful than "did it fail".
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeCatalog.EachLight), MemberType = typeof(ThemeCatalog))]
    public async Task Светлата_тема_не_оставя_тъмни_страници(string themeKey)
    {
        var theme = ThemeCatalog.Of(themeKey);
        await ActivateThemeAsync(themeKey);

        var dark = new List<string>();

        foreach (var target in PublicPages.All)
        {
            await using var visit = await OpenAsync(target);
            await visit.Page.AcceptCookieNoticeAsync();
            await visit.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var spots = (await SampledBackgroundsAsync(visit.Page))
                .Select(sample => sample.Split('|'))
                .Where(parts => parts[1] != "image")            // a drawing is not measured by colour
                // The accent is deliberately saturated under a light theme as well: a
                // button with an accent background and --on-accent text on it is not a
                // "dark page". The colours derived from it (hover, active) are the
                // same colour darkened, which is where the tolerance comes from.
                .Where(parts => !IsNear(parts[1], theme.Token("--accent")))
                .Where(parts => Contrast(parts[1], "#ffffff") > 2.0)
                .ToArray();

            if (spots.Length > 0)
                dark.Add($"{target.Path} ({spots.Length}/25: " +
                         $"{spots[0][2]} е {spots[0][1]} на {spots[0][0]})");
        }

        Assert.True(dark.Count == 0,
            $"{theme}: {dark.Count} от {PublicPages.All.Count} страници показват тъмен фон, " +
            $"а темата е светла (--bg е {theme.Token("--bg")}) — " +
            string.Join("; ", dark));
    }

    // ════════════════════════════════════════════════════════════════════
    // Switching
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A switch takes effect at once, with no restart: the theme's CSS is cached in
    /// <c>ThemeProvider</c> and the admin panel clears that cache itself. The test
    /// goes through two different themes in one process.
    /// </summary>
    [Fact]
    public async Task Смяната_на_тема_се_вижда_веднага_без_рестарт()
    {
        var first  = ThemeCatalog.All.First(t => !t.IsLight);
        var second = ThemeCatalog.All.First(t => t.IsLight);

        await ActivateThemeAsync(first.Key);
        await using (var before = await OpenAsync(PublicPages.Of("/")))
            Assert.Equal(first.Token("--bg"), await VarAsync(before.Page, "--bg"));

        await ActivateThemeAsync(second.Key);
        await using (var after = await OpenAsync(PublicPages.Of("/")))
            Assert.Equal(second.Token("--bg"), await VarAsync(after.Page, "--bg"));
    }

    /// <summary>
    /// Switching off returns the site to the values in <c>mainStyle.css</c>: the
    /// normal state, in which there is simply no theme.
    /// </summary>
    [Fact]
    public async Task Изключената_тема_връща_стандартните_цветове()
    {
        await ActivateThemeAsync(ThemeCatalog.All.First(t => t.IsLight).Key);
        await DeactivateThemeAsync();

        await using var visit = await OpenAsync(PublicPages.Of("/"));

        Assert.Equal(Fallback("--bg"),   await VarAsync(visit.Page, "--bg"));
        Assert.Equal(Fallback("--text"), await VarAsync(visit.Page, "--text"));
        Assert.Equal(Fallback("--ink"),  await VarAsync(visit.Page, "--ink"));

        // And the logo goes back to white, the default value.
        Assert.Equal(Fallback("--logo-invert"), await VarAsync(visit.Page, "--logo-invert"));
    }

    /// <summary>
    /// A theme must not depend on the language: the same values in English too.
    /// </summary>
    [Fact]
    public async Task Темата_е_една_и_съща_на_двата_езика()
    {
        var theme = ThemeCatalog.All.First(t => t.IsLight);
        await ActivateThemeAsync(theme.Key);

        foreach (var culture in new[] { "bg", "en" })
        {
            await using var visit = await OpenAsync(PublicPages.Of("/"), culture);
            Assert.Equal(theme.Token("--bg"), await VarAsync(visit.Page, "--bg"));
        }
    }
}
