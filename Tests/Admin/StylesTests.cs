// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using ConferenceApp.Services.Styles;
using ConferenceApp.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Tests.Admin;

/// <summary>
/// Part 6, the Visual Styles tab: the background presets, the motion and the
/// mobile switch.
/// <para>
/// Saving a row means nothing on its own; what is checked is what comes out in
/// <c>#ambient-fx</c> on the page itself. Every test here therefore takes one
/// public page and reads its attributes.
/// </para>
/// </summary>
public class StylesTests : AdminTestBase
{
    public StylesTests(AppFixture app) : base(app, "ad-sty") { }

    /// <summary>The page experimented on. Not the home page, which other tests use.</summary>
    private const string PageKey  = "/Travel";
    private const string PagePath = "/Travel";

    private static Dictionary<string, string> StyleForm(string background) => new()
    {
        ["pageKey"]          = PageKey,
        ["background"]       = background,
        ["intensity"]        = "0.5",
        ["ink"]              = "0.08",
        ["glow"]             = "0.2",
        ["cursorAlpha"]      = "0.1",
        ["gridStep"]         = "48",
        ["paperStep"]        = "12",
        ["barHeight"]        = "3",
        ["customCss"]        = "",
        ["customCssEnabled"] = "false",
        ["motion"]           = "drift",
        ["motionSpeed"]      = "slow",
        ["showOnMobile"]     = "false"
    };

    private async Task<string> AmbientLayerAsync(string path = PagePath)
    {
        using var client = App.NewClient();
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();

        var html = await response.ReadPageAsync();
        var start = html.IndexOf("id=\"ambient-fx\"", StringComparison.Ordinal);

        return start < 0 ? string.Empty : html[start..html.IndexOf('>', start)];
    }

    private Task<PageStyleSetting?> RowAsync(string key = PageKey) =>
        App.Db.ReadAsync(db => db.PageStyleSettings.AsNoTracking()
            .FirstOrDefaultAsync(p => p.PageKey == key));

    public override async Task DisposeAsync()
    {
        // The styles are global: left behind, they are visible to every later test.
        TrackStyle(PageKey);
        TrackStyle(PageStyleSetting.GlobalKey);

        await base.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // The presets
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Панелът_изброява_всички_страници_със_стил()
    {
        using var admin = await SignedInAdminAsync();
        var html = await PanelAsync(admin);

        foreach (var page in new[] { "/Index", "/Conference", "/Travel", "/Profile", "/Privacy" })
            Assert.Contains($"\"{page}\"", html);
    }

    [Theory]
    [InlineData("grid")]
    [InlineData("paper")]
    [InlineData("hatch")]
    [InlineData("glow")]
    [InlineData("contour")]
    [InlineData("fiber")]
    [InlineData("lantern")]
    [InlineData("prism")]
    [InlineData("sonar")]
    [InlineData("caustic")]
    [InlineData("spine")]
    [InlineData("ascent")]
    public async Task Присетът_се_записва_и_се_вижда_на_страницата(string background)
    {
        using var admin = await SignedInAdminAsync();

        var reply = await PostAsync(admin, "SavePageStyle", StyleForm(background));
        Assert.True(reply.Success, reply.Message);

        var row = await RowAsync();
        Assert.Equal(background, row!.Background);

        Assert.Contains($"data-gfx-bg=\"{background}\"", await AmbientLayerAsync());
    }

    [Fact]
    public async Task Присетът_off_маха_целия_слой()
    {
        using var admin = await SignedInAdminAsync();

        Assert.True((await PostAsync(admin, "SavePageStyle", StyleForm("off"))).Success);

        Assert.Equal(string.Empty, await AmbientLayerAsync());
    }

    [Fact]
    public async Task Непознат_фон_се_отказва()
    {
        using var admin = await SignedInAdminAsync();

        Assert.True((await PostAsync(admin, "SavePageStyle", StyleForm("grid"))).Success);

        var reply = await PostAsync(admin, "SavePageStyle", StyleForm("no-such-background"));
        Assert.False(reply.Success);
        Assert.Contains("фон", reply.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("grid", (await RowAsync())!.Background);
    }

    [Fact]
    public async Task Непозната_страница_се_отказва()
    {
        using var admin = await SignedInAdminAsync();

        var form = StyleForm("grid");
        form["pageKey"] = "/NoSuchPage";

        var reply = await PostAsync(admin, "SavePageStyle", form);

        Assert.False(reply.Success);
        Assert.Contains("страница", reply.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await RowAsync("/NoSuchPage"));
    }

    [Fact]
    public async Task Панелът_и_списъкът_с_фонове_не_се_разминават()
    {
        using var admin = await SignedInAdminAsync();
        var html = await PanelAsync(admin);

        // The choices in the panel are built from AmbientBackgrounds, and that is
        // exactly what the test guards, because the list used to be copied in five
        // places.
        foreach (var bg in AmbientBackgrounds.Slugs)
            Assert.Contains($"value=\"{bg}\"", html);
    }

    // ════════════════════════════════════════════════════════════════════
    // Values out of range
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("intensity",   "5")]
    [InlineData("intensity",   "-1")]
    [InlineData("ink",         "0.9")]
    [InlineData("glow",        "3")]
    [InlineData("cursorAlpha", "1")]
    [InlineData("gridStep",    "5")]
    [InlineData("gridStep",    "500")]
    [InlineData("paperStep",   "100")]
    [InlineData("barHeight",   "40")]
    [InlineData("intensity",   "не-число")]
    public async Task Стойност_извън_диапазона_става_празна_а_не_грешка(string field, string value)
    {
        using var admin = await SignedInAdminAsync();

        var form = StyleForm("grid");
        form[field] = value;

        var reply = await PostAsync(admin, "SavePageStyle", form);
        Assert.True(reply.Success, reply.Message);

        // The server answers with what was SAVED, not with what was submitted.
        Assert.Contains($"\"{Camel(field)}\":null", reply.Raw.Replace(" ", string.Empty));
    }

    private static string Camel(string field) => field switch
    {
        "cursorAlpha" => "cursorAlpha",
        "gridStep"    => "gridStep",
        "paperStep"   => "paperStep",
        "barHeight"   => "barHeight",
        _             => field
    };

    // ════════════════════════════════════════════════════════════════════
    // Motion
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("off")]
    [InlineData("drift")]
    [InlineData("slide")]
    [InlineData("swell")]
    [InlineData("breathe")]
    [InlineData("turn")]
    public async Task Познатото_движение_се_записва_и_излиза_на_страницата(string motion)
    {
        using var admin = await SignedInAdminAsync();

        var form = StyleForm("glow");
        form["motion"] = motion;

        Assert.True((await PostAsync(admin, "SavePageStyle", form)).Success);
        Assert.Equal(motion, (await RowAsync())!.Motion);

        Assert.Contains($"data-gfx-motion=\"{motion}\"", await AmbientLayerAsync());
    }

    [Theory]
    [InlineData("slower")]
    [InlineData("slow")]
    [InlineData("fast")]
    [InlineData("faster")]
    public async Task Познатата_скорост_се_записва(string speed)
    {
        using var admin = await SignedInAdminAsync();

        var form = StyleForm("glow");
        form["motionSpeed"] = speed;

        Assert.True((await PostAsync(admin, "SavePageStyle", form)).Success);
        Assert.Equal(speed, (await RowAsync())!.MotionSpeed);

        Assert.Contains($"data-gfx-speed=\"{speed}\"", await AmbientLayerAsync());
    }

    [Theory]
    [InlineData("motion",      "spin")]
    [InlineData("motionSpeed", "instant")]
    public async Task Непозната_стойност_за_движение_значи_както_е_замислено(string field, string value)
    {
        using var admin = await SignedInAdminAsync();

        var form = StyleForm("glow");
        form[field] = value;

        Assert.True((await PostAsync(admin, "SavePageStyle", form)).Success);

        var row = await RowAsync();
        Assert.Null(field == "motion" ? row!.Motion : row!.MotionSpeed);
    }

    // ════════════════════════════════════════════════════════════════════
    // The mobile switch
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Включен_фон_на_телефон_се_обявява_на_страницата()
    {
        using var admin = await SignedInAdminAsync();

        var form = StyleForm("grid");
        form["showOnMobile"] = "true";

        Assert.True((await PostAsync(admin, "SavePageStyle", form)).Success);
        Assert.True((await RowAsync())!.ShowOnMobile);

        Assert.Contains("data-gfx-mobile=\"on\"", await AmbientLayerAsync());
    }

    [Fact]
    public async Task Превключване_на_една_страница_от_списъка()
    {
        using var admin = await SignedInAdminAsync();

        var reply = await PostAsync(admin, "TogglePageMobile", new Dictionary<string, string>
        {
            ["pageKey"] = PageKey,
            ["enabled"] = "true"
        });

        Assert.True(reply.Success, reply.Message);
        Assert.True((await RowAsync())!.ShowOnMobile);

        // A row created afresh has to carry the page's own FALLBACK background
        // rather than "grid" for everything.
        Assert.Equal("caustic", (await RowAsync())!.Background);
    }

    /// <summary>
    /// The global ban overrides the page's own setting: it is a safety valve rather
    /// than a preference.
    /// </summary>
    [Fact]
    public async Task Глобалната_забрана_бие_включената_страница()
    {
        using var admin = await SignedInAdminAsync();

        var form = StyleForm("grid");
        form["showOnMobile"] = "true";
        Assert.True((await PostAsync(admin, "SavePageStyle", form)).Success);
        Assert.Contains("data-gfx-mobile=\"on\"", await AmbientLayerAsync());

        var off = await PostAsync(admin, "SetGlobalMobile",
            new Dictionary<string, string> { ["allowed"] = "false" });
        Assert.True(off.Success, off.Message);

        Assert.DoesNotContain("data-gfx-mobile=\"on\"", await AmbientLayerAsync());

        var on = await PostAsync(admin, "SetGlobalMobile",
            new Dictionary<string, string> { ["allowed"] = "true" });
        Assert.True(on.Success, on.Message);

        Assert.Contains("data-gfx-mobile=\"on\"", await AmbientLayerAsync());
    }

    [Fact]
    public async Task Глобалният_ред_не_се_показва_като_страница()
    {
        using var admin = await SignedInAdminAsync();

        await PostAsync(admin, "SetGlobalMobile",
            new Dictionary<string, string> { ["allowed"] = "false" });

        var html = await PanelAsync(admin);
        Assert.DoesNotContain($"data-page-key=\"{PageStyleSetting.GlobalKey}\"", html);

        await PostAsync(admin, "SetGlobalMobile",
            new Dictionary<string, string> { ["allowed"] = "true" });
    }

    // ════════════════════════════════════════════════════════════════════
    // Undoing
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Нулирането_връща_резервната_стойност()
    {
        using var admin = await SignedInAdminAsync();

        Assert.True((await PostAsync(admin, "SavePageStyle", StyleForm("spine"))).Success);
        Assert.Contains("data-gfx-bg=\"spine\"", await AmbientLayerAsync());

        var reply = await PostAsync(admin, "ResetPageStyle",
            new Dictionary<string, string> { ["pageKey"] = PageKey });

        Assert.True(reply.Success, reply.Message);
        Assert.Contains("\"fallbackBackground\":\"caustic\"", reply.Raw);
        Assert.Null(await RowAsync());

        Assert.Contains("data-gfx-bg=\"caustic\"", await AmbientLayerAsync());
    }

    /// <summary>A snapshot taken BEFORE the change: the only way back.</summary>
    [Fact]
    public async Task Всяка_промяна_оставя_снимка_на_предишната()
    {
        using var admin = await SignedInAdminAsync();

        Assert.True((await PostAsync(admin, "SavePageStyle", StyleForm("grid"))).Success);
        Assert.True((await PostAsync(admin, "SavePageStyle", StyleForm("paper"))).Success);

        var revisions = await App.Db.ReadAsync(db => db.PageStyleRevisions.AsNoTracking()
            .Where(r => r.PageKey == PageKey).ToListAsync());

        Assert.NotEmpty(revisions);
        Assert.Contains(revisions, r => r.SnapshotJson.Contains("grid"));
    }

    [Fact]
    public async Task Пазят_се_последните_десет_снимки()
    {
        using var admin = await SignedInAdminAsync();

        for (var i = 0; i < 13; i++)
            Assert.True((await PostAsync(admin, "SavePageStyle",
                StyleForm(i % 2 == 0 ? "grid" : "paper"))).Success);

        var count = await App.Db.ReadAsync(db =>
            db.PageStyleRevisions.CountAsync(r => r.PageKey == PageKey));

        Assert.True(count <= 10, $"Снимките са {count}, а таванът е десет.");
    }

    // ════════════════════════════════════════════════════════════════════
    // Custom CSS
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Собственият_CSS_влиза_в_страницата_чак_когато_е_включен()
    {
        using var admin = await SignedInAdminAsync();

        var form = StyleForm("grid");
        form["customCss"]        = "#ambient-fx { opacity: 0.42; }";
        form["customCssEnabled"] = "false";

        Assert.True((await PostAsync(admin, "SavePageStyle", form)).Success);

        using (var client = App.NewClient())
        {
            var page = await (await client.GetAsync(PagePath)).ReadPageAsync();
            Assert.DoesNotContain("0.42", page);
        }

        form["customCssEnabled"] = "true";
        Assert.True((await PostAsync(admin, "SavePageStyle", form)).Success);

        using (var client = App.NewClient())
        {
            var page = await (await client.GetAsync(PagePath)).ReadPageAsync();
            Assert.Contains("0.42", page);
        }
    }

    [Fact]
    public async Task Аварийният_изход_гаси_целия_собствен_CSS()
    {
        using var admin = await SignedInAdminAsync();

        var form = StyleForm("grid");
        form["customCss"]        = "#ambient-fx { opacity: 0.37; }";
        form["customCssEnabled"] = "true";
        Assert.True((await PostAsync(admin, "SavePageStyle", form)).Success);

        var reply = await PostAsync(admin, "DisableAllCustomCss");
        Assert.True(reply.Success, reply.Message);

        var row = await RowAsync();
        Assert.False(row!.CustomCssEnabled);
        Assert.NotNull(row.CustomCss);      // switched off, not deleted

        using var client = App.NewClient();
        var page = await (await client.GetAsync(PagePath)).ReadPageAsync();
        Assert.DoesNotContain("0.37", page);
    }

    [Fact]
    public async Task Опасен_CSS_се_отхвърля()
    {
        using var admin = await SignedInAdminAsync();

        var form = StyleForm("grid");
        form["customCss"]        = "body { background: url(\"javascript:alert(1)\"); } @import url(//evil.test/x.css);";
        form["customCssEnabled"] = "true";

        var reply = await PostAsync(admin, "SavePageStyle", form);

        // Either a refusal or a cleared value, but the dangerous part must not
        // survive.
        var row = await RowAsync();
        Assert.DoesNotContain("javascript:", row?.CustomCss ?? string.Empty);
        Assert.DoesNotContain("@import", row?.CustomCss ?? string.Empty);
        Assert.Contains("report", reply.Raw);
    }
}
