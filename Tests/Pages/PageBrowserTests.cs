// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;
using Microsoft.Playwright;

namespace ConferenceApp.Tests.Pages;

/// <summary>
/// Part 7 through a browser: whether every page opens in both languages, and
/// whether the console stays quiet.
/// <para>
/// "Not one console error" catches all three things the HTTP test cannot see: a
/// broken script, a missing file — an image, a stylesheet, a font, all of which
/// Chromium reports as errors — and a background request that failed.
/// </para>
/// </summary>
public class PageBrowserTests : PageTestBase
{
    public PageBrowserTests(AppFixture app) : base(app, "pg-ui") { }

    [Theory]
    [MemberData(nameof(PublicPages.EachInBothLanguages), MemberType = typeof(PublicPages))]
    public async Task Страницата_се_отваря_без_грешка_в_конзолата(string path, string culture)
    {
        var target = PublicPages.Of(path);

        await using var visit = await OpenAsync(target, culture);

        Assert.Equal(target.Entry == PageEntry.Missing ? 404 : 200, visit.Status);

        // The body has to be rendered, not merely received.
        await Assertions.Expect(visit.Page.Locator("body")).ToBeVisibleAsync();

        Assert.True(visit.Errors.Count == 0,
            $"{target} на „{culture}“ каза в конзолата:\n  " +
            string.Join("\n  ", visit.Errors));
    }

    /// <summary>
    /// The top bar and the footer are on every page, including the 404, where a
    /// person needs a way out most of all.
    /// </summary>
    [Theory]
    [MemberData(nameof(PublicPages.Each), MemberType = typeof(PublicPages))]
    public async Task Страницата_носи_лентата_и_футъра(string path)
    {
        await using var visit = await OpenAsync(PublicPages.Of(path));

        await Assertions.Expect(visit.Page.Locator("header.topbar")).ToBeVisibleAsync();
        await Assertions.Expect(visit.Page.Locator("footer").First).ToBeAttachedAsync();
    }

    /// <summary>
    /// The language is switched from the top bar itself: the button submits the
    /// hidden form and comes back to the same page, translated.
    /// </summary>
    [Theory]
    [MemberData(nameof(PublicPages.Each), MemberType = typeof(PublicPages))]
    public async Task Бутонът_EN_превежда_страницата_на_място(string path)
    {
        var target = PublicPages.Of(path);

        // The "Done" page is shown once and is gone after a language switch: it is
        // not a page anyone stands on and toggles.
        if (target.Entry == PageEntry.JustRegistered) return;

        await using var visit = await OpenAsync(target, "bg");

        var home = Resx.Value("Pages.Shared._Layout", "Nav_Home", "en");

        // On a 404 the address bar comes back to /Error rather than to the address
        // that does not exist: the hidden field carries Context.Request.Path, and
        // under UseStatusCodePagesWithReExecute that has already been rewritten to
        // /Error. See [T-24]: behaviour rather than a fault, since the person
        // stays on the same page.
        var landing = target.Entry == PageEntry.Missing ? "/Error" : target.Expect;

        await visit.Page.AcceptCookieNoticeAsync();
        await visit.Page.ClickAsync(".lang-switch[value='en']");
        await visit.Page.WaitForURLAsync($"**{landing}");

        await Assertions.Expect(visit.Page.Locator($"nav.nav a:text-is(\"{home}\")"))
            .ToBeAttachedAsync();
    }
}
