using System.Text;
using System.Text.RegularExpressions;

namespace ConferenceApp.Services.Styles
{
    public sealed class CssCheckResult
    {
        /// <summary>ok | warn | bad — същите нива като в панела.</summary>
        public string Level { get; set; } = "ok";
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();

        /// <summary>Преписаният CSS с наложен обхват. Празен при Level == "bad".</summary>
        public string Sanitized { get; set; } = string.Empty;
    }

    /// <summary>
    /// Санитизира собствения CSS от админ панела.
    ///
    /// <para>
    /// <b>Това е защитата, не клиентската проверка.</b> Панелът проверява
    /// същите правила веднага, за удобство, но клиентът никога не е защита —
    /// заявка може да се прати и без него.
    /// </para>
    ///
    /// <para>
    /// Съзнателно НЕ е пълен CSS парсър. Работи на ниво селектори и забранени
    /// низове. Екзотичен вход (вложени @supports с правила вътре) минава като
    /// текст и в най-лошия случай не се прилага нищо. Ако това стане важно,
    /// мястото е истински парсър (ExCSS), не още регулярни изрази.
    /// </para>
    /// </summary>
    public static class CssSanitizer
    {
        private const int MaxLength = 4000;

        /// <summary>
        /// Селектори, които показват намерение да се излезе от фона. Такъв
        /// запис се ОТКАЗВА, а не се префиксира мълчаливо: тихото пренаписване
        /// би скрило проблема от човека, който го е написал.
        /// </summary>
        private static readonly string[] EscapingSelectors =
        {
            "html", "body", "main", "header", "footer", "nav", "*",
            ".topbar", ".site-main", ".site-footer", "#body"
        };

        /// <summary>
        /// Низове, които спират излизане от &lt;style&gt; контекста или външни
        /// заявки от името на посетителя.
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
        /// Като <see cref="Sanitize"/>, но позволява и <c>@keyframes</c>.
        /// Имената се префиксират с <c>gfx-u-{scope}-</c>, за да не могат да
        /// презапишат системните (<c>gfx-c-slide</c> и другите) — иначе един
        /// собствен фон би променил движението на всички останали.
        /// </summary>
        public static CssCheckResult SanitizeWithKeyframes(string? raw, string scope, string selector)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new CssCheckResult();

            var prefix = $"gfx-u-{scope}-";
            var css = raw;
            var names = new List<string>();

            // 1) Намираме имената на собствените keyframes и ги преименуваме
            //    навсякъде — и в дефиницията, и в animation: правилата.
            foreach (Match m in Regex.Matches(css, @"@keyframes\s+([A-Za-z_][\w-]*)", RegexOptions.IgnoreCase))
                if (!names.Contains(m.Groups[1].Value)) names.Add(m.Groups[1].Value);

            foreach (var n in names)
            {
                css = Regex.Replace(css, $@"@keyframes\s+{Regex.Escape(n)}\b", $"@keyframes {prefix}{n}");
                // Замяна в animation / animation-name, но НЕ на системните имена.
                css = Regex.Replace(css, $@"(animation(?:-name)?\s*:[^;{{}}]*?)\b{Regex.Escape(n)}\b",
                                    $"$1{prefix}{n}");
            }

            // 2) Изрязваме keyframes блоковете, за да не пречат на обхватния
            //    анализ на обикновените правила, и ги връщаме накрая.
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

            // ── 1. Груби откази ───────────────────────────────────────────
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

            // url() само с data: — всичко друго е външна заявка
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

            // ── 2. Налагане на обхват ─────────────────────────────────────
            var scoped = new StringBuilder();

            foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}", RegexOptions.Singleline))
            {
                var selectorRaw = rule.Groups[1].Value.Trim();
                var body = rule.Groups[2].Value.Trim();

                if (selectorRaw.Length == 0 || body.Length == 0) continue;

                // @media / @supports минават като текст — правилата вътре не
                // се преписват. Виж бележката за парсъра в резюмето на класа.
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

                    // Вече в обхват — не се пипа.
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

            // ── 3. Предупреждения (не отказ) ─────────────────────────────
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
