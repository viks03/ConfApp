// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;
using Microsoft.Playwright;

namespace ConferenceApp.Tests.Pages;

/// <summary>
/// Part 7, the three widths: 1440 for a desktop, 768 for a tablet and 375 for a
/// phone.
/// <para>
/// Each width gets a context of its own, opened at that width rather than
/// resized after loading: media queries are evaluated at the first paint, and a
/// page that looks right only after a resize is broken for the person arriving
/// straight from their phone.
/// </para>
/// </summary>
public class PageWidthTests : PageTestBase
{
    public PageWidthTests(AppFixture app) : base(app, "pg-w") { }

    /// <summary>The height each width is opened at.</summary>
    private static int HeightFor(int width) => width switch
    {
        1440 => 900,
        768  => 1024,
        _    => 667
    };

    /// <summary>
    /// Horizontal scrolling on a page means something sticks out past the screen:
    /// the commonest fault at a narrow width, and the only one visible without a
    /// human eye.
    /// </summary>
    [Theory]
    [MemberData(nameof(PublicPages.EachAtEveryWidth), MemberType = typeof(PublicPages))]
    public async Task Страницата_не_стърчи_настрани(string path, int width)
    {
        var target = PublicPages.Of(path);

        await using var visit = await OpenAsync(target, "bg", width, HeightFor(width));
        await visit.Page.AcceptCookieNoticeAsync();

        // Lazy images and fonts go on shifting things after the first frame, so the
        // measurement is taken once the network has gone quiet.
        await visit.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var overflowing = await visit.Page.EvaluateAsync<string[]>(@"
            (limit) => {
                const out = [];
                for (const el of document.querySelectorAll('body *')) {
                    const style = getComputedStyle(el);
                    if (style.display === 'none' || style.visibility === 'hidden') continue;
                    // Скритите извън екрана слоеве (мобилното меню, банери)
                    // нарочно стоят вдясно, докато не ги отвориш.
                    if (style.position === 'fixed' && parseFloat(style.opacity) === 0) continue;
                    const box = el.getBoundingClientRect();
                    if (box.width === 0 || box.height === 0) continue;
                    if (box.right > limit + 1) {
                        const id = el.id ? '#' + el.id : '';
                        const cls = (el.className && typeof el.className === 'string')
                            ? '.' + el.className.trim().split(/\s+/).join('.') : '';
                        out.push(el.tagName.toLowerCase() + id + cls +
                                 ' → ' + Math.round(box.right) + 'px');
                    }
                }
                return out.slice(0, 8);
            }", width);

        var scrollWidth = await visit.Page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth");

        Assert.True(scrollWidth <= width + 1,
            $"{target} на {width}px се скролва настрани ({scrollWidth}px). " +
            $"Стърчат: {(overflowing.Length == 0 ? "—" : string.Join("; ", overflowing))}");
    }

    /// <summary>
    /// The navigation has to be reachable at every width: the bar itself on a
    /// desktop, and the hamburger in its place below 1240px.
    /// </summary>
    [Theory]
    [MemberData(nameof(PublicPages.EachAtEveryWidth), MemberType = typeof(PublicPages))]
    public async Task Навигацията_е_достижима(string path, int width)
    {
        var target = PublicPages.Of(path);

        await using var visit = await OpenAsync(target, "bg", width, HeightFor(width));
        await visit.Page.AcceptCookieNoticeAsync();

        var burger = visit.Page.Locator(".menu-toggle");
        var nav    = visit.Page.Locator("nav.nav");

        if (width > 1240)
        {
            await Assertions.Expect(nav).ToBeVisibleAsync();
            await Assertions.Expect(burger).ToBeHiddenAsync();
        }
        else
        {
            await Assertions.Expect(burger).ToBeVisibleAsync();
        }

        // The register/profile button is in the top bar at every width; it is how a
        // person reaches the site's actual purpose.
        await Assertions.Expect(visit.Page.Locator(".topbar-actions").First).ToBeVisibleAsync();
    }

    /// <summary>
    /// On a phone the menu opens and closes, and the links inside it really do
    /// lead somewhere.
    /// </summary>
    [Theory]
    [MemberData(nameof(PublicPages.Each), MemberType = typeof(PublicPages))]
    public async Task Мобилното_меню_се_отваря_и_затваря(string path)
    {
        await using var visit = await OpenAsync(PublicPages.Of(path), "bg", 375, 667);
        await visit.Page.AcceptCookieNoticeAsync();

        await visit.Page.ClickAsync(".menu-toggle");

        var overlay = visit.Page.Locator("#mobile-nav-overlay");
        await Assertions.Expect(overlay).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"\bis-open\b"));

        var links = overlay.Locator("a[href]");
        Assert.True(await links.CountAsync() > 0, "Мобилното меню се отвори празно.");

        await visit.Page.ClickAsync(".menu-toggle");
        await Assertions.Expect(overlay).Not.ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"\bis-open\b"));

        Assert.Empty(visit.Errors);
    }
}
