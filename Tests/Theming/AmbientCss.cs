// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text.RegularExpressions;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Theming;

/// <summary>
/// What <c>globalEffects.css</c> promises for each preset.
///
/// <para>
/// A preset is a drawing, and the drawing lives only in the CSS: it cannot be
/// generated from C# (see the note in <c>AmbientBackgrounds</c>). The test
/// therefore asks the file itself which layer (<c>.afx-a</c> or <c>.afx-b</c>)
/// has a <c>background</c> rule for which preset. The browser then has to
/// confirm that the layer really does draw.
/// </para>
///
/// <para>
/// The search is deliberately loose — it matches the
/// <c>[data-gfx-bg="…"] .afx-…</c> fragment without requiring the selector to
/// start with <c>#ambient-fx</c>. A rule with a superfluous selector in front of
/// it is still a promise in the file, and that is exactly the kind of rule the
/// browser silently fails to apply.
/// </para>
/// </summary>
public static class AmbientCss
{
    private static IReadOnlyList<(string Background, string Layer)>? _promises;

    /// <summary>The preset-and-layer pairs the file defines a drawing for.</summary>
    public static IReadOnlyList<(string Background, string Layer)> Promises =>
        _promises ??= Read();

    public static IEnumerable<string> LayersOf(string background) =>
        Promises.Where(p => p.Background == background).Select(p => p.Layer).Distinct();

    private static IReadOnlyList<(string, string)> Read()
    {
        var path = Path.Combine(TestPaths.RepoRoot, "wwwroot", "css", "globalEffects.css");
        var css  = File.ReadAllText(path);

        // The comments are dropped: they contain both preset names and fragments of
        // CSS.
        css = Regex.Replace(css, @"/\*.*?\*/", " ", RegexOptions.Singleline);

        var found = new List<(string, string)>();

        foreach (var rule in css.Split('}'))
        {
            var brace = rule.IndexOf('{');
            if (brace < 0) continue;

            var selector = rule[..brace];
            var body     = rule[(brace + 1)..];

            // Only rules that really draw. "background: none" and
            // "background-blend-mode" are not a drawing.
            if (!Regex.IsMatch(body, @"background\s*:\s*(?!none)\S")) continue;

            foreach (Match m in Regex.Matches(selector,
                         @"\[data-gfx-bg=""(?<bg>[a-z-]+)""\][^,{]*?\.afx-(?<layer>[ab])\b"))
            {
                var pair = (m.Groups["bg"].Value, "afx-" + m.Groups["layer"].Value);
                if (!found.Contains(pair)) found.Add(pair);
            }
        }

        if (found.Count == 0)
            throw new InvalidOperationException(
                $"В {path} не намерих нито едно правило за присет — образецът е остарял.");

        return found;
    }
}
