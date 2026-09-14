// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ConferenceApp.Services.Styles
{
    /// <summary>
    /// Turns the parameters of a custom background into CSS.
    ///
    /// <para>
    /// <b>This is the ONLY place that CSS is generated.</b> The panel does not
    /// have a version of its own — it asks for the rule from here through
    /// <c>?handler=CustomBackgroundCss</c>. The reason: two implementations of
    /// the same thing drift apart silently, and then the preview shows one thing
    /// while the site draws another. The cost is one request per change in the
    /// editor.
    /// </para>
    /// </summary>
    public static class CustomBackgroundCss
    {
        private sealed class Layer
        {
            public string Kind { get; set; } = "none";
            public string Color { get; set; } = "ink";
            public int Spacing { get; set; } = 48;
            public int Thickness { get; set; } = 1;
            public int Angle { get; set; } = 45;
            public double Alpha { get; set; } = 0.05;
            public string Motion { get; set; } = "none";
            public int Duration { get; set; } = 120;
        }

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// The "free CSS" mode: the administrator wrote the rules by hand.
        /// Scoping is enforced here — every selector is nested under the
        /// selector of this one background, so one custom background cannot
        /// affect another.
        /// </summary>
        public static string BuildFromCss(string slug, string? rawCss)
        {
            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(rawCss))
                return string.Empty;

            if (!System.Text.RegularExpressions.Regex.IsMatch(slug, @"^[a-z0-9-]{2,40}$"))
                return string.Empty;

            var sel = $"#ambient-fx[data-gfx-bg=\"custom:{slug}\"]";
            var checkd = CssSanitizer.SanitizeWithKeyframes(rawCss, slug, sel);
            return checkd.Level == "bad" ? string.Empty : checkd.Sanitized;
        }

        /// <summary>
        /// The layer mode: the rules are assembled from the parameters the panel
        /// collected. Returns an empty string on invalid input — the background
        /// then simply does not appear, rather than the page failing.
        /// </summary>
        public static string Build(string slug, string? layersJson)
        {
            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(layersJson))
                return string.Empty;

            // The slug goes into a CSS selector. It is checked here as well as
            // on write, because this method can be called with data from
            // elsewhere.
            if (!System.Text.RegularExpressions.Regex.IsMatch(slug, @"^[a-z0-9-]{2,40}$"))
                return string.Empty;

            Layer? a, b;
            try
            {
                using var doc = JsonDocument.Parse(layersJson);
                a = Read(doc.RootElement, "a");
                b = Read(doc.RootElement, "b");
            }
            catch (JsonException)
            {
                return string.Empty;
            }

            var sel = $"#ambient-fx[data-gfx-bg=\"custom:{slug}\"]";
            var sb = new StringBuilder();

            if (a is not null) sb.Append(sel).Append(" .afx-a{").Append(LayerCss(a)).Append("}\n");
            if (b is not null) sb.Append(sel).Append(" .afx-b{").Append(LayerCss(b)).Append("}\n");

            return sb.ToString();
        }

        private static Layer? Read(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Object)
                return null;

            var layer = el.Deserialize<Layer>(JsonOpts);
            if (layer is null) return null;

            // The ranges are the same as in the panel. A value outside them is
            // clamped rather than rejected — one wrong parameter must not remove
            // the whole background.
            layer.Kind      = In(layer.Kind, "none", "lines", "grid", "dots", "rings", "glow");
            layer.Color     = In(layer.Color, "ink", "accent");
            layer.Motion    = In(layer.Motion, "none", "slide", "swell", "drift");
            layer.Spacing   = Clamp(layer.Spacing, 4, 240);
            layer.Thickness = Clamp(layer.Thickness, 1, 6);
            layer.Angle     = Clamp(layer.Angle, 0, 180);
            layer.Duration  = Clamp(layer.Duration, 20, 600);
            layer.Alpha     = Math.Clamp(layer.Alpha, 0, 0.3);

            return layer;
        }

        private static string LayerCss(Layer l)
        {
            if (l.Kind == "none") return "background: none;";

            var ink = Rgba(l.Color, l.Alpha);
            var t = l.Thickness;
            var s = Math.Max(l.Spacing, t + 1);
            var css = l.Kind switch
            {
                "lines" =>
                    $"background: repeating-linear-gradient({l.Angle}deg, {ink} 0 {t}px, transparent {t}px {s}px);",

                "grid" =>
                    $"background: linear-gradient({ink} {t}px, transparent {t}px) 0 0 / 100% {s}px," +
                    $" linear-gradient(90deg, {ink} {t}px, transparent {t}px) 0 0 / {s}px 100%;",

                "dots" =>
                    $"background: radial-gradient(circle at 30% 34%, {ink} 0 {t}px, transparent {F(t + 0.6)}px) 0 0 / {s}px {s}px;",

                "rings" =>
                    $"background: repeating-radial-gradient(circle at 18% -8%, transparent 0 {s}px, {ink} {s}px {s + t}px);",

                "glow" =>
                    $"background: radial-gradient(60% 90% at 50% -18%, {Rgba(l.Color, l.Alpha * 3)} 0%, " +
                    $"{Rgba(l.Color, l.Alpha * 1.4)} 34%, {Rgba(l.Color, l.Alpha * 0.4)} 66%, {Rgba(l.Color, 0)} 100%);" +
                    " mask-image: none; -webkit-mask-image: none;",

                _ => "background: none;"
            };

            if (l.Motion != "none")
            {
                var easing = l.Motion == "slide" ? "linear" : "ease-in-out";
                css += $" animation: gfx-c-{l.Motion} {l.Duration}s {easing} infinite;";
            }

            return css;
        }

        private static string Rgba(string color, double alpha)
            => color == "accent"
                ? $"rgba(255, 54, 54, {F(alpha)})"
                : $"rgba(255, 255, 255, {F(alpha)})";

        /// <summary>Always a dot for the decimal separator — under a Bulgarian
        /// locale it would come out as "0,05" and the CSS rule would be
        /// invalid.</summary>
        private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        private static string In(string? v, params string[] allowed)
            => allowed.Contains(v, StringComparer.OrdinalIgnoreCase) ? v!.ToLowerInvariant() : allowed[0];
    }
}
