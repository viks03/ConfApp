// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Services;

/// <summary>
/// The application as a <b>separate process</b>: the only way to see what it does
/// when it really shuts down.
/// <para>
/// The other parts start it inside the test process (<see cref="AppHost"/>),
/// because the Stripe SDK needs a static setting made from the same process.
/// There, though, the application never stops: <c>app.Run()</c> holds the thread
/// until the end of the suite and <c>StopAsync</c> on the background services is
/// never called once. That method is exactly the subject of [E-01], draining the
/// queue before shutdown.
/// </para>
/// <para>
/// So here <c>bin/…/ConferenceApp.dll</c> is launched as a process, sent
/// <c>SIGTERM</c> — the same signal systemd sends on a restart — and what
/// reached the mailbox before the process went away is what is inspected.
/// </para>
/// </summary>
public sealed class AppProcess : IAsyncDisposable
{
    private Process? _process;
    private readonly StringBuilder _stdout = new();

    public string Scratch { get; }
    public string DbFile  { get; }
    public string WebRoot { get; }
    public string LogDir  { get; }
    public int    Port    { get; }
    public string BaseUrl { get; }

    public string Output { get { lock (_stdout) return _stdout.ToString(); } }

    private AppProcess(string name)
    {
        Scratch = Path.Combine(TestPaths.RunScratch, "process-" + name);
        DbFile  = Path.Combine(Scratch, "app.db");
        WebRoot = Path.Combine(Scratch, "wwwroot");
        LogDir  = Path.Combine(Scratch, "logs");

        Directory.CreateDirectory(Scratch);
        Directory.CreateDirectory(WebRoot);
        Directory.CreateDirectory(LogDir);

        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        Port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        BaseUrl = $"http://127.0.0.1:{Port}";
    }

    /// <summary>
    /// The application's assembly as <c>dotnet test</c> built it for the solution.
    /// It is looked for explicitly, because the copy sitting next to the tests has
    /// no <c>runtimeconfig.json</c> of its own and cannot be launched as a
    /// process.
    /// </summary>
    public static string FindAssembly()
    {
        var bin = Path.Combine(TestPaths.RepoRoot, "bin");

        var candidates = Directory.Exists(bin)
            ? Directory.GetFiles(bin, "ConferenceApp.dll", SearchOption.AllDirectories)
                .Where(p => File.Exists(Path.ChangeExtension(p, ".runtimeconfig.json")))
                .OrderByDescending(p => new FileInfo(p).LastWriteTimeUtc)
                .ToList()
            : new List<string>();

        if (candidates.Count == 0)
            throw new InvalidOperationException(
                $"Няма построена сглобка на приложението под {bin}. " +
                "Пусни `dotnet build ConferenceApp.slnx` преди тестовете.");

        return candidates[0];
    }

    /// <param name="prepare">
    /// Called AFTER the folders and the port are ready and BEFORE the process
    /// starts: this is where the database the startup should find is prepared.
    /// </param>
    public static async Task<AppProcess> StartAsync(
        string name,
        IReadOnlyDictionary<string, string> settings,
        Func<AppProcess, Task>? prepare = null)
    {
        var app = new AppProcess(name);
        app.CopyTemplates();

        if (prepare != null) await prepare(app);

        var arguments = new List<string>
        {
            "exec", FindAssembly(),
            "--urls", app.BaseUrl,
            "--environment", "Development",
            $"--webroot={app.WebRoot}",
            $"--ConnectionStrings:DefaultConnection=Data Source={app.DbFile}",
            $"--{ConferenceApp.Services.DatabaseLocation.OverrideKey}={app.DbFile}",
            $"--Uploads:PrivateRoot={Path.Combine(app.Scratch, "private")}",
            $"--DataProtection:KeysPath={Path.Combine(app.Scratch, "dp-keys")}",
            "--Logging:File:Enabled=true",
            $"--Logging:File:Path={app.LogDir}",
            "--Logging:File:MinimumLevel=Information",
            $"--AppSettings:BaseUrl={app.BaseUrl}",
            "--AppSettings:ForceBaseUrl=1",
            $"--AdminSettings:SystemAdminPassword={TestCredentials.Current.AdminPassword}",
            $"--Stripe:SecretKey={AppFixture.StripeSecretKey}",
            $"--Stripe:PublishableKey={AppFixture.StripePublishable}",
            $"--Stripe:WebhookSecret={AppFixture.StripeWebhookSecret}",
            "--Go28:BaseUrl=http://127.0.0.1:1/api/v1",
            "--Go28:ApiToken=process-token"
        };

        arguments.AddRange(settings.Select(kv => $"--{kv.Key}={kv.Value}"));

        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory       = TestPaths.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        app._process = Process.Start(info)
            ?? throw new InvalidOperationException("`dotnet exec` не тръгна.");

        app._process.OutputDataReceived += app.Capture;
        app._process.ErrorDataReceived  += app.Capture;
        app._process.BeginOutputReadLine();
        app._process.BeginErrorReadLine();

        await app.WaitUntilListeningAsync(TimeSpan.FromSeconds(120));
        return app;
    }

    private void Capture(object sender, DataReceivedEventArgs e)
    {
        if (e.Data == null) return;
        lock (_stdout) _stdout.AppendLine(e.Data);
    }

    /// <summary>The mail templates are read from the webroot, and here that is empty.</summary>
    private void CopyTemplates()
    {
        var source = Path.Combine(TestPaths.RepoRoot, "wwwroot", "templates");
        var target = Path.Combine(WebRoot, "templates");

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private async Task WaitUntilListeningAsync(TimeSpan timeout)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (_process!.HasExited)
                throw new InvalidOperationException(
                    $"Процесът приключи с код {_process.ExitCode}, преди да отговори:\n" + Output);

            try
            {
                var response = await client.GetAsync(BaseUrl + "/Error");
                if ((int)response.StatusCode < 500) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Процесът не отговори на {BaseUrl} за {timeout.TotalSeconds:0} s:\n" + Output);
    }

    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The signal systemd sends on a restart. <c>Process.Kill</c> sends SIGKILL,
    /// under which nothing is drained and the test would be checking nothing.
    /// </summary>
    public void SendSigTerm()
    {
        using var kill = Process.Start(new ProcessStartInfo("/bin/kill")
        {
            ArgumentList = { "-TERM", _process!.Id.ToString() },
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Не можах да пусна /bin/kill.");

        kill.WaitForExit();
    }

    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);

        try
        {
            await _process!.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public string ReadLog()
    {
        if (!Directory.Exists(LogDir)) return string.Empty;

        var text = new StringBuilder();

        foreach (var file in Directory.EnumerateFiles(LogDir))
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                text.AppendLine(reader.ReadToEnd());
            }
            catch (IOException) { }
        }

        return text.ToString();
    }

    public HttpClient NewClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        })
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        client.DefaultRequestHeaders.Add("X-Forwarded-For", HttpSession.NextClientIp());
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            try
            {
                SendSigTerm();
                await WaitForExitAsync(TimeSpan.FromSeconds(30));
            }
            catch { }

            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
            catch { }
        }

        _process?.Dispose();
    }
}
