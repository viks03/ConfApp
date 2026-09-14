// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Services.Theming;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Theming;

/// <summary>One built-in theme: its key and the values it promises.</summary>
public sealed record BuiltInTheme(string Key, string Name, IReadOnlyDictionary<string, string> Tokens)
{
    /// <summary>
    /// The value the page is supposed to show. A missing token is NOT an error: a
    /// theme that does not set one leaves the value from <c>mainStyle.css</c> in
    /// place, which is the same one <c>ThemeTokens</c> holds as the default.
    /// </summary>
    public string Token(string name) =>
        Tokens.TryGetValue(name, out var value)
            ? value
            : ThemeTokens.All.FirstOrDefault(s => s.Name == name)?.Default
              ?? throw new InvalidOperationException($"Няма токен {name} в ThemeTokens.All.");

    /// <summary>
    /// A light theme, recognised by <c>--logo-invert</c>: the logo is a white PNG
    /// and 0 means "make it black", which is only ever done on a light
    /// background.
    /// </summary>
    public bool IsLight => Token("--logo-invert") == "0";

    public override string ToString() => $"{Key} — {Name}";
}

/// <summary>
/// The catalogue of part 9: the built-in themes, read from <c>wwwroot/themes</c>
/// — the same files the admin panel seeds on first opening.
/// <para>
/// The brief speaks of "the nine themes". There are eleven files, and that is
/// exactly why the catalogue is read from the folder rather than copied in here:
/// a new theme joins every check in this part as soon as its file is dropped in,
/// and a deleted one drops out.
/// </para>
/// </summary>
public static class ThemeCatalog
{
    private static IReadOnlyList<BuiltInTheme>? _all;

    public static IReadOnlyList<BuiltInTheme> All => _all ??= Load();

    /// <summary>
    /// The three pages each theme is opened on. They are not arbitrary: three
    /// different stylesheets — the home page (indexStyle), a list
    /// (lecturersStyle) and a form (authStyle). A theme that looks right only on
    /// the home page has restyled half a site.
    /// </summary>
    public static readonly string[] ThreePages = { "/", "/Lecturers", "/Register" };

    public static BuiltInTheme Of(string key) =>
        All.FirstOrDefault(t => t.Key == key)
        ?? throw new InvalidOperationException($"В wwwroot/themes няма {key}.json.");

    /// <summary>Every theme once.</summary>
    public static IEnumerable<object[]> Each() =>
        All.Select(t => new object[] { t.Key });

    /// <summary>The light ones only, for the logo inversion.</summary>
    public static IEnumerable<object[]> EachLight() =>
        All.Where(t => t.IsLight).Select(t => new object[] { t.Key });

    private static IReadOnlyList<BuiltInTheme> Load()
    {
        var dir = Path.Combine(TestPaths.RepoRoot, "wwwroot", "themes");

        if (!Directory.Exists(dir))
            throw new InvalidOperationException($"Няма папка с теми: {dir}.");

        var themes = new List<BuiltInTheme>();

        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f))
        {
            var key  = Path.GetFileNameWithoutExtension(file);
            var json = File.ReadAllText(file);

            // The same check the admin panel makes while seeding. A broken file
            // there simply never reaches the database; here it has to be seen.
            var check = ThemeTokens.Parse(json);
            Assert.True(check.Ok,
                $"wwwroot/themes/{key}.json не минава ThemeTokens.Parse: " +
                string.Join("; ", check.Errors));

            var name = key;
            using (var doc = System.Text.Json.JsonDocument.Parse(json))
                if (doc.RootElement.TryGetProperty("name", out var n))
                    name = n.GetString() ?? key;

            themes.Add(new BuiltInTheme(key, name, check.Values));
        }

        if (themes.Count == 0)
            throw new InvalidOperationException($"В {dir} няма нито един файл с тема.");

        return themes;
    }
}
