// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Services.Styles;
using ConferenceApp.Tests.Fixtures;
using ConferenceApp.Tests.Pages;
using Microsoft.Playwright;

namespace ConferenceApp.Tests.Theming;

/// <summary>
/// Part 9, the background presets seen through a browser.
/// <para>
/// Part 6 checked that a choice made in the admin panel is saved and comes back
/// as <c>data-gfx-bg</c> on the body of the response. The question here is the
/// next one: does anything actually get drawn. An attribute no CSS rule matches
/// is exactly as invisible as a missing attribute, and only a browser can tell.
/// </para>
/// </summary>
public class BackgroundPresetTests : ThemingTestBase
{
    public BackgroundPresetTests(AppFixture app) : base(app, "th-bg") { }

    /// <summary>The page experimented on, deliberately not the one part 6 uses.</summary>
    private const string PageKey = "/FAQ";

    public static IEnumerable<object[]> EachPreset() =>
        AmbientBackgrounds.Slugs.Where(s => s != "off").Select(s => new object[] { s });

    /// <summary>Opens the page with a given preset, set through the admin panel as a person would.</summary>
    private async Task<PageVisit> WithPresetAsync(
        string background, string? motion = null, string? speed = null,
        bool showOnMobile = false, int width = 1440, int height = 900)
    {
        await SavePageStyleAsync(StyleForm(PageKey, background, motion, speed, showOnMobile));

        var visit = await OpenAsync(PublicPages.Of(PageKey), "bg", width, height);
        await visit.Page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        return visit;
    }

    /// <summary>Whether this layer draws anything at all.</summary>
    private static async Task<string> LayerPaintAsync(IPage page, string layer) =>
        await StyleAsync(page, $"#ambient-fx .{layer}", "background-image");

    // ════════════════════════════════════════════════════════════════════
    // Is it visible
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Each preset's layer is present on screen and draws exactly what
    /// <c>globalEffects.css</c> promises for it.
    /// </summary>
    [Theory]
    [MemberData(nameof(EachPreset))]
    public async Task Присетът_рисува_това_което_обещава(string background)
    {
        await using var visit = await WithPresetAsync(background);
        var page = visit.Page;

        var layer = page.Locator("#ambient-fx");
        await Assertions.Expect(layer).ToHaveAttributeAsync("data-gfx-bg", background);

        Assert.NotEqual("none", await StyleAsync(page, "#ambient-fx", "display"));

        var promised = AmbientCss.LayersOf(background).ToArray();
        Assert.True(promised.Length > 0,
            $"За „{background}“ в globalEffects.css няма нито едно правило с чертеж.");

        foreach (var name in promised)
        {
            var paint = await LayerPaintAsync(page, name);

            Assert.True(paint != "none",
                $"Присетът „{background}“ има правило за .{name} в globalEffects.css, " +
                $"но браузърът не рисува нищо в този слой (background-image: none). " +
                $"Обикновено значи селектор, който не хваща.");
        }
    }

    /// <summary>
    /// The presets really are DIFFERENT from one another. A dozen choices that
    /// produce the same picture are one choice under a dozen names.
    /// </summary>
    [Fact]
    public async Task Присетите_се_различават_един_от_друг()
    {
        var seen = new Dictionary<string, string>();

        foreach (var background in AmbientBackgrounds.Slugs.Where(s => s != "off"))
        {
            await using var visit = await WithPresetAsync(background);

            var fingerprint =
                await LayerPaintAsync(visit.Page, "afx-a") + " ‖ " +
                await LayerPaintAsync(visit.Page, "afx-b");

            var twin = seen.FirstOrDefault(kv => kv.Value == fingerprint);
            Assert.True(twin.Key is null,
                $"„{background}“ изглежда точно като „{twin.Key}“.");

            seen[background] = fingerprint;
        }

        Assert.Equal(AmbientBackgrounds.Slugs.Count - 1, seen.Count);
    }

    /// <summary>"No background" removes the layer and the progress bar, not only the drawing.</summary>
    [Fact]
    public async Task Изборът_без_фон_маха_целия_слой()
    {
        await using var visit = await WithPresetAsync("off");

        Assert.Equal(0, await visit.Page.Locator("#ambient-fx").CountAsync());
        Assert.Equal(0, await visit.Page.Locator("#scroll-progress").CountAsync());
    }

    // ════════════════════════════════════════════════════════════════════
    // The motion
    // ════════════════════════════════════════════════════════════════════

    /// <summary>The chosen kind of motion shows up as an animation on the layers.</summary>
    [Theory]
    [InlineData("drift",   "afxUDrift")]
    [InlineData("slide",   "afxUSlide")]
    [InlineData("swell",   "afxUSwell")]
    [InlineData("breathe", "afxUBreathe")]
    [InlineData("turn",    "afxUTurn")]
    public async Task Избраното_движение_наистина_върви(string motion, string keyframes)
    {
        await using var visit = await WithPresetAsync("grid", motion);

        Assert.Equal(keyframes, await StyleAsync(visit.Page, "#ambient-fx .afx-a", "animation-name"));
        Assert.Equal("infinite",
            await StyleAsync(visit.Page, "#ambient-fx .afx-a", "animation-iteration-count"));
    }

    /// <summary>"Stopped" means stopped: for both layers and for the spot under the cursor.</summary>
    [Fact]
    public async Task Спряното_движение_спира_и_трите_слоя()
    {
        await using var visit = await WithPresetAsync("grid", "off");

        foreach (var layer in new[] { "afx-a", "afx-b", "afx-cursor" })
            Assert.Equal("none",
                await StyleAsync(visit.Page, $"#ambient-fx .{layer}", "animation-name"));
    }

    /// <summary>
    /// Speed changes the duration rather than the kind of motion. The figures are
    /// in the CSS, and those are the ones that have to reach the screen.
    /// </summary>
    [Theory]
    [InlineData("slower", "200s")]
    [InlineData("slow",   "120s")]
    [InlineData("fast",   "40s")]
    [InlineData("faster", "22s")]
    public async Task Скоростта_мени_продължителността(string speed, string expected)
    {
        await using var visit = await WithPresetAsync("grid", "drift", speed);

        Assert.Equal(expected,
            await StyleAsync(visit.Page, "#ambient-fx .afx-a", "animation-duration"));
        Assert.Equal("afxUDrift",
            await StyleAsync(visit.Page, "#ambient-fx .afx-a", "animation-name"));
    }

    /// <summary>
    /// The live presets do NOT take motion from elsewhere. They are drawn in
    /// screen coordinates, so moving the whole layer would tear the drawing away
    /// from the cursor and the lantern would no longer be under the mouse. The
    /// choice is therefore ignored on purpose, and that has to stay true.
    /// </summary>
    [Theory]
    [InlineData("lantern")]
    [InlineData("spine")]
    [InlineData("ascent")]
    public async Task Живите_присети_не_приемат_чуждо_движение(string background)
    {
        await using var visit = await WithPresetAsync(background, "turn", "faster");

        Assert.Equal("none", await StyleAsync(visit.Page, "#ambient-fx .afx-a", "animation-name"));
        Assert.Equal("none", await StyleAsync(visit.Page, "#ambient-fx .afx-a", "transform"));
    }

    // ════════════════════════════════════════════════════════════════════
    // On a phone
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// On a narrow screen the layer is hidden by default: a full-screen gradient
    /// recomputed on every scroll, on a device running off a battery.
    /// </summary>
    [Fact]
    public async Task На_телефон_фонът_е_скрит_по_подразбиране()
    {
        await using var visit = await WithPresetAsync("grid", width: 375, height: 667);

        Assert.Equal("none", await StyleAsync(visit.Page, "#ambient-fx", "display"));
    }

    /// <summary>The switch in the admin panel brings it back, for this page.</summary>
    [Fact]
    public async Task Включеният_за_телефон_фон_се_вижда()
    {
        await using var visit = await WithPresetAsync(
            "grid", showOnMobile: true, width: 375, height: 667);

        await Assertions.Expect(visit.Page.Locator("#ambient-fx"))
            .ToHaveAttributeAsync("data-gfx-mobile", "on");

        Assert.NotEqual("none", await StyleAsync(visit.Page, "#ambient-fx", "display"));
        Assert.NotEqual("none", await LayerPaintAsync(visit.Page, "afx-a"));
    }
}
