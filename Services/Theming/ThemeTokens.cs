// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ConferenceApp.Services.Theming
{
    public enum TokenKind
    {
        /// <summary>#rgb, #rrggbb, #rrggbbaa</summary>
        Color,
        /// <summary>"255, 255, 255" — three numbers, for rgba(var(--ink), α)</summary>
        Triplet,
        /// <summary>rgba(r, g, b, α)</summary>
        Rgba,
        /// <summary>A number with a unit: 1180px, 2rem</summary>
        Length,
        /// <summary>A shadow: a list of lengths and one colour</summary>
        Shadow,
        /// <summary>clamp(a, b, c) of lengths</summary>
        Clamp,
        /// <summary>A blend mode: overlay, multiply, screen…</summary>
        Blend,
        /// <summary>A number between 0 and 1</summary>
        Unit,
        /// <summary>A color-mix value derived from --accent</summary>
        Mix
    }

    public sealed record TokenSpec(
        string Name, TokenKind Kind, string Default, string Description);

    public sealed class ThemeCheck
    {
        public bool Ok => Errors.Count == 0;
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
        public Dictionary<string, string> Values { get; } = new();
    }

    /// <summary>
    /// Validates a theme that came from outside and turns it into CSS.
    ///
    /// <para>
    /// <b>Why a theme is JSON and not CSS:</b> a CSS file uploaded from the
    /// panel would have to be sanitized as text, and a text sanitizer is never
    /// complete. JSON with a fixed list of keys and a per-value check BY TYPE is
    /// a closed problem: either the value is a valid colour or it is not. There
    /// is no third option and nothing to slip through.
    /// </para>
    ///
    /// <para>
    /// An unknown key in the file is a warning rather than an error, so that a
    /// theme written for a newer version still loads and simply ignores what it
    /// does not understand.
    /// </para>
    ///
    /// <para>
    /// The token descriptions are shown to the administrator and to the model
    /// that generates a theme, so they stay in Bulgarian.
    /// </para>
    /// </summary>
    public static class ThemeTokens
    {
        public static readonly TokenSpec[] All =
        {
            // ── Base ──────────────────────────────────────────────────────
            new("--bg",          TokenKind.Color,   "#050505",
                "Фонът на цялата страница. Най-тъмното (или най-светлото) в темата."),
            new("--panel",       TokenKind.Color,   "#0b0b0b",
                "Панели и карти върху фона. Малко по-различен от --bg, за да се отделят."),
            new("--surface",     TokenKind.Color,   "#0a0a0a",
                "Втора повърхност — вложени блокове вътре в панел."),
            new("--surface-alt", TokenKind.Color,   "#17181a",
                "Трета повърхност, с лек студен оттенък."),

            // ── Text ──────────────────────────────────────────────────────
            new("--text",        TokenKind.Color,   "#f4f2ec",
                "Основният текст. Трябва да има поне 7:1 контраст спрямо --bg."),
            new("--text-soft",   TokenKind.Color,   "#d8d5cf",
                "Малко по-тих от основния — подзаглавия."),
            new("--muted",       TokenKind.Color,   "#a7a39b",
                "Второстепенен текст. Поне 4.5:1 спрямо --bg."),
            new("--text-dim",    TokenKind.Color,   "#8a8680",
                "Още по-тих — бележки, дати, помощен текст."),
            new("--text-faint",  TokenKind.Color,   "#6f6b66",
                "Най-тихият четим текст. Под него става нечетимо."),

            // ── Accent ────────────────────────────────────────────────────
            new("--accent",      TokenKind.Color,   "#FF3636",
                "Единственият акцент на сайта. Бутони, връзки, подчертавания."),
            new("--accent-soft", TokenKind.Color,   "#ff363692",
                "Същият акцент с прозрачност — за фонове зад акцентен текст."),

            // The three derived shades are DERIVED from --accent with
            // color-mix rather than repeated as separate colours, so that
            // changing the accent moves all of them at once.
            new("--accent-hover",  TokenKind.Mix, "color-mix(in srgb, var(--accent) 85%, #000)",
                "Акцентът при посочване. По-тъмен от основния."),
            new("--accent-active", TokenKind.Mix, "color-mix(in srgb, var(--accent) 70%, #000)",
                "Акцентът при натискане. Още по-тъмен."),
            new("--accent-light",  TokenKind.Mix, "color-mix(in srgb, var(--accent) 72%, #fff)",
                "По-светъл вариант — за връзки върху фон."),

            new("--on-accent",     TokenKind.Color, "#ffffff",
                "Цветът на текста ВЪРХУ акцентен бутон. НЕ се извежда от --ink: " +
                "--ink следва дали ТЕМАТА е тъмна, а тук значение има дали САМИЯТ " +
                "АКЦЕНТ е тъмен или светъл. Тъмна тема със светъл акцент (жълто " +
                "върху черно) иска ТЪМЕН текст на бутона — обратното на --ink."),

            // ── The contrast layer ────────────────────────────────────────
            new("--ink",         TokenKind.Triplet, "255, 255, 255",
                "ТРИ ЧИСЛА, не цвят. Основата на 488 полупрозрачни рамки и фонове. " +
                "На тъмна тема е 255,255,255; на светла — 0,0,0. Това е ключът към " +
                "обръщането на цялата тема."),
            new("--line",        TokenKind.Rgba,    "rgba(255, 255, 255, 0.08)",
                "Разделителна линия. Обикновено --ink с алфа около 0.08."),
            new("--panel-soft",  TokenKind.Rgba,    "rgba(255, 255, 255, 0.04)",
                "Много лек фон върху панел. --ink с алфа около 0.04."),

            // ── States (these are NOT inverted for a light theme) ─────────
            new("--ok",             TokenKind.Color, "#4ea86b", "Успех. Зелено и на светъл фон."),
            new("--warn",           TokenKind.Color, "#d9932b", "Предупреждение, изчакване."),
            new("--danger",         TokenKind.Color, "#b81c1c", "Грешка, отказ."),
            new("--danger-bright",  TokenKind.Color, "#e03535", "По-ярък вариант за акцентна грешка."),

            // ── Blending and logos ────────────────────────────────────────
            // These are not colours but a WAY of compositing, and which one is
            // right depends on whether what is underneath is dark or light.
            // "screen" and "overlay" lighten; over a white background they
            // either do nothing or make a mess.
            new("--blend-tint",    TokenKind.Blend, "overlay",
                "Наслагване върху снимките в банерите. Тъмна: overlay. Светла: multiply."),
            new("--blend-soft",    TokenKind.Blend, "soft-light",
                "Фините решетки върху снимки. Тъмна: soft-light. Светла: multiply."),
            new("--blend-ambient", TokenKind.Blend, "screen",
                "Глобалният фонов слой. Тъмна: screen. Светла: multiply."),

            new("--logo-invert",   TokenKind.Unit, "1",
                "Логата са бели PNG. brightness(0) ги прави черни, invert ги връща " +
                "бели. Значи 1 = бяло лого (тъмна тема), 0 = ЧЕРНО лого (светла)."),

            // ── Structure ─────────────────────────────────────────────────
            new("--shadow",    TokenKind.Shadow, "0 30px 80px rgba(0, 0, 0, 0.35)",
                "Сянка на изскачащите панели. На светла тема алфата трябва да е по-малка."),
            new("--max-width", TokenKind.Length, "1180px",
                "Ширина на съдържанието."),
            new("--mobile-nav-strip-width", TokenKind.Clamp, "clamp(56px, 15vw, 84px)",
                "Ширина на страничната лента в мобилното меню."),
        };

        private static readonly Dictionary<string, TokenSpec> ByName =
            All.ToDictionary(t => t.Name, StringComparer.Ordinal);

        // ── Patterns ──────────────────────────────────────────────────────
        // Deliberately STRICT: better to refuse valid but exotic syntax than to
        // let through something that later escapes the CSS declaration.
        private static readonly Regex ReColor = new(@"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$");
        private static readonly Regex ReTriplet = new(@"^\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*$");
        private static readonly Regex ReRgba = new(@"^rgba?\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}\s*(?:,\s*(?:0|1|0?\.\d+)\s*)?\)$");
        private static readonly Regex ReLength = new(@"^-?\d+(?:\.\d+)?(?:px|rem|em|vw|vh|vmin|vmax|%)$");
        private static readonly Regex ReClamp = new(@"^clamp\(\s*[^(),]+,\s*[^(),]+,\s*[^(),]+\s*\)$");
        private static readonly Regex ReBlend = new(@"^(?:normal|multiply|screen|overlay|darken|lighten|color-dodge|color-burn|hard-light|soft-light|difference|exclusion|hue|saturation|color|luminosity)$");
        private static readonly Regex ReUnit = new(@"^(?:0|1|0?\.\d+)$");
        // A tightly restricted shape: ONLY a color-mix of --accent with black
        // or white. An arbitrary color-mix is not accepted, because it would
        // allow another variable to be smuggled in.
        private static readonly Regex ReMix = new(@"^color-mix\(in srgb, var\(--accent\) \d{1,3}%, (?:#[0-9a-fA-F]{3,8}|transparent)\)$");
        // The unit is optional because a bare "0" is a valid CSS length and the
        // first value of a shadow is almost always exactly 0.
        private static readonly Regex ReShadow = new(@"^(?:inset\s+)?(?:-?\d+(?:\.\d+)?(?:px|rem|em)?\s+){2,4}rgba?\([^()]*\)$");

        /// <summary>
        /// Checks one value against its type. Returns null when it is valid,
        /// otherwise a message meant for a person.
        /// </summary>
        public static string? ValidateValue(TokenSpec spec, string raw)
        {
            var v = raw.Trim();

            if (v.Length == 0) return "празна стойност";
            if (v.Length > 120) return "твърде дълга стойност";

            // The first line of defence: nothing that could escape the
            // declaration or pull in an external resource.
            foreach (var bad in new[] { ";", "}", "{", "<", ">", "@", "url(", "expression", "javascript:", "/*", "\\" })
                if (v.Contains(bad, StringComparison.OrdinalIgnoreCase))
                    return $"забранен знак или дума: „{bad}“";

            var ok = spec.Kind switch
            {
                TokenKind.Color   => ReColor.IsMatch(v),
                TokenKind.Triplet => ReTriplet.IsMatch(v) && TripletInRange(v),
                TokenKind.Rgba    => ReRgba.IsMatch(v),
                TokenKind.Length  => ReLength.IsMatch(v),
                TokenKind.Clamp   => ReClamp.IsMatch(v),
                TokenKind.Shadow  => ReShadow.IsMatch(v),
                TokenKind.Blend   => ReBlend.IsMatch(v),
                TokenKind.Unit    => ReUnit.IsMatch(v),
                // A plain colour is accepted too: a theme that would rather not
                // use color-mix can set the value directly.
                TokenKind.Mix     => ReMix.IsMatch(v) || ReColor.IsMatch(v),
                _ => false
            };

            return ok ? null : spec.Kind switch
            {
                TokenKind.Color   => "очаква се цвят като #1a1a1a",
                TokenKind.Triplet => "очакват се три числа 0-255, напр. „255, 255, 255“",
                TokenKind.Rgba    => "очаква се rgba(r, g, b, а)",
                TokenKind.Length  => "очаква се число с мерна единица, напр. 1180px",
                TokenKind.Clamp   => "очаква се clamp(мин, предпочитано, макс)",
                TokenKind.Shadow  => "очаква се сянка, напр. 0 30px 80px rgba(0,0,0,0.35)",
                TokenKind.Blend   => "очаква се режим като overlay, multiply, screen, soft-light",
                TokenKind.Unit    => "очаква се число между 0 и 1",
                TokenKind.Mix     => "очаква се цвят или color-mix(in srgb, var(--accent) N%, #000)",
                _ => "непозната стойност"
            };
        }

        private static bool TripletInRange(string v)
        {
            var m = ReTriplet.Match(v);
            for (var i = 1; i <= 3; i++)
                if (int.Parse(m.Groups[i].Value) > 255) return false;
            return true;
        }

        /// <summary>Validates a whole theme file.</summary>
        public static ThemeCheck Parse(string json)
        {
            var res = new ThemeCheck();

            JsonElement root;
            try
            {
                using var doc = JsonDocument.Parse(json);
                root = doc.RootElement.Clone();
            }
            catch (JsonException e)
            {
                res.Errors.Add("Файлът не е валиден JSON: " + e.Message);
                return res;
            }

            // The tokens may sit at the root or under "tokens". Both are
            // accepted, because the template produces the latter while a person
            // easily sends the former.
            var tokens = root.ValueKind == JsonValueKind.Object &&
                         root.TryGetProperty("tokens", out var t) && t.ValueKind == JsonValueKind.Object
                         ? t : root;

            if (tokens.ValueKind != JsonValueKind.Object)
            {
                res.Errors.Add("Очаква се обект с токени.");
                return res;
            }

            var seen = 0;
            foreach (var p in tokens.EnumerateObject())
            {
                var key = p.Name.StartsWith("--") ? p.Name : "--" + p.Name;

                if (!ByName.TryGetValue(key, out var spec))
                {
                    // An unknown key is NOT an error: a theme from a newer
                    // version should still load, ignoring what we do not
                    // understand.
                    res.Warnings.Add($"Непознат токен „{p.Name}“ — пропуснат.");
                    continue;
                }

                if (p.Value.ValueKind != JsonValueKind.String)
                {
                    res.Errors.Add($"{key}: стойността трябва да е текст.");
                    continue;
                }

                var val = p.Value.GetString() ?? "";
                var err = ValidateValue(spec, val);
                if (err is not null) res.Errors.Add($"{key}: {err} (получено: „{val}“)");
                else { res.Values[key] = val.Trim(); seen++; }
            }

            if (seen == 0 && res.Errors.Count == 0)
                res.Errors.Add("Файлът не съдържа нито един разпознат токен.");

            // Missing tokens fall back to their defaults — a theme that changes
            // only the accent is a perfectly valid theme.
            var missing = All.Count(s => !res.Values.ContainsKey(s.Name));
            if (missing > 0 && res.Ok)
                res.Warnings.Add($"{missing} токена липсват — ползват се стандартните им стойности.");

            return res;
        }

        /// <summary>
        /// Builds the CSS for <c>:root</c>. The values have already been
        /// validated, so nothing is processed here.
        /// </summary>
        public static string BuildCss(IDictionary<string, string> values)
        {
            var sb = new StringBuilder();
            foreach (var spec in All)
                if (values.TryGetValue(spec.Name, out var v))
                    sb.Append(spec.Name).Append(':').Append(v).Append(';');
            return sb.Length == 0 ? string.Empty : ":root{" + sb + "}";
        }
    }
}
