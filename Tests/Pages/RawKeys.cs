// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using System.Text.RegularExpressions;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Pages;

/// <summary>
/// Looks for a raw resource key in the visible text of a page: <c>NAV_HOME</c>,
/// <c>Home_HeroTitle</c> and the like.
/// <para>
/// This is the commonest way for a page to look broken without anything blowing
/// up: <c>IViewLocalizer</c> returns <b>the key itself</b> when
/// <c>Resources/*.resx</c> does not have it. The response is 200, the console is
/// clean, and the visitor reads "Home_HeroTitle".
/// </para>
/// <para>
/// Two things are therefore looked at: a key that really does appear in some
/// resx file, where there is no doubt, and a token that merely looks like one
/// but belongs to a known family (<c>Nav_</c>, <c>Footer_</c>, …). The second
/// catches the key that is mistyped in the view and exists in no resx at all.
/// </para>
/// </summary>
internal static class RawKeys
{
    private static readonly Regex Scripts =
        new(@"<script\b[^>]*>.*?</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex Styles =
        new(@"<style\b[^>]*>.*?</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex Comments = new(@"<!--.*?-->", RegexOptions.Singleline);

    /// <summary>The attributes a user sees or hears.</summary>
    private static readonly Regex VisibleAttributes = new(
        @"\b(?:alt|title|placeholder|aria-label|data-label-[a-z]+)\s*=\s*""(?<v>[^""]*)""",
        RegexOptions.IgnoreCase);

    private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Singleline);

    /// <summary>A key starts with a letter, holds at least one underscore and has no spaces.</summary>
    private static readonly Regex KeyShaped = new(@"\b[A-Za-z][A-Za-z0-9]*(?:_[A-Za-z0-9]+)+\b");

    private static readonly Lazy<HashSet<string>> KnownKeys = new(LoadKeys);
    private static readonly Lazy<HashSet<string>> KnownFamilies = new(() =>
        KnownKeys.Value
            .Select(k => k[..k.IndexOf('_')])
            .ToHashSet(StringComparer.Ordinal));

    /// <summary>Tokens that look like a key but are ordinary visible text.</summary>
    private static readonly HashSet<string> NotKeys = new(StringComparer.Ordinal);

    /// <summary>The keys found in the visible text of the page.</summary>
    public static IReadOnlyList<string> In(string html)
    {
        var text = VisibleText(html);

        return KeyShaped.Matches(text)
            .Select(m => m.Value)
            .Where(token => !NotKeys.Contains(token))
            .Where(IsResourceKey)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsResourceKey(string token)
    {
        if (KnownKeys.Value.Contains(token)) return true;

        // A key that is in no resx at all is recognised by its family and by being
        // a single word_word token with no spaces.
        var family = token[..token.IndexOf('_')];
        return KnownFamilies.Value.Contains(family);
    }

    /// <summary>What a person actually sees: no scripts, no styles, no tags.</summary>
    public static string VisibleText(string html)
    {
        var body = Comments.Replace(Styles.Replace(Scripts.Replace(html, " "), " "), " ");

        var attributes = string.Join(" ",
            VisibleAttributes.Matches(body).Select(m => m.Groups["v"].Value));

        return WebUtility.HtmlDecode(Tags.Replace(body, " ") + " " + attributes);
    }

    private static HashSet<string> LoadKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var folder = Path.Combine(TestPaths.RepoRoot, "Resources");

        foreach (var file in Directory.GetFiles(folder, "*.resx"))
        foreach (var name in System.Xml.Linq.XDocument.Load(file).Root!
                     .Elements("data")
                     .Select(d => d.Attribute("name")?.Value)
                     .Where(n => !string.IsNullOrEmpty(n) && n!.Contains('_')))
            keys.Add(name!);

        if (keys.Count == 0)
            throw new InvalidOperationException($"В {folder} не намерих нито един ключ.");

        return keys;
    }
}
