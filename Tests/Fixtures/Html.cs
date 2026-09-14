// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using System.Text.RegularExpressions;

namespace ConferenceApp.Tests.Fixtures;

/// <summary>
/// Help with reading the pages that come back.
/// <para>
/// The application does not widen <c>HtmlEncoder</c> (see the note in
/// TESTS.md), so every dynamic string in Cyrillic arrives as
/// <c>&amp;#x41D;…</c>. A browser renders that correctly, but a text comparison
/// against the raw HTML finds nothing. Every assertion about visible text
/// therefore goes through <see cref="Text"/>.
/// </para>
/// </summary>
public static class Html
{
    public static string Text(string html) => WebUtility.HtmlDecode(html);

    /// <summary>The body of the response, ready to be compared against visible text.</summary>
    public static async Task<string> ReadPageAsync(this HttpResponseMessage response) =>
        Text(await response.Content.ReadAsStringAsync());

    /// <summary>
    /// The error messages the server rendered, taken from the fields
    /// (<c>field-validation-error</c>) and from the summary
    /// (<c>validation-summary-errors</c>).
    /// <para>
    /// Searching the whole page does not work: the pages also carry
    /// <c>window.ValidationMessages</c> — the same texts, but as templates for
    /// the script. A test that counts those as errors passes every time.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> ValidationErrors(string html)
    {
        var fields = Regex.Matches(html,
                "<span[^>]*class=\"[^\"]*field-validation-error[^\"]*\"[^>]*>(?<msg>.*?)</span>",
                RegexOptions.Singleline)
            .Select(m => m.Groups["msg"].Value);

        var summaries = Regex.Matches(html,
                "<div[^>]*class=\"[^\"]*validation-summary-errors[^\"]*\"[^>]*>(?<body>.*?)</div>",
                RegexOptions.Singleline)
            .SelectMany(m => Regex.Matches(m.Groups["body"].Value, "<li[^>]*>(?<msg>.*?)</li>",
                                           RegexOptions.Singleline)
                .Select(li => li.Groups["msg"].Value));

        return fields.Concat(summaries)
            .Select(msg => Text(Regex.Replace(msg, "<[^>]+>", string.Empty)).Trim())
            .Where(msg => msg.Length > 0)
            .ToList();
    }

    /// <summary>The error messages taken from the response.</summary>
    public static async Task<IReadOnlyList<string>> ValidationErrorsAsync(this HttpResponseMessage response) =>
        ValidationErrors(await response.Content.ReadAsStringAsync());
}
