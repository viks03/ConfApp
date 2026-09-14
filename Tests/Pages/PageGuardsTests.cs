// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;
using Microsoft.Playwright;

namespace ConferenceApp.Tests.Pages;

/// <summary>
/// The checks in part 7 rest on two measurements of our own: the search for a
/// raw resource key, and the measurement of the width. A check that cannot fail
/// guards nothing, so both are run here against a deliberately broken page.
/// </summary>
public class PageGuardsTests : PageTestBase
{
    public PageGuardsTests(AppFixture app) : base(app, "pg-guard") { }

    // ════════════════════════════════════════════════════════════════════
    // The raw key
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Ключ_във_видимия_текст_се_хваща()
    {
        Assert.Contains("Nav_Home", RawKeys.In("<p>Nav_Home</p>"));
        Assert.Contains("Home_HeroTitle", RawKeys.In("<h1>Home_HeroTitle</h1>"));
    }

    [Fact]
    public void Ключ_във_видим_атрибут_се_хваща()
    {
        Assert.Contains("Nav_OpenMenu", RawKeys.In("<button aria-label=\"Nav_OpenMenu\"></button>"));
        Assert.Contains("Home_PageTitle", RawKeys.In("<img alt=\"Home_PageTitle\" src=\"a.png\">"));
    }

    /// <summary>
    /// A key that is in no resx at all, mistyped in the view itself. That is
    /// precisely the case a list of known keys cannot catch.
    /// </summary>
    [Fact]
    public void Несъществуващ_ключ_с_познато_семейство_също_се_хваща()
    {
        Assert.Contains("Nav_ThisKeyDoesNotExist", RawKeys.In("<p>Nav_ThisKeyDoesNotExist</p>"));
    }

    [Fact]
    public void Скриптовете_и_стиловете_не_се_броят()
    {
        // window.ValidationMessages carries keys as field names on every page:
        // that is code, not text meant to be read.
        Assert.Empty(RawKeys.In("<script>var m = { Nav_Home: 'x' };</script>"));
        Assert.Empty(RawKeys.In("<style>.Nav_Home { color: red }</style>"));
        Assert.Empty(RawKeys.In("<!-- Nav_Home -->"));
    }

    [Fact]
    public void Обикновеният_текст_не_се_брои_за_ключ()
    {
        Assert.Empty(RawKeys.In("<p>Начало, Home, blockchain_education, 2026</p>"));
    }

    // ════════════════════════════════════════════════════════════════════
    // The width
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The same measurement as in <see cref="PageWidthTests"/>, but against a page
    /// with a deliberately oversized element: if that passes as "nothing sticks
    /// out", the whole of part 7 guards nothing.
    /// </summary>
    [Fact]
    public async Task Меренето_на_ширината_хваща_стърчащ_елемент()
    {
        await using var visit = await OpenAsync(PublicPages.Of("/Conference"), "bg", 375, 667);
        await visit.Page.AcceptCookieNoticeAsync();
        await visit.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var before = await visit.Page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth");
        Assert.True(before <= 376, $"Страницата стърчи и без намеса: {before}px.");

        await visit.Page.EvaluateAsync(@"
            () => {
                const wide = document.createElement('div');
                wide.style.width = '3000px';
                wide.style.height = '10px';
                document.body.appendChild(wide);
            }");

        var after = await visit.Page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth");

        Assert.True(after > 376,
            $"Сложих елемент от 3000px, а мерената ширина остана {after}px — мерката е сляпа.");
    }
}
