// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;

namespace ConferenceApp.Tests.Fixtures;

/// <summary>
/// The control server from part A, but local — <c>GET /status</c> and nothing
/// else, because that is the only thing ConfApp ever asks it.
/// <para>
/// Steerable in exactly the ways the specification asks about: the state it
/// reports, a body that cannot be understood, a status code that is not 200,
/// and silence.
/// </para>
/// </summary>
public sealed class ControlStub : StubServer
{
    /// <summary>The secret ConfApp has to send in <c>X-Control-Key</c>.</summary>
    public const string Key = "control-stub-key-not-a-real-one";

    private readonly object _gate = new();

    private bool   _visible   = true;
    private string _mode      = "maintenance";
    private string _messageBg = "Извършваме кратка поддръжка. Ще се върнем скоро.";
    private string _messageEn = "We are performing brief maintenance. We will be back shortly.";

    private string? _rawBody;
    private int     _statusCode = 200;
    private bool    _silent;

    /// <summary>How many times <c>/status</c> has been asked for.</summary>
    public int StatusRequests { get; private set; }

    /// <summary>The value of <c>X-Control-Key</c> on the last request, or null if it carried none.</summary>
    public string? LastKeyHeader { get; private set; }

    /// <summary>How many requests arrived without the right secret.</summary>
    public int UnauthorizedRequests { get; private set; }

    /// <summary>Report the site as visible.</summary>
    public void Show()
    {
        lock (_gate)
        {
            _visible = true;
            _rawBody = null;
        }
    }

    /// <summary>Report the site as hidden, in one of the two modes.</summary>
    public void Hide(string mode = "maintenance")
    {
        lock (_gate)
        {
            _visible = false;
            _mode    = mode;
            _rawBody = null;
        }
    }

    public void SetMessages(string bulgarian, string english)
    {
        lock (_gate)
        {
            _messageBg = bulgarian;
            _messageEn = english;
        }
    }

    /// <summary>Answer with this exact body instead of a state. Null goes back to the state.</summary>
    public void AnswerWith(string? rawBody, int statusCode = 200)
    {
        lock (_gate)
        {
            _rawBody    = rawBody;
            _statusCode = statusCode;
        }
    }

    /// <summary>
    /// Stop answering. The connection is accepted and then dropped, which is
    /// what a VPS that has stopped responding looks like from here — ConfApp
    /// should wait out its timeout and keep what it already knows.
    /// </summary>
    public void GoSilent() { lock (_gate) _silent = true; }

    public void SpeakAgain() { lock (_gate) _silent = false; }

    public void ResetCounters()
    {
        lock (_gate)
        {
            StatusRequests       = 0;
            UnauthorizedRequests = 0;
            LastKeyHeader        = null;
        }
        lock (RequestLog) RequestLog.Clear();
    }

    protected override async Task HandleAsync(HttpListenerContext ctx, string method, string path)
    {
        string? rawBody;
        int     statusCode;
        bool    silent;
        string  body;

        var key = ctx.Request.Headers["X-Control-Key"];

        lock (_gate)
        {
            StatusRequests++;
            LastKeyHeader = key;

            if (!string.Equals(key, Key, StringComparison.Ordinal))
                UnauthorizedRequests++;

            rawBody    = _rawBody;
            statusCode = _statusCode;
            silent     = _silent;

            body =
                $$"""
                {"visible":{{(_visible ? "true" : "false")}},"mode":"{{_mode}}",
                 "messageBg":{{System.Text.Json.JsonSerializer.Serialize(_messageBg)}},
                 "messageEn":{{System.Text.Json.JsonSerializer.Serialize(_messageEn)}},
                 "changedAt":"2026-09-14T02:15:00Z","changedBy":"tests"}
                """;
        }

        if (silent)
        {
            // Neither an answer nor a refusal — the socket simply closes.
            ctx.Response.Abort();
            return;
        }

        if (!string.Equals(method, "GET", StringComparison.Ordinal) ||
            !path.EndsWith("/status", StringComparison.Ordinal))
        {
            ctx.Response.StatusCode = 404;
            await WriteAsync(ctx, "text/plain", "not found");
            return;
        }

        // The real server answers 401 without the secret, and the test that
        // ConfApp sends it rides on exactly that.
        if (!string.Equals(key, Key, StringComparison.Ordinal))
        {
            await WriteJsonAsync(ctx, """{"error":"unauthorized"}""", 401);
            return;
        }

        await WriteJsonAsync(ctx, rawBody ?? body, statusCode);
    }
}
