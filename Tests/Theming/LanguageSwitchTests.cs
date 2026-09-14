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
/// Part 9, switching the language from the top bar.
/// <para>
/// Part 7 already checked that every page EXISTS in both languages. The question
/// here is a different one: does the switch itself work — from the page a person
/// is standing on, with a mouse, on each of the twenty pages.
/// </para>
/// <para>
/// "The transition is not abrupt" is measured by three things, because only
/// those are measurable: the person stays on the page they were on (one
/// redirect, not a jump to the home page); the page arrives already in the new
/// language (no intermediate frame in the old one); and the buttons themselves
/// change over a transition rather than instantly.
/// </para>
/// </summary>
public class LanguageSwitchTests : ThemingTestBase
{
    public LanguageSwitchTests(AppFixture app) : base(app, "th-lang") { }

    private const string LayoutResx = "Pages.Shared._Layout";

    private static string Switch(string culture) => $".lang-switch[value='{culture}']";

    /// <summary>Clicks the button and waits for the new page to load.</summary>
    private static async Task ClickSwitchAsync(IPage page, string culture)
    {
        await page.RunAndWaitForResponseAsync(
            async () => await page.ClickAsync(Switch(culture)),
            // The first response is to the POST itself, a redirect; the document that
            // gets rendered arrives with the GET that follows.
            response => response.Request.IsNavigationRequest && response.Request.Method == "GET");

        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    private static Task<string> LangAsync(IPage page) =>
        page.EvaluateAsync<string>("() => document.documentElement.lang");

    /// <summary>
    /// The text of the first link in the top bar, as it stands in the document.
    /// <para>
    /// <c>textContent</c> deliberately, not <c>innerText</c>: the bar carries
    /// <c>text-transform: uppercase</c> and the latter returns the upper-cased
    /// form, so a test on it would be comparing the CSS's capitals rather than the
    /// language.
    /// </para>
    /// </summary>
    private static async Task<string> FirstNavLabelAsync(IPage page) =>
        (await page.Locator("nav.nav a").First.TextContentAsync() ?? "").Trim();

    // ════════════════════════════════════════════════════════════════════
    // From the top bar, on every page
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The BG/EN button is on every page and really does change the language of
    /// that page.
    /// </summary>
    [Theory]
    [MemberData(nameof(PublicPages.Each), MemberType = typeof(PublicPages))]
    public async Task Езикът_се_сменя_от_лентата(string path)
    {
        var target = PublicPages.Of(path);

        await using var visit = await OpenAsync(target, "bg");
        var page = visit.Page;

        await page.AcceptCookieNoticeAsync();

        Assert.Equal("bg", await LangAsync(page));
        Assert.Equal(Resx.Value(LayoutResx, "Nav_Home"), await FirstNavLabelAsync(page));

        await ClickSwitchAsync(page, "en");

        Assert.Equal("en", await LangAsync(page));
        Assert.Equal(Resx.Value(LayoutResx, "Nav_Home", "en"), await FirstNavLabelAsync(page));

        // And back again: the switch is not one-way.
        await ClickSwitchAsync(page, "bg");
        Assert.Equal("bg", await LangAsync(page));
    }

    /// <summary>
    /// The switch brings the person back to where they were. The only exception is
    /// a page that by design is shown once — the Done page — which cannot be
    /// reloaded even by an ordinary refresh.
    /// </summary>
    [Theory]
    [MemberData(nameof(PublicPages.Each), MemberType = typeof(PublicPages))]
    public async Task Смяната_оставя_човека_на_същата_страница(string path)
    {
        var target = PublicPages.Of(path);
        if (target.Entry == PageEntry.JustRegistered) return;   // shown once by design

        await using var visit = await OpenAsync(target, "bg");
        var page = visit.Page;

        await page.AcceptCookieNoticeAsync();
        await ClickSwitchAsync(page, "en");

        var landed = new Uri(page.Url).AbsolutePath;

        // The 404 page is rendered by re-executing /Error
        // (UseStatusCodePagesWithReExecute), so the hidden field in the top bar
        // carries "/Error" and that is where the person stays. The same page, a
        // different address.
        var expected = target.Entry == PageEntry.Missing ? "/Error" : target.Expect;
        Assert.Equal(expected, landed);

        // The 404 page's console deliberately carries its own response.
        if (target.Entry != PageEntry.Missing)
            Assert.True(visit.Errors.Count == 0,
                $"{target} след смяна на езика изкара: {string.Join(" | ", visit.Errors)}");
    }

    /// <summary>The choice applies to the next page too, not only the current one.</summary>
    [Fact]
    public async Task Езикът_се_помни_на_следващата_страница()
    {
        await using var visit = await OpenAsync(PublicPages.Of("/"), "bg");
        var page = visit.Page;

        await page.AcceptCookieNoticeAsync();
        await ClickSwitchAsync(page, "en");

        await page.GotoAsync("/Conference");
        Assert.Equal("en", await LangAsync(page));

        await page.GotoAsync("/FAQ");
        Assert.Equal("en", await LangAsync(page));
    }

    /// <summary>
    /// The choice outlives a closed browser: the cookie lasts a year rather than
    /// the session. There used to be an inline script here that wrote a session
    /// cookie, and the choice was lost the moment the browser closed.
    /// </summary>
    [Fact]
    public async Task Изборът_не_се_губи_при_затваряне_на_браузъра()
    {
        await using var visit = await OpenAsync(PublicPages.Of("/"), "bg");

        await visit.Page.AcceptCookieNoticeAsync();
        await ClickSwitchAsync(visit.Page, "en");

        var cookie = (await visit.Context.CookiesAsync())
            .FirstOrDefault(c => c.Name == CultureCookieName);

        Assert.NotNull(cookie);

        // The value goes into the header percent-encoded.
        Assert.Contains("uic=en", Uri.UnescapeDataString(cookie!.Value));

        var expires = DateTimeOffset.FromUnixTimeSeconds((long)cookie.Expires);
        Assert.True(expires > DateTimeOffset.UtcNow.AddDays(300),
            $"Бисквитката за език изтича на {expires:u} — това не е „запомнено“.");
    }

    // ════════════════════════════════════════════════════════════════════
    // The redirect itself
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One redirect back to the same address, query string included. A page with a
    /// filter in its address must not be reset because someone changed the
    /// language.
    /// </summary>
    [Fact]
    public async Task Пренасочването_пази_адреса_със_заявката()
    {
        using var session = App.NewSession();
        using var noRedirect = session.NoRedirectClient();

        var response = await noRedirect.PostAsync("/Language/SetLanguage",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["culture"]   = "en",
                ["returnUrl"] = "/Lecturers?highlight=3"
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Lecturers?highlight=3", response.Headers.Location?.ToString());
    }

    /// <summary>
    /// A foreign address in <c>returnUrl</c> does not lead off the site: the switch
    /// is a POST carrying an address from the request, and that is exactly the kind
    /// of form that is easy to forge.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/evil")]
    [InlineData("//example.com/evil")]
    [InlineData("")]
    public async Task Чужд_или_празен_адрес_връща_на_началната(string returnUrl)
    {
        using var session = App.NewSession();
        using var noRedirect = session.NoRedirectClient();

        var response = await noRedirect.PostAsync("/Language/SetLanguage",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["culture"]   = "en",
                ["returnUrl"] = returnUrl
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.ToString());
    }

    /// <summary>
    /// A missing language does not break the switch: it falls back to Bulgarian
    /// rather than to an error.
    /// </summary>
    [Fact]
    public async Task Без_подаден_език_страницата_е_на_български()
    {
        using var session = App.NewSession();
        await session.SetLanguageAsync("en");

        var response = await session.Client.PostAsync("/Language/SetLanguage",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["returnUrl"] = "/" }));

        response.EnsureSuccessStatusCode();

        var html = await (await session.Client.GetAsync("/")).ReadPageAsync();
        Assert.Contains(Resx.Value(LayoutResx, "Nav_Home"), html);
    }

    // ════════════════════════════════════════════════════════════════════
    // The transition
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The page arrives ALREADY in the new language: the translation is done on the
    /// server rather than patched in by a script after loading. Otherwise a person
    /// sees a frame of the old language, which is precisely what "abrupt" means.
    /// </summary>
    [Fact]
    public async Task Страницата_идва_готова_на_новия_език()
    {
        using var session = App.NewSession();
        using var noRedirect = session.NoRedirectClient();

        var redirect = await noRedirect.PostAsync("/Language/SetLanguage",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["culture"]   = "en",
                ["returnUrl"] = "/Conference"
            }));

        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);

        // One redirect, not a chain: the next response is already the page.
        var page = await session.Client.GetAsync(redirect.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        var html = await page.ReadPageAsync();
        Assert.Contains(Resx.Value(LayoutResx, "Nav_Home", "en"), html);
        Assert.Contains("<html lang=\"en\"", html);
    }

    /// <summary>
    /// The buttons in the bar change over a transition rather than jumping. The
    /// exact duration is not copied in here; all that is checked is that a
    /// transition exists at all.
    /// </summary>
    [Fact]
    public async Task Бутоните_за_език_не_се_променят_рязко()
    {
        await using var visit = await OpenAsync(PublicPages.Of("/"));

        var duration = await StyleAsync(visit.Page, ".lang-switch", "transition-duration");
        var property = await StyleAsync(visit.Page, ".lang-switch", "transition-property");

        Assert.Contains("color", property);

        // A list of durations; it is enough that the first is not zero.
        var first = duration.Split(',')[0].Trim().TrimEnd('s');
        Assert.True(double.Parse(first, System.Globalization.CultureInfo.InvariantCulture) > 0,
            $"Бутоните за език се менят мигновено: transition-duration е {duration}.");
    }
}
