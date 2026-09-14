// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text.Json;

namespace ConferenceApp.Tests.Fixtures;

/// <summary>
/// The credentials the tests sign in with. The file is kept out of git (see
/// .gitignore) and nothing from it reaches a log, a failure message or a
/// commit.
/// </summary>
public sealed class TestCredentials
{
    private static readonly Lazy<TestCredentials> _instance = new(Load);

    public static TestCredentials Current => _instance.Value;

    public string AdminEmail    { get; private init; } = string.Empty;
    public string AdminPassword { get; private init; } = string.Empty;

    /// <summary>A participant signs in with a one-time code rather than a password, hence no password here.</summary>
    public string UserEmail     { get; private init; } = string.Empty;

    public string TestCard      { get; private init; } = "4242424242424242";

    private static TestCredentials Load()
    {
        var path = TestPaths.CredentialsFile;

        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"Липсва {Path.GetFileName(path)} в корена на проекта ({TestPaths.RepoRoot}). " +
                "Без него тестовете не могат да влязат никъде. Не продължавам с празни стойности.");

        // The check that the file cannot reach git is deliberately here: a missing
        // line in .gitignore costs more than a failing test.
        AssertGitIgnored(path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        var admin = Require(root, "admin");
        var user  = Require(root, "user");

        var creds = new TestCredentials
        {
            AdminEmail    = RequireString(admin, "email",    "admin.email"),
            AdminPassword = RequireString(admin, "password", "admin.password"),
            UserEmail     = RequireString(user,  "email",    "user.email"),
            TestCard      = root.TryGetProperty("stripe", out var stripe)
                            && stripe.TryGetProperty("testCard", out var card)
                            && card.ValueKind == JsonValueKind.String
                                ? card.GetString()!.Replace(" ", string.Empty)
                                : "4242424242424242"
        };

        return creds;
    }

    private static void AssertGitIgnored(string path)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", "check-ignore -q " + Path.GetFileName(path))
            {
                WorkingDirectory = TestPaths.RepoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError  = true
            };

            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return;          // no git, which is no reason to stop
            p.WaitForExit(5000);

            if (p.ExitCode != 0)
                throw new InvalidOperationException(
                    $"{Path.GetFileName(path)} НЕ е в .gitignore. Добави го, преди да пуснеш тестовете — " +
                    "файлът съдържа пароли.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // git is not installed, so the check falls away; the file is still required.
        }
    }

    private static JsonElement Require(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el)
            ? el
            : throw new InvalidOperationException(
                $"test-credentials.local.json: липсва секция \"{name}\".");

    private static string RequireString(JsonElement parent, string name, string label)
    {
        if (!parent.TryGetProperty(name, out var el)
            || el.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(el.GetString()))
        {
            throw new InvalidOperationException(
                $"test-credentials.local.json: \"{label}\" е празно или липсва. " +
                "Не продължавам с празна стойност.");
        }

        return el.GetString()!;
    }
}
