// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using ConferenceApp.Tests.Fixtures;
using ConferenceApp.Tests.Pages;
using Microsoft.Playwright;

namespace ConferenceApp.Tests.Theming;

/// <summary>
/// Part 9, the mobile menu.
/// <para>
/// Part 7 checked that the hamburger opens and closes something on each of the
/// twenty pages. What is checked here is the menu itself: whether it behaves like
/// a menu (Escape, a locked scroll, the state of the hamburger), whether its
/// links lead anywhere, and what happens at the two different sizes — because
/// there are TWO menus.
/// </para>
/// <para>
/// Below 641px the full-screen overlay (<c>#mobile-nav-overlay</c>) is at work,
/// while between 641 and 1240px the same hamburger shows a dropdown of the SAME
/// links (<c>nav.nav</c>) and the overlay is explicitly disabled. A test at only
/// one width sees half the menu.
/// </para>
/// </summary>
public class MobileNavTests : ThemingTestBase
{
    public MobileNavTests(AppFixture app) : base(app, "th-nav") { }

    private const int PhoneWidth  = 375;
    private const int PhoneHeight = 667;

    /// <summary>The destinations the bar promises: the same eight in both menus.</summary>
    private static readonly string[] NavDestinations =
    {
        "/Index", "/Conference", "/ICBI", "/Lecturers", "/Schedule", "/Attend", "/FAQ", "/Travel"
    };

    private async Task<PageVisit> OnPhoneAsync(string path = "/")
    {
        var visit = await OpenAsync(PublicPages.Of(path), "bg", PhoneWidth, PhoneHeight);
        await visit.Page.AcceptCookieNoticeAsync();
        return visit;
    }

    private static ILocator Overlay(IPage page) => page.Locator("#mobile-nav-overlay");

    private static async Task<bool> IsOpenAsync(IPage page) =>
        await page.EvaluateAsync<bool>(
            "() => document.getElementById('mobile-nav-overlay').classList.contains('is-open')");

    // ════════════════════════════════════════════════════════════════════
    // Opening and closing
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The hamburger opens the menu and turns into an X, and a screen reader learns
    /// of it from <c>aria-expanded</c> and from the changed label.
    /// </summary>
    [Fact]
    public async Task Бургерът_отваря_и_затваря_менюто()
    {
        await using var visit = await OnPhoneAsync();
        var page = visit.Page;

        var burger = page.Locator(".menu-toggle");
        await Assertions.Expect(burger).ToHaveAttributeAsync("aria-expanded", "false");

        var opened = await burger.GetAttributeAsync("data-label-open");
        var closed = await burger.GetAttributeAsync("data-label-close");
        Assert.False(string.IsNullOrWhiteSpace(opened));
        Assert.NotEqual(opened, closed);

        await burger.ClickAsync();

        await Assertions.Expect(Overlay(page)).ToBeVisibleAsync();
        await Assertions.Expect(burger).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(burger).ToHaveAttributeAsync("aria-label", closed!);
        await Assertions.Expect(Overlay(page)).ToHaveAttributeAsync("aria-hidden", "false");

        Assert.Contains("is-open", await burger.GetAttributeAsync("class") ?? "");

        await burger.ClickAsync();

        await Assertions.Expect(Overlay(page)).ToBeHiddenAsync();
        await Assertions.Expect(burger).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(burger).ToHaveAttributeAsync("aria-label", opened!);

        Assert.Empty(visit.Errors);
    }

    /// <summary>Escape closes the menu, as it does any other overlay.</summary>
    [Fact]
    public async Task Escape_затваря_менюто()
    {
        await using var visit = await OnPhoneAsync();
        var page = visit.Page;

        await page.ClickAsync(".menu-toggle");
        Assert.True(await IsOpenAsync(page));

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Overlay(page)).ToBeHiddenAsync();
    }

    /// <summary>
    /// While the menu is open the page beneath does not scroll; otherwise a finger
    /// moves both things at once.
    /// </summary>
    [Fact]
    public async Task Отвореното_меню_заключва_скрола()
    {
        await using var visit = await OnPhoneAsync();
        var page = visit.Page;

        var overflowBefore = await StyleAsync(page, "body", "overflow");

        await page.ClickAsync(".menu-toggle");
        Assert.Equal("hidden", await StyleAsync(page, "body", "overflow"));

        await page.ClickAsync(".menu-toggle");
        await Assertions.Expect(Overlay(page)).ToBeHiddenAsync();
        Assert.Equal(overflowBefore, await StyleAsync(page, "body", "overflow"));
    }

    /// <summary>
    /// The menu slides rather than popping: both opening and closing have a
    /// transition, and the top bar moves in step with it.
    /// </summary>
    [Fact]
    public async Task Менюто_се_плъзга_а_не_изскача()
    {
        await using var visit = await OnPhoneAsync();
        var page = visit.Page;

        static double Seconds(string value) =>
            double.Parse(value.Split(',')[0].Trim().TrimEnd('s'),
                System.Globalization.CultureInfo.InvariantCulture);

        var closed = await StyleAsync(page, "#mobile-nav-overlay", "transition-duration");
        Assert.True(Seconds(closed) > 0,
            $"Менюто се затваря мигновено: transition-duration е {closed}.");

        await page.ClickAsync(".menu-toggle");
        await Assertions.Expect(Overlay(page)).ToBeVisibleAsync();

        var open = await StyleAsync(page, "#mobile-nav-overlay", "transition-duration");
        Assert.True(Seconds(open) > 0,
            $"Менюто се отваря мигновено: transition-duration е {open}.");

        // The top bar stays visible ABOVE the panel: the menu holds no second logo
        // and no second sign-in button, the real ones are moved.
        await Assertions.Expect(page.Locator(".topbar")).ToBeVisibleAsync();

        // The shift is itself a transition, so it is measured once it has finished
        // rather than on the first frame after the click.
        await Assertions.Expect(page.Locator(".topbar")).Not.ToHaveCSSAsync("margin-left", "0px");
    }

    // ════════════════════════════════════════════════════════════════════
    // The links
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The menu carries all eight destinations from the bar and each of them
    /// opens. The external ones — the social networks — are listed but not
    /// fetched: the test does not leave the machine.
    /// </summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/Schedule")]
    public async Task Всички_връзки_в_менюто_водят_нанякъде(string from)
    {
        await using var visit = await OnPhoneAsync(from);
        var page = visit.Page;

        await page.ClickAsync(".menu-toggle");
        await Assertions.Expect(Overlay(page)).ToBeVisibleAsync();

        var hrefs = await page.EvaluateAsync<string[]>(@"
            () => Array.from(
                document.querySelectorAll('#mobile-nav-overlay a[href]'),
                a => a.getAttribute('href'))");

        foreach (var destination in NavDestinations)
            Assert.Contains(destination, hrefs);

        using var session = App.NewSession();

        foreach (var href in hrefs.Distinct())
        {
            // An external address, a mailto or an anchor within the same page is not
            // a page of ours and is not checked with a network request.
            if (!href.StartsWith('/') || href.StartsWith("//")) continue;

            using var response = await session.Client.GetAsync(href);

            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"Връзката „{href}“ от мобилното меню на {from} върна {(int)response.StatusCode}.");
        }
    }

    /// <summary>
    /// A clicked link takes the person away and leaves the menu closed on the new
    /// page.
    /// </summary>
    [Fact]
    public async Task Връзка_от_менюто_отвежда_и_затваря_менюто()
    {
        await using var visit = await OnPhoneAsync();
        var page = visit.Page;

        await page.ClickAsync(".menu-toggle");
        await Assertions.Expect(Overlay(page)).ToBeVisibleAsync();

        await page.RunAndWaitForResponseAsync(
            async () => await page.ClickAsync("#mobile-nav-overlay a[href='/Schedule']"),
            response => response.Request.IsNavigationRequest && response.Request.Method == "GET");

        await page.WaitForURLAsync("**/Schedule");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        Assert.False(await IsOpenAsync(page), "Менюто остана отворено върху новата страница.");
        await Assertions.Expect(Overlay(page)).ToBeHiddenAsync();
    }

    /// <summary>
    /// The language can be switched with the menu open: the BG/EN buttons are on
    /// the real top bar, which sits above the panel for exactly this reason.
    /// </summary>
    [Fact]
    public async Task Езикът_се_сменя_и_при_отворено_меню()
    {
        await using var visit = await OnPhoneAsync();
        var page = visit.Page;

        await page.ClickAsync(".menu-toggle");
        await Assertions.Expect(Overlay(page)).ToBeVisibleAsync();

        await page.RunAndWaitForResponseAsync(
            async () => await page.ClickAsync(".lang-switch[value='en']"),
            response => response.Request.IsNavigationRequest && response.Request.Method == "GET");

        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        Assert.Equal("en", await page.EvaluateAsync<string>("() => document.documentElement.lang"));
        Assert.False(await IsOpenAsync(page));
    }

    // ════════════════════════════════════════════════════════════════════
    // The middle size
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Between 641 and 1240px the hamburger shows the dropdown and the full-screen
    /// overlay is explicitly disabled: it is designed for a narrow portrait screen
    /// and looks stretched at this width.
    /// </summary>
    [Fact]
    public async Task На_таблет_бургерът_показва_падащия_списък()
    {
        await using var visit = await OpenAsync(PublicPages.Of("/"), "bg", 768, 1024);
        var page = visit.Page;

        await page.AcceptCookieNoticeAsync();

        Assert.Equal("none", await StyleAsync(page, "#mobile-nav-overlay", "display"));
        await Assertions.Expect(page.Locator("nav.nav")).ToBeHiddenAsync();

        await page.ClickAsync(".menu-toggle");

        await Assertions.Expect(page.Locator("nav.nav")).ToBeVisibleAsync();

        // The full-screen one stays hidden even when the script puts the class on
        // it.
        Assert.Equal("none", await StyleAsync(page, "#mobile-nav-overlay", "display"));

        var links = await page.Locator("nav.nav a").AllInnerTextsAsync();
        Assert.Equal(NavDestinations.Length, links.Count);

        await page.ClickAsync(".menu-toggle");
        await Assertions.Expect(page.Locator("nav.nav")).ToBeHiddenAsync();

        Assert.Empty(visit.Errors);
    }

    /// <summary>
    /// Widening the window closes the menu; otherwise the overlay hangs open behind
    /// the bar that is now shown.
    /// </summary>
    [Fact]
    public async Task Разширеният_прозорец_затваря_менюто()
    {
        await using var visit = await OnPhoneAsync();
        var page = visit.Page;

        await page.ClickAsync(".menu-toggle");
        Assert.True(await IsOpenAsync(page));

        await page.SetViewportSizeAsync(1440, 900);

        await Assertions.Expect(page.Locator("nav.nav")).ToBeVisibleAsync();

        // The closing happens in the resize listener, so the assertion waits for
        // that rather than for the first frame after the resize.
        await Assertions.Expect(Overlay(page)).Not.ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"\bis-open\b"));

        Assert.False(await IsOpenAsync(page), "Менюто остана отворено зад десктоп навигацията.");
    }
}
