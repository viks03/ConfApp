// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using System.Text.RegularExpressions;
using ConferenceApp.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Tests.Pages;

/// <summary>
/// Part 7 over HTTP: whether every page opens, in both languages, and whether
/// what is written on it can be read.
/// <para>
/// The browser checks the rest — the console and the three widths — in
/// <see cref="PageBrowserTests"/> and <see cref="PageWidthTests"/>. This is the
/// cheap, exhaustive half.
/// </para>
/// </summary>
public class PublicPageTests : PageTestBase
{
    public PublicPageTests(AppFixture app) : base(app, "pg-http") { }

    // ════════════════════════════════════════════════════════════════════
    // Does it open at all
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(PublicPages.EachInBothLanguages), MemberType = typeof(PublicPages))]
    public async Task Страницата_се_отваря(string path, string culture)
    {
        var target = PublicPages.Of(path);
        var page = await FetchAsync(target, culture);

        var expected = target.Entry == PageEntry.Missing ? HttpStatusCode.NotFound : HttpStatusCode.OK;

        Assert.Equal(expected, page.Status);

        // A redirect to the sign-in page or anywhere else means this page is not
        // reached, which is just as much a failure as an error.
        if (target.Entry != PageEntry.Missing)
            Assert.Equal(target.Expect, page.FinalPath);

        Assert.False(string.IsNullOrWhiteSpace(page.Html), $"{target} върна празно тяло.");
    }

    [Theory]
    [MemberData(nameof(PublicPages.EachInBothLanguages), MemberType = typeof(PublicPages))]
    public async Task Страницата_има_свое_заглавие(string path, string culture)
    {
        var target = PublicPages.Of(path);
        var html = (await FetchAsync(target, culture)).Html;

        var title = Regex.Match(html, "<title>(?<t>.*?)</title>", RegexOptions.Singleline);
        Assert.True(title.Success, $"{target} няма <title>.");

        // The template is "<something> | Blockchain Education 2026", so an empty
        // left-hand side means a page that forgot to introduce itself.
        var text = WebUtility.HtmlDecode(title.Groups["t"].Value).Trim();
        var own  = text.Split('|')[0].Trim();

        Assert.True(own.Length > 0, $"{target} се представя само с името на сайта: „{text}“.");
        Assert.Empty(RawKeys.In(own));
    }

    // ════════════════════════════════════════════════════════════════════
    // Not one raw key
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(PublicPages.EachInBothLanguages), MemberType = typeof(PublicPages))]
    public async Task Няма_суров_ключ_в_текста(string path, string culture)
    {
        var target = PublicPages.Of(path);
        var html = (await FetchAsync(target, culture)).Html;

        var raw = RawKeys.In(html);

        Assert.True(raw.Count == 0,
            $"{target} на „{culture}“ показва суров ключ: {string.Join(", ", raw)}");
    }

    // ════════════════════════════════════════════════════════════════════
    // The language
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(PublicPages.EachInBothLanguages), MemberType = typeof(PublicPages))]
    public async Task Лентата_е_на_избрания_език(string path, string culture)
    {
        var target = PublicPages.Of(path);
        var html = (await FetchAsync(target, culture)).Html;
        var text = RawKeys.VisibleText(html);

        var mine  = Resx.Value("Pages.Shared._Layout", "Nav_Home", culture);
        var other = Resx.Value("Pages.Shared._Layout", "Nav_Home", culture == "bg" ? "en" : "bg");

        Assert.Contains(mine, text, StringComparison.Ordinal);
        Assert.DoesNotContain($">{other}<", html, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(PublicPages.EachInBothLanguages), MemberType = typeof(PublicPages))]
    public async Task Езикът_на_документа_съвпада_с_избрания(string path, string culture)
    {
        var target = PublicPages.Of(path);
        var html = (await FetchAsync(target, culture)).Html;

        var lang = Regex.Match(html, "<html[^>]*\\blang=\"(?<l>[^\"]+)\"");
        Assert.True(lang.Success, $"{target} няма lang на <html>.");

        Assert.Equal(culture, lang.Groups["l"].Value.Split('-')[0]);
    }

    /// <summary>
    /// The BG/EN button submits the hidden form in the top bar carrying the
    /// current page's address, so switching the language leaves the person where
    /// they were.
    /// </summary>
    [Theory]
    [MemberData(nameof(PublicPages.Each), MemberType = typeof(PublicPages))]
    public async Task Смяната_на_езика_връща_на_същата_страница(string path)
    {
        var target = PublicPages.Of(path);

        using var session = App.NewSession();

        var response = await session.Client.PostAsync("/Language/SetLanguage",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["culture"]   = "en",
                ["returnUrl"] = target.Path
            }));

        // The page itself may demand a sign-in (the profile, the payment page);
        // what is checked here is where the switch leads, not what it shows
        // afterwards.
        var landed = response.RequestMessage!.RequestUri!;

        Assert.True(
            landed.AbsolutePath == target.Expect ||
            landed.AbsolutePath.StartsWith("/Login", StringComparison.OrdinalIgnoreCase),
            $"Смяната на езика от {target} хвърли на {landed.AbsolutePath}.");
    }

    // ════════════════════════════════════════════════════════════════════
    // The links
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Every internal link on the page leads somewhere. A redirect to the sign-in
    /// page is a normal answer (the profile, the admin panel); 404 and 500 are
    /// not.
    /// <para>
    /// The quick links in the footer are drawn at random on every load (see
    /// <c>_Layout.cshtml</c>), so their coverage builds up across repeated runs.
    /// That is why they are checked as well, not only the top bar.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(PublicPages.Each), MemberType = typeof(PublicPages))]
    public async Task Вътрешните_връзки_не_са_счупени(string path)
    {
        var target = PublicPages.Of(path);
        var html = (await FetchAsync(target)).Html;

        var links = Regex.Matches(html, "href=\"(?<h>/[^\"#?]*)")
            .Select(m => WebUtility.HtmlDecode(m.Groups["h"].Value))
            .Where(h => h.Length > 1)
            .Where(h => !h.StartsWith("//", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.NotEmpty(links);

        using var session = App.NewSession();
        var broken = new List<string>();

        foreach (var link in links)
        {
            var response = await session.Client.GetAsync(link);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.InternalServerError)
                broken.Add($"{link} → {(int)response.StatusCode}");
        }

        Assert.True(broken.Count == 0,
            $"{target} сочи към счупени адреси: {string.Join(", ", broken)}");
    }

    /// <summary>
    /// The "Contact us" link on the access-denied page leads to the same contact
    /// address as the footer — the same row in the database, so that the two
    /// cannot drift apart when it is changed from the admin panel ([T-25]).
    /// </summary>
    [Fact]
    public async Task Връзката_за_помощ_на_отказания_достъп_води_до_пощата()
    {
        var expected = await App.Db.ReadAsync(db => db.FooterContents
            .AsNoTracking()
            .Select(f => f.ContactEmail)
            .FirstOrDefaultAsync());

        Assert.False(string.IsNullOrWhiteSpace(expected),
            "Във FooterContents няма адрес за връзка — страницата няма какво да покаже.");

        var html = (await FetchAsync(PublicPages.Of("/AccessDenied"))).Html;

        var help = Resx.Value("Pages.AccessDenied", "denied_help_link");
        var link = Regex.Match(html,
            $"<a[^>]*href=\"(?<h>[^\"]+)\"[^>]*>\\s*{Regex.Escape(WebUtility.HtmlEncode(help))}\\s*</a>");

        Assert.True(link.Success, $"На /AccessDenied няма връзка „{help}“.");
        Assert.Equal($"mailto:{expected}", WebUtility.HtmlDecode(link.Groups["h"].Value));
    }

    [Fact]
    public async Task Празен_език_не_чупи_превключвателя()
    {
        using var session = App.NewSession();

        var response = await session.Client.PostAsync("/Language/SetLanguage",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["culture"]   = "",
                ["returnUrl"] = "/Conference"
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/Conference", response.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Чужд_адрес_в_превключвателя_не_извежда_навън()
    {
        using var session = App.NewSession();

        var response = await session.Client.PostAsync("/Language/SetLanguage",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["culture"]   = "en",
                ["returnUrl"] = "https://example.com/phish"
            }));

        Assert.Equal(App.BaseUrl.TrimEnd('/'),
            response.RequestMessage!.RequestUri!.GetLeftPart(UriPartial.Authority));
        Assert.Equal("/", response.RequestMessage.RequestUri.AbsolutePath);
    }
}
