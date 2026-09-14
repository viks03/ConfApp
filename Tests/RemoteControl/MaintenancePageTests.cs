// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Services.RemoteControl;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;

namespace ConferenceApp.Tests.RemoteControl;

/// <summary>
/// The page that is shown while the site is off. It has to work when nothing
/// else does, so what is checked here is mostly what it does NOT contain.
/// </summary>
public class MaintenancePageTests
{
    private static RemoteControlDecision Hidden(int statusCode = 503) =>
        new(true, statusCode, "Извършваме кратка поддръжка.", "Brief maintenance in progress.");

    [Fact]
    public void Страницата_не_зависи_от_нищо_външно()
    {
        var html = MaintenancePage.Render(Hidden(), english: false);

        // No script, no external style, no link to anywhere: every one of those
        // is a request that may fail for the same reason the site is off.
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link",   html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<a ",     html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img",    html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://",  html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);

        // The styles are in the document.
        Assert.Contains("<style>", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Съобщението_е_на_езика_на_посетителя()
    {
        var bulgarian = MaintenancePage.Render(Hidden(), english: false);
        var english   = MaintenancePage.Render(Hidden(), english: true);

        Assert.Contains("Извършваме кратка поддръжка.", bulgarian);
        Assert.Contains("lang=\"bg\"", bulgarian);

        Assert.Contains("Brief maintenance in progress.", english);
        Assert.Contains("lang=\"en\"", english);
    }

    [Fact]
    public void Без_съобщение_от_сървъра_страницата_пак_казва_нещо()
    {
        var silent = new RemoteControlDecision(true, 503, string.Empty, string.Empty);

        Assert.Contains("поддръжка", MaintenancePage.Render(silent, english: false));
        Assert.Contains("maintenance", MaintenancePage.Render(silent, english: true));
    }

    [Fact]
    public void Текстът_от_сървъра_не_може_да_вкара_HTML()
    {
        // The message comes from another machine and lands inside a document.
        var nasty = new RemoteControlDecision(
            true, 503, "<script>alert(1)</script>", "<script>alert(1)</script>");

        var html = MaintenancePage.Render(nasty, english: false);

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Двата_оформени_режима_имат_различно_заглавие()
    {
        var maintenance = MaintenancePage.Render(Hidden(503), english: false);
        var failure     = MaintenancePage.Render(Hidden(500), english: false);

        Assert.Contains("Кратка поддръжка", maintenance);
        Assert.Contains("Временно недостъпно", failure);
    }

    // ── notfound: a different page, not a version of the one above ────

    [Fact]
    public void Режим_notfound_дава_гола_страница()
    {
        var html = MaintenancePage.Render(Hidden(404), english: false);

        Assert.Equal(
            "<html><head><title>404 Not Found</title></head>\n" +
            "<body><h1>Not Found</h1><p>The requested URL was not found on this server.</p></body></html>",
            html);
    }

    [Fact]
    public void Страницата_при_notfound_не_издава_нищо_наше()
    {
        var html = MaintenancePage.Render(Hidden(404), english: false);

        // Nothing that says an application is answering: no styles of ours, no
        // robots, no charset, no viewport, no language, no doctype.
        Assert.DoesNotContain("<style",  html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("robots",  html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("charset", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("viewport", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lang=",   html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DOCTYPE", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Съобщенията_не_се_показват_при_notfound()
    {
        // The control server goes on sending both texts; under this mode the
        // site does not explain why it is gone, so neither is written out.
        var told = new RemoteControlDecision(
            true, 404, "Спряхме сайта до понеделник.", "We took the site down until Monday.");

        var html = MaintenancePage.Render(told, english: true);

        Assert.DoesNotContain("Спряхме", html);
        Assert.DoesNotContain("Monday", html);
        Assert.Equal(MaintenancePage.Render(Hidden(404), english: false), html);
    }

    [Fact]
    public void Езикът_на_посетителя_не_мени_404()
    {
        // A 404 is not translated — and a page that greeted you in your own
        // language would be admitting that something here read the request.
        Assert.Equal(
            MaintenancePage.Render(Hidden(404), english: false),
            MaintenancePage.Render(Hidden(404), english: true));
    }

    // ── Which language, decided without the localisation middleware ───

    [Fact]
    public void Без_никакъв_признак_езикът_е_български()
    {
        Assert.False(MaintenancePage.PrefersEnglish(Request()));
    }

    [Fact]
    public void Бисквитката_на_сайта_решава()
    {
        var cookie = CookieRequestCultureProvider.MakeCookieValue(new RequestCulture("en"));
        Assert.True(MaintenancePage.PrefersEnglish(Request(cookie: cookie)));

        cookie = CookieRequestCultureProvider.MakeCookieValue(new RequestCulture("bg"));
        Assert.False(MaintenancePage.PrefersEnglish(Request(cookie: cookie)));
    }

    [Theory]
    [InlineData("en-GB,en;q=0.9", true)]
    [InlineData("bg-BG,bg;q=0.9,en;q=0.8", false)]
    [InlineData("en-US,en;q=0.9,bg;q=0.8", true)]
    // A language the site does not speak is not a reason to switch off Bulgarian.
    [InlineData("de-DE,de;q=0.9", false)]
    [InlineData("", false)]
    // The order in the header is what the browser meant, so a tie keeps the first.
    [InlineData("bg,en", false)]
    [InlineData("en,bg", true)]
    // Quality beats order.
    [InlineData("bg;q=0.2,en;q=0.9", true)]
    public void Accept_Language_решава_когато_няма_бисквитка(string header, bool english)
    {
        Assert.Equal(english, MaintenancePage.PrefersEnglish(Request(acceptLanguage: header)));
    }

    [Fact]
    public void Бисквитката_тежи_повече_от_заглавния_ред()
    {
        // Somebody who chose Bulgarian on the site gets Bulgarian, whatever the
        // browser they are sitting at asks for.
        var cookie = CookieRequestCultureProvider.MakeCookieValue(new RequestCulture("bg"));

        Assert.False(MaintenancePage.PrefersEnglish(
            Request(cookie: cookie, acceptLanguage: "en-US,en;q=0.9")));
    }

    private static HttpRequest Request(string? cookie = null, string? acceptLanguage = null)
    {
        var context = new DefaultHttpContext();

        if (cookie is not null)
        {
            context.Request.Headers.Cookie =
                $"{CookieRequestCultureProvider.DefaultCookieName}={Uri.EscapeDataString(cookie)}";
        }

        if (acceptLanguage is not null)
            context.Request.Headers.AcceptLanguage = acceptLanguage;

        return context.Request;
    }
}
