// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Localization;

namespace ConferenceApp.Services.RemoteControl
{
    /// <summary>
    /// The page the visitor sees while the site is switched off. It is built
    /// here, in C#, from nothing.
    /// <para>
    /// Not a Razor page and above all not <c>_Layout.cshtml</c>: the layout
    /// reads the menu, the tariffs and <c>ThemeProvider</c> from the database.
    /// If the database is the reason the site was switched off, a page built on
    /// the layout would answer 500 instead of explaining itself — which is the
    /// one thing this page exists to avoid.
    /// </para>
    /// <para>
    /// Hence: styles written into the document, no external file, no script, no
    /// link, no logo. Nothing that can be missing.
    /// </para>
    /// <para>
    /// The <c>notfound</c> mode is not this page at all — see
    /// <see cref="NotFoundPage"/>.
    /// </para>
    /// </summary>
    public static class MaintenancePage
    {
        // Used when the control server sends no text of its own, or sends an
        // empty one. The page must say something even then.
        private const string DefaultMaintenanceBg = "Извършваме кратка поддръжка. Ще се върнем скоро.";
        private const string DefaultMaintenanceEn = "We are performing brief maintenance. We will be back shortly.";
        private const string DefaultErrorBg       = "Сайтът временно не е достъпен. Работим по въпроса.";
        private const string DefaultErrorEn       = "The site is temporarily unavailable. We are working on it.";

        private const string TitleMaintenanceBg = "Кратка поддръжка";
        private const string TitleMaintenanceEn = "Brief maintenance";
        private const string TitleErrorBg       = "Временно недостъпно";
        private const string TitleErrorEn       = "Temporarily unavailable";

        /// <summary>
        /// The page for one visitor: the message in their language, with
        /// Bulgarian as the default.
        /// <para>
        /// Except under <c>notfound</c>, where there is no visitor to speak to
        /// and nothing to say — the answer is the same handful of bytes for
        /// everyone, and <paramref name="english"/> and the two messages on
        /// <paramref name="decision"/> are all left unread.
        /// </para>
        /// </summary>
        public static string Render(RemoteControlDecision decision, bool english)
        {
            if (decision.StatusCode == StatusCodes.Status404NotFound)
                return NotFoundPage;

            var isError = decision.StatusCode == StatusCodes.Status500InternalServerError;

            var title = english
                ? (isError ? TitleErrorEn : TitleMaintenanceEn)
                : (isError ? TitleErrorBg : TitleMaintenanceBg);

            var message = english ? decision.MessageEn : decision.MessageBg;

            if (string.IsNullOrWhiteSpace(message))
            {
                message = english
                    ? (isError ? DefaultErrorEn : DefaultMaintenanceEn)
                    : (isError ? DefaultErrorBg : DefaultMaintenanceBg);
            }

            var language = english ? "en" : "bg";

            // WebUtility rather than string interpolation on its own: the text
            // comes from another machine, and it lands inside an HTML document.
            var safeTitle   = WebUtility.HtmlEncode(title);
            var safeMessage = WebUtility.HtmlEncode(message.Trim());

            var html = new StringBuilder();
            html.Append("<!DOCTYPE html>\n");
            html.Append("<html lang=\"").Append(language).Append("\">\n");
            html.Append("<head>\n");
            html.Append("<meta charset=\"utf-8\">\n");
            html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            html.Append("<meta name=\"robots\" content=\"noindex\">\n");
            html.Append("<title>").Append(safeTitle).Append("</title>\n");
            html.Append("<style>\n");
            html.Append(Styles);
            html.Append("</style>\n");
            html.Append("</head>\n");
            html.Append("<body>\n");
            html.Append("<main>\n");
            html.Append("<h1>").Append(safeTitle).Append("</h1>\n");
            html.Append("<p>").Append(safeMessage).Append("</p>\n");
            html.Append("</main>\n");
            html.Append("</body>\n");
            html.Append("</html>\n");

            return html.ToString();
        }

        /// <summary>
        /// The <c>notfound</c> answer: what a web server says about an address
        /// that leads nowhere, and not a syllable more.
        /// <para>
        /// Deliberately not the page above, and deliberately plain: no styles,
        /// no charset, no viewport, no <c>robots</c>, no language of ours, no
        /// message from the control server. English only — a 404 is not
        /// translated, and a page that knew which language to greet you in would
        /// be admitting that something here is reading the request.
        /// </para>
        /// <para>
        /// Whatever <c>messageBg</c> and <c>messageEn</c> carry, they are not
        /// written here. The point of this mode is that the site does not
        /// explain its absence.
        /// </para>
        /// </summary>
        private const string NotFoundPage = """
            <html><head><title>404 Not Found</title></head>
            <body><h1>Not Found</h1><p>The requested URL was not found on this server.</p></body></html>
            """;

        /// <summary>
        /// System fonts only — a web font is a request to somewhere else, and
        /// somewhere else is exactly what may be broken.
        /// </summary>
        private const string Styles = """
            :root { color-scheme: light dark; }
            * { box-sizing: border-box; }
            html, body { height: 100%; margin: 0; }
            body {
              display: flex;
              align-items: center;
              justify-content: center;
              padding: 24px;
              background: #f5f6f8;
              color: #1c2029;
              font-family: system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
              line-height: 1.6;
              -webkit-text-size-adjust: 100%;
            }
            main {
              max-width: 34rem;
              width: 100%;
              padding: 40px 32px;
              background: #ffffff;
              border: 1px solid #e2e5ea;
              border-radius: 14px;
              text-align: center;
            }
            h1 {
              margin: 0 0 16px;
              font-size: 1.5rem;
              font-weight: 600;
              letter-spacing: -0.01em;
            }
            p {
              margin: 0;
              font-size: 1.0625rem;
              color: #454b57;
              overflow-wrap: break-word;
            }
            @media (prefers-color-scheme: dark) {
              body { background: #14161a; color: #e8eaee; }
              main { background: #1c1f25; border-color: #2c313a; }
              p    { color: #b9bfca; }
            }
            """;

        /// <summary>
        /// Which language to write in, worked out without the localisation
        /// middleware: this page is asked for before <c>UseRouting</c> and has to
        /// stand on its own. Bulgarian unless the visitor has clearly asked for
        /// English — through the language cookie the site itself sets, or through
        /// <c>Accept-Language</c>.
        /// </summary>
        public static bool PrefersEnglish(HttpRequest request)
        {
            if (request.Cookies.TryGetValue(CookieRequestCultureProvider.DefaultCookieName, out var cookie) &&
                !string.IsNullOrWhiteSpace(cookie))
            {
                var culture = CookieRequestCultureProvider.ParseCookieValue(cookie);
                var chosen  = culture?.UICultures.FirstOrDefault().Value
                           ?? culture?.Cultures.FirstOrDefault().Value;

                if (!string.IsNullOrWhiteSpace(chosen))
                    return chosen.StartsWith("en", StringComparison.OrdinalIgnoreCase);
            }

            return AcceptsEnglishFirst(request.Headers.AcceptLanguage.ToString());
        }

        /// <summary>
        /// The first of the two languages the site speaks that the browser asks
        /// for, by quality. Anything else in the header is ignored — a visitor
        /// who prefers German still gets Bulgarian.
        /// </summary>
        private static bool AcceptsEnglishFirst(string? header)
        {
            if (string.IsNullOrWhiteSpace(header)) return false;

            var best = default(string);
            var bestQuality = -1.0;

            foreach (var entry in header.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = entry.Split(';', StringSplitOptions.RemoveEmptyEntries);
                var tag   = parts[0].Trim();

                if (tag.Length == 0) continue;

                var isBulgarian = tag.StartsWith("bg", StringComparison.OrdinalIgnoreCase);
                var isEnglish   = tag.StartsWith("en", StringComparison.OrdinalIgnoreCase);
                if (!isBulgarian && !isEnglish) continue;

                var quality = 1.0;
                foreach (var parameter in parts.Skip(1))
                {
                    var trimmed = parameter.Trim();
                    if (!trimmed.StartsWith("q=", StringComparison.OrdinalIgnoreCase)) continue;

                    if (!double.TryParse(trimmed.AsSpan(2), System.Globalization.NumberStyles.Float,
                                         System.Globalization.CultureInfo.InvariantCulture, out quality))
                        quality = 1.0;
                }

                // Strictly greater: on a tie the one written first wins, which is
                // the order the browser meant.
                if (quality > bestQuality)
                {
                    bestQuality = quality;
                    best = isEnglish ? "en" : "bg";
                }
            }

            return best == "en";
        }
    }
}
