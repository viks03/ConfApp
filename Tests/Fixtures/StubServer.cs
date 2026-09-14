// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using System.Text;

namespace ConferenceApp.Tests.Fixtures;

/// <summary>
/// The common base of the two stubs, Go28 and Stripe: an HttpListener on a free
/// loopback port, with a simple router.
/// </summary>
public abstract class StubServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public int Port { get; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";

    /// <summary>Every request the stub received, for assertions about whether it is called at all.</summary>
    public List<string> RequestLog { get; } = new();

    protected StubServer()
    {
        Port = FreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
    }

    public void Start()
    {
        _listener.Start();
        _loop = Task.Run(AcceptLoopAsync);
    }

    protected abstract Task HandleAsync(HttpListenerContext ctx, string method, string path);

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (HttpListenerException) { return; }
            catch (ObjectDisposedException) { return; }

            _ = Task.Run(async () =>
            {
                var method = ctx.Request.HttpMethod;
                var path   = ctx.Request.Url?.AbsolutePath ?? "/";

                lock (RequestLog) RequestLog.Add($"{method} {path}");

                try
                {
                    await HandleAsync(ctx, method, path);
                }
                catch (Exception ex)
                {
                    try
                    {
                        ctx.Response.StatusCode = 500;
                        await WriteAsync(ctx, "text/plain", "stub error: " + ex.Message);
                    }
                    catch (Exception) { /* the connection is already closed */ }
                }
                finally
                {
                    try { ctx.Response.Close(); } catch (Exception) { }
                }
            });
        }
    }

    protected static async Task<string> ReadBodyAsync(HttpListenerContext ctx)
    {
        using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    protected static async Task WriteJsonAsync(HttpListenerContext ctx, string json, int status = 200)
    {
        ctx.Response.StatusCode = status;
        await WriteAsync(ctx, "application/json", json);
    }

    protected static async Task WriteAsync(HttpListenerContext ctx, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
    }

    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _listener.Stop();  } catch (Exception) { }
        try { _listener.Close(); } catch (Exception) { }
        if (_loop != null)
        {
            try { await _loop; } catch (Exception) { }
        }
        _cts.Dispose();
    }
}
