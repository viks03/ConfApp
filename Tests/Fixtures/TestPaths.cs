// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Tests.Fixtures;

/// <summary>
/// The paths everything else starts from. The root is found by looking for the
/// application's .csproj rather than by a path relative to the build output, so
/// that the tests work under a different build configuration as well.
/// </summary>
public static class TestPaths
{
    private static string? _root;

    /// <summary>The root of the repository: the folder holding ConferenceApp.csproj.</summary>
    public static string RepoRoot => _root ??= FindRoot();

    /// <summary>The copy of the database the tests recreate on every run.</summary>
    public static string TestDbFile => Path.Combine(RepoRoot, "App_Data", "test.db");

    /// <summary>The real database. The tests have no business with it; it is kept only for comparison.</summary>
    public static string LiveDbFile => Path.Combine(RepoRoot, "conferenceapp.db");

    public static string CredentialsFile => Path.Combine(RepoRoot, "test-credentials.local.json");

    /// <summary>A folder for everything a test creates that must not be left in the repository.</summary>
    public static string RunScratch { get; } = Path.Combine(
        Path.GetTempPath(), "confapp-tests", DateTime.Now.ToString("yyyyMMdd-HHmmss"));

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ConferenceApp.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Не намирам корена на проекта (папката с ConferenceApp.csproj) над " +
            AppContext.BaseDirectory + ".");
    }
}
