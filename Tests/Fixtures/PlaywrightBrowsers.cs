// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Tests.Fixtures;

/// <summary>
/// Playwright needs a browser downloaded before it can start one. So that a
/// single command is enough to run the suite, the suite downloads it itself
/// when it is missing.
/// </summary>
public static class PlaywrightBrowsers
{
    private static bool _done;
    private static readonly object _gate = new();

    public static void EnsureInstalled()
    {
        lock (_gate)
        {
            if (_done) return;

            var exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
            if (exitCode != 0)
                throw new InvalidOperationException(
                    $"Свалянето на Chromium за Playwright се провали (код {exitCode}). " +
                    "Пусни на ръка: pwsh Tests/bin/Debug/net9.0/playwright.ps1 install chromium");

            _done = true;
        }
    }
}
