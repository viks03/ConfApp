// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Collections.Concurrent;
using System.Globalization;
using System.Xml.Linq;

namespace ConferenceApp.Tests.Fixtures;

/// <summary>
/// Reads the messages straight from <c>Resources/*.resx</c>.
/// <para>
/// A test has to check the text the user actually sees, but a string copied
/// into the test only checks that both copies were made the same way. The key
/// is therefore what is asserted: changing a translation does not break the
/// test, deleting a key does.
/// </para>
/// </summary>
public static class Resx
{
    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> _files = new();

    /// <summary>The value of a key, for example <c>Value("Pages.Login", "Error_UserNotFound")</c>.</summary>
    public static string Value(string baseName, string key, string culture = "bg")
    {
        var map = _files.GetOrAdd($"{baseName}.{culture}", Load);

        return map.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException(
                $"В Resources/{baseName}.{culture}.resx няма ключ \"{key}\".");
    }

    /// <summary>A value with {0} filled in, the way the page composes it.</summary>
    public static string Format(string baseName, string key, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, Value(baseName, key), args);

    /// <summary>
    /// The part before the first placeholder, for messages whose ending depends
    /// on a counter the test would rather not predict.
    /// </summary>
    public static string Prefix(string baseName, string key)
    {
        var value = Value(baseName, key);
        var at = value.IndexOf("{0}", StringComparison.Ordinal);
        return at < 0 ? value : value[..at];
    }

    private static Dictionary<string, string> Load(string fileKey)
    {
        var path = Path.Combine(TestPaths.RepoRoot, "Resources", fileKey + ".resx");

        if (!File.Exists(path))
            throw new InvalidOperationException($"Няма файл {path}.");

        return XDocument.Load(path).Root!
            .Elements("data")
            .Where(d => d.Attribute("name") != null)
            .ToDictionary(
                d => d.Attribute("name")!.Value,
                d => d.Element("value")?.Value ?? string.Empty);
    }
}
