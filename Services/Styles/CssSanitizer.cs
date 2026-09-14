// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text;
using System.Text.RegularExpressions;

namespace ConferenceApp.Services.Styles
{
    public sealed class CssCheckResult
    {
        /// <summary>ok | warn | bad — the same three levels the panel shows.</summary>
        public string Level { get; set; } = "ok";
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();

        /// <summary>The rewritten, scoped CSS. Empty when Level is "bad".</summary>
        public string Sanitized { get; set; } = string.Empty;
    }

    /// <summary>
    /// Sanitizes the custom CSS written in the admin panel.
    ///
    /// <para>
    /// <b>This is the defence; the check in the browser is not.</b> The panel
    /// applies the same rules immediately, for convenience, but a client-side
    /// check is never a defence — a request can be sent without it.
    /// </para>
    ///
    /// <para>
    /// It is deliberately NOT a full CSS parser. It works at the level of
    /// selectors and forbidden substrings. Exotic input (a nested @supports with
    /// rules inside) passes through as text and at worst nothing is applied. If
    /// that ever starts to matter, the answer is a real parser (ExCSS), not more
    /// regular expressions.
    /// </para>
    /// </summary>
    public static class CssSanitizer
    {
        private const int MaxLength = 4000;

        /// <summary>
        /// Selectors that show an intent to reach outside the background. Such
        /// a rule is REFUSED rather than silently prefixed: rewriting it quietly
        /// would hide the problem from the person who wrote it.
        /// </summary>
        private static readonly string[] EscapingSelectors =
        {
            "html", "body", "main", "header", "footer", "nav", "*",
            ".topbar", ".site-main", ".site-footer", "#body"
        };

        /// <summary>
        /// Substrings that would break out of the &lt;style&gt; context or make
        /// an outbound request on the visitor's behalf. The explanations are
        /// shown to the administrator, so they stay in Bulgarian.
        /// </summary>
        private static readonly (string Needle, string Why)[] Forbidden =
        {
            ("<",           "Знакът < може да затвори <style> блока."),
            (">",           "Знакът > може да затвори <style> блока."),
            ("@import",     "@import прави външна заявка от името на посетителя."),
            ("expression(", "expression() изпълнява код в стари браузъри."),
            ("javascript:", "javascript: изпълнява код."),
            ("behavior:",   "behavior: зарежда външен скрипт в стари браузъри."),
            ("-moz-binding","-moz-binding зарежда външен документ."),
        };

        /// <summary>
        /// Like <see cref="Sanitize"/>, but <c>@keyframes</c> is allowed as
        /// well. The names are prefixed with <c>gfx-u-{scope}-</c> so that they
        /// cannot overwrite the built-in ones (<c>gfx-c-slide</c> and the rest)
        /// — otherwise one custom background would change the motion of every
        /// other.
        /// </summary>
        public static CssCheckResult SanitizeWithKeyframes(string? raw, string scope, string selector)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new CssCheckResult();

            var prefix = $"gfx-u-{scope}-";
            var css = raw;
            var names = new List<string>();

            // 1) Collect the names of the custom keyframes and rename them
            //    everywhere — in the definition and in the animation rules.
            foreach (Match m in Regex.Matches(css, @"@keyframes\s+([A-Za-z_][\w-]*)", RegexOptions.IgnoreCase))
                if (!names.Contains(m.Groups[1].Value)) names.Add(m.Groups[1].Value);

            foreach (var n in names)
            {
                css = Regex.Replace(css, $@"@keyframes\s+{Regex.Escape(n)}\b", $"@keyframes {prefix}{n}");
                // Renamed inside animation / animation-name only, so that a
                // built-in name mentioned elsewhere is left alone.
                css = Regex.Replace(css, $@"(animation(?:-name)?\s*:[^;{{}}]*?)\b{Regex.Escape(n)}\b",
                                    $"$1{prefix}{n}");
            }

            // 2) Cut the keyframes blocks out, so that they do not confuse the
            //    scope analysis of the ordinary rules, and put them back at the
            //    end. They need no scoping: the prefix above already makes their
            //    names unique.
            var frames = new StringBuilder();
            css = Regex.Replace(css, @"@keyframes\s+[\w-]+\s*\{(?:[^{}]|\{[^{}]*\})*\}",
                m => { frames.Append(m.Value).Append('\n'); return string.Empty; },
                RegexOptions.IgnoreCase);

            var result = Sanitize(css, selector);
            if (result.Level == "bad") return result;

            if (frames.Length > 0)
                result.Sanitized = frames.ToString() + result.Sanitized;

            return result;
        }

        public static CssCheckResult Sanitize(string? raw) => Sanitize(raw, "#ambient-fx");

        public static CssCheckResult Sanitize(string? raw, string scopeSelector)
        {
            var result = new CssCheckResult();

            if (string.IsNullOrWhiteSpace(raw))
            {
                result.Sanitized = string.Empty;
                return result;
            }

            var css = raw.Trim();

            // ── 1. Outright refusals ──────────────────────────────────────
            if (css.Length > MaxLength)
            {
                result.Errors.Add($"Над {MaxLength} знака ({css.Length}).");
                result.Level = "bad";
                return result;
            }

            foreach (var (needle, why) in Forbidden)
            {
                if (css.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    result.Errors.Add($"Забранено: „{needle}“. {why}");
                    result.Level = "bad";
                }
            }

            // url() with a data: target only — anything else is an outbound
            // request made from the visitor's browser.
            foreach (Match m in Regex.Matches(css, @"url\(\s*([""']?)([^""')]*)\1\s*\)", RegexOptions.IgnoreCase))
            {
                var target = m.Groups[2].Value.Trim();
                if (!target.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    result.Errors.Add("url() към външен адрес. Позволено е само data:.");
                    result.Level = "bad";
                    break;
                }
            }

            if (CountOf(css, '{') != CountOf(css, '}'))
            {
                result.Errors.Add("Неравен брой къдрави скоби.");
                result.Level = "bad";
            }

            if (result.Level == "bad") return result;

            // ── 2. Scoping ────────────────────────────────────────────────
            var scoped = new StringBuilder();

            foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}", RegexOptions.Singleline))
            {
                var selectorRaw = rule.Groups[1].Value.Trim();
                var body = rule.Groups[2].Value.Trim();

                if (selectorRaw.Length == 0 || body.Length == 0) continue;

                // @media / @supports pass through as text — the rules inside are
                // not rewritten. See the note about the parser in the class
                // summary.
                if (selectorRaw.StartsWith("@"))
                {
                    result.Warnings.Add($"„{Trim(selectorRaw, 40)}“ не се обработва и може да не се приложи.");
                    continue;
                }

                var parts = selectorRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var kept = new List<string>();

                foreach (var sel in parts)
                {
                    var head = sel.Split(' ', ':', '>', '+', '~')[0].Trim().ToLowerInvariant();

                    if (EscapingSelectors.Contains(head))
                    {
                        result.Errors.Add(
                            $"Селекторът „{Trim(sel, 40)}“ излиза извън фона и може да засегне цялата страница.");
                        result.Level = "bad";
                        continue;
                    }

                    // Already inside the layer — left as written.
                    kept.Add(
                        sel.StartsWith("#ambient-fx", StringComparison.OrdinalIgnoreCase) ||
                        sel.StartsWith(".afx-", StringComparison.OrdinalIgnoreCase)
                            ? sel
                            : $"{scopeSelector} {sel}");
                }

                if (kept.Count > 0)
                    scoped.Append(string.Join(", ", kept)).Append(" { ").Append(body).Append(" }\n");
            }

            if (result.Level == "bad") return result;

            // ── 3. Warnings, not refusals ────────────────────────────────
            // Both of these are legal CSS that usually means the author expected
            // something else; the rule is still applied.
            if (Regex.IsMatch(css, @"blur\(\s*([5-9]\d|\d{3,})", RegexOptions.IgnoreCase))
                result.Warnings.Add("Голям blur() върху цял екран е скъп за слаби устройства.");

            if (Regex.IsMatch(css, @"position\s*:\s*fixed", RegexOptions.IgnoreCase))
                result.Warnings.Add("position: fixed вътре във фона рядко прави каквото се очаква.");

            result.Sanitized = scoped.ToString().Trim();

            if (result.Sanitized.Length == 0 && css.Length > 0)
                result.Warnings.Add("Нищо не остана след обработката — провери синтаксиса.");

            if (result.Warnings.Count > 0) result.Level = "warn";
            return result;
        }

        private static int CountOf(string s, char c) => s.Count(ch => ch == c);

        private static string Trim(string s, int max)
            => s.Length <= max ? s : s[..max] + "…";
    }
}
