// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;
using ConferenceApp.Tests.Pages;
using Microsoft.Playwright;

namespace ConferenceApp.Tests.Theming;

/// <summary>
/// Part 9, the logo when the theme changes.
/// <para>
/// The logo is a white PNG, and a single token decides whether it stays white:
/// <c>filter: brightness(0) invert(var(--logo-invert, 1))</c>, where 1 turns it
/// white again and 0 leaves it black. Under a light theme the token therefore has
/// to be 0; otherwise a white logo sits on an almost white background and simply
/// vanishes.
/// </para>
/// <para>
/// The same mechanism is used in several places — the top bar, the footer and the
/// inner layouts — so all of them are checked, not only the top bar.
/// </para>
/// </summary>
public class LogoTests : ThemingTestBase
{
    public LogoTests(AppFixture app) : base(app, "th-logo") { }

    /// <summary>Where the logo lives: the page, and the selector of the image itself.</summary>
    private static readonly (string Path, string Selector, string Where)[] Places =
    {
        ("/",        ".topbar .mainLogo_Img", "лентата горе"),
        ("/",        ".footer img",           "футърът"),
        ("/Login",   ".auth-brand img",       "входът"),
        ("/Profile", ".pf-brand img",         "профилът")
    };

    /// <summary>The filter the CSS should have computed for a given value.</summary>
    private static string ExpectedFilter(string invert) => $"brightness(0) invert({invert})";

    [Theory]
    [MemberData(nameof(ThemeCatalog.Each), MemberType = typeof(ThemeCatalog))]
    public async Task Логото_следва_темата(string themeKey)
    {
        var theme = ThemeCatalog.Of(themeKey);
        await ActivateThemeAsync(themeKey);

        foreach (var (path, selector, where) in Places)
        {
            await using var visit = await OpenAsync(PublicPages.Of(path));

            var filter = await StyleAsync(visit.Page, selector, "filter");

            Assert.True(filter == ExpectedFilter(theme.Token("--logo-invert")),
                $"{theme}: логото в „{where}“ ({path}) е с филтър „{filter}“, " +
                $"а --logo-invert е {theme.Token("--logo-invert")}.");
        }
    }

    /// <summary>
    /// The same thing stated through the result rather than the value: under a
    /// light theme the logo has to be BLACK, that is, the inversion has to be
    /// off.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeCatalog.EachLight), MemberType = typeof(ThemeCatalog))]
    public async Task Светлата_тема_прави_логото_черно(string themeKey)
    {
        var theme = ThemeCatalog.Of(themeKey);
        await ActivateThemeAsync(themeKey);

        await using var visit = await OpenAsync(PublicPages.Of("/"));

        Assert.Equal("0", await VarAsync(visit.Page, "--logo-invert"));
        Assert.Equal(ExpectedFilter("0"),
            await StyleAsync(visit.Page, ".topbar .mainLogo_Img", "filter"));

        // And the background behind it really is light: a black logo on black is
        // the same mistake, only mirrored.
        var behind = await PaintedBehindAsync(visit.Page, ".topbar .mainLogo_Img");
        Assert.True(Contrast(behind.Color, "#000000") > 4.5,
            $"{theme}: зад логото стои {behind} — черно лого няма да се види.");
    }

    /// <summary>
    /// The logo file exists and loads. Inverting a colour does not help a missing
    /// image, and an empty space in the top bar raises no error on screen.
    /// </summary>
    [Fact]
    public async Task Логото_се_зарежда_наистина()
    {
        await using var visit = await OpenAsync(PublicPages.Of("/"));

        var width = await visit.Page.EvaluateAsync<int>(
            "() => document.querySelector('.topbar .mainLogo_Img')?.naturalWidth ?? 0");

        Assert.True(width > 0, "Логото в лентата не се зареди.");
    }

    /// <summary>
    /// With no theme the logo is white, the default from mainStyle.css. That is
    /// the site's normal state.
    /// </summary>
    [Fact]
    public async Task Без_тема_логото_остава_бяло()
    {
        await DeactivateThemeAsync();

        await using var visit = await OpenAsync(PublicPages.Of("/"));

        Assert.Equal("1", await VarAsync(visit.Page, "--logo-invert"));
        Assert.Equal(ExpectedFilter("1"),
            await StyleAsync(visit.Page, ".topbar .mainLogo_Img", "filter"));
    }
}
