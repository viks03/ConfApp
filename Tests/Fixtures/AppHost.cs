// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace ConferenceApp.Tests.Fixtures;

/// <summary>
/// Starts the real application — <c>Program.cs</c>, the whole pipeline, Kestrel
/// on a free port — inside the test process.
/// <para>
/// Why in the same process rather than a separate one: <c>StripeConfiguration</c>
/// is static in the Stripe SDK, and the only way to point the API at a local
/// stub without touching the application's code is to set it from the same
/// process. Kestrel is real rather than <c>TestServer</c> because Playwright
/// needs an address a browser can go to, and because the webhook tests then
/// pass through the rate and size limits that <c>TestServer</c> does not
/// enforce.
/// </para>
/// <para>
/// The settings are passed as command-line arguments: those have the highest
/// precedence in <c>CreateBuilder</c>, so they override even the user secrets
/// holding the live keys.
/// </para>
/// </summary>
public sealed class AppHost
{
    private Thread? _thread;

    public int    Port    { get; private set; }
    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>
    /// Claims the port up front, so that <c>AppSettings:BaseUrl</c> can point at
    /// the application before the application has started.
    /// </summary>
    public void Reserve()
    {
        if (Port != 0) return;
        Port    = FreePort();
        BaseUrl = $"http://127.0.0.1:{Port}";
    }

    /// <summary>
    /// Starts the application and returns the startup error instead of throwing
    /// it. Part 1 asserts on exactly that: whether the application stops on a
    /// bad path, and whether it names the path and the setting.
    /// </summary>
    public async Task<Exception?> TryStartAsync(IReadOnlyDictionary<string, string> settings)
    {
        try
        {
            await StartAsync(settings);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    public async Task StartAsync(IReadOnlyDictionary<string, string> settings)
    {
        Reserve();

        var args = new List<string>
        {
            "--urls", BaseUrl,
            "--contentRoot", TestPaths.RepoRoot,
            // The entry point belongs to the TEST assembly, while Razor Pages are
            // looked for in the assembly named by applicationName. Without this
            // line every page returns 404.
            "--applicationName", "ConferenceApp",
            // Development, because otherwise the Identity cookie is issued with
            // Secure=true and is not sent back over http, so no test can sign in.
            "--environment", "Development"
        };

        args.AddRange(settings.Select(kv => $"--{kv.Key}={kv.Value}"));

        // Relative paths in the application (Data Source, CHANGELOG.md) are
        // resolved against the working directory, which for the test process is
        // Tests/bin/...
        Directory.SetCurrentDirectory(TestPaths.RepoRoot);

        var entryPoint = typeof(ConferenceApp.Data.ApplicationDbContext).Assembly.EntryPoint
            ?? throw new InvalidOperationException("Сглобката на приложението няма входна точка.");

        Exception? startupFailure = null;

        _thread = new Thread(() =>
        {
            try
            {
                entryPoint.Invoke(null, new object?[] { args.ToArray() });
            }
            catch (TargetInvocationException ex)
            {
                startupFailure = ex.InnerException ?? ex;
            }
            catch (Exception ex)
            {
                startupFailure = ex;
            }
        })
        {
            IsBackground = true,
            Name = "conferenceapp-under-test"
        };

        _thread.Start();

        await WaitUntilListeningAsync(() => startupFailure, StartupTimeout);
    }

    /// <summary>How long to wait for the application to answer. Short when a failure is expected.</summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(90);

    private async Task WaitUntilListeningAsync(Func<Exception?> failure, TimeSpan timeout)
    {
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.Add(timeout);

        while (DateTime.UtcNow < deadline)
        {
            if (failure() is { } ex)
                throw new InvalidOperationException(
                    "Приложението не тръгна: " + ex.Message, ex);

            try
            {
                // /Error is the cheapest page that does not require signing in.
                var response = await probe.GetAsync(BaseUrl + "/Error");
                if ((int)response.StatusCode < 500) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Приложението не отговори на {BaseUrl} до {timeout.TotalSeconds:F0} секунди. " +
            "Виж конзолата за грешката при старт.");
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
