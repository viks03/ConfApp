// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ConferenceApp.Tests.Fixtures;

/// <summary>A captured message: as much of it as it takes to check what went out.</summary>
public sealed record CapturedMail(string To, string Subject, string Body, DateTime ReceivedAt);

/// <summary>
/// An SMTP sink on loopback. It exists for one reason: the tests do NOT send
/// real mail. The application is configured to this port, so nothing leaves the
/// machine and the test can still see what was sent.
/// </summary>
public sealed class SmtpSink : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<CapturedMail> _mail = new();
    private Task? _loop;

    public int Port { get; }

    public SmtpSink()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public void Start() => _loop = Task.Run(AcceptLoopAsync);

    public IReadOnlyCollection<CapturedMail> All => _mail.ToArray();

    public void Clear() => _mail.Clear();

    // ── Deliberate rejection ────────────────────────────────────────────
    // A failed message is retried three times (MailComposer.MaxAttempts) before
    // it is given up on. Without a sink that rejects, there is no way to check
    // that at all short of stopping SMTP for the whole suite.
    //
    // The rejection is PER ADDRESS deliberately: the suite runs against one
    // shared instance of the application, and another test's mail must not
    // suffer for it.

    private readonly ConcurrentDictionary<string, int> _failuresLeft = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _attempts     = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Rejects the next <paramref name="times"/> messages to this address.</summary>
    public void FailNextFor(string address, int times) => _failuresLeft[address] = times;

    /// <summary>Rejects every message to this address until it is stopped explicitly.</summary>
    public void AlwaysFailFor(string address) => _failuresLeft[address] = int.MaxValue;

    public void StopFailingFor(string address) => _failuresLeft.TryRemove(address, out _);

    /// <summary>How many times the application GOT AS FAR AS submitting a message to this address.</summary>
    public int AttemptsFor(string address) => _attempts.TryGetValue(address, out var n) ? n : 0;

    /// <summary>Waits for at least that many attempts; the retries are 3 and 15 seconds apart.</summary>
    public async Task<int> WaitForAttemptsAsync(string address, int atLeast, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var seen = AttemptsFor(address);
            if (seen >= atLeast) return seen;
            await Task.Delay(200);
        }

        return AttemptsFor(address);
    }

    // ── Deliberate slowness ─────────────────────────────────────────────
    // For the question of whether the request waits on the mail. A sink that
    // answers instantly cannot tell sending inside the request apart from
    // sending on a background queue.

    private readonly ConcurrentDictionary<string, TimeSpan> _delays = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Delays the answer to <c>DATA</c> for messages to this address.</summary>
    public void DelayFor(string address, TimeSpan delay) => _delays[address] = delay;

    public void StopDelayingFor(string address) => _delays.TryRemove(address, out _);

    private TimeSpan DelayFor(List<string> recipients)
    {
        var longest = TimeSpan.Zero;

        foreach (var recipient in recipients)
            if (_delays.TryGetValue(recipient, out var delay) && delay > longest)
                longest = delay;

        return longest;
    }

    /// <summary>
    /// Decides whether the message to any of the recipients is rejected, and
    /// counts the attempt as spent. The rejection comes at the end of DATA
    /// deliberately: rejecting at RCPT TO produces an
    /// <c>SmtpFailedRecipientException</c>, which MailComposer does NOT retry —
    /// an address that does not exist will not start existing on the tenth
    /// attempt.
    /// </summary>
    private bool ShouldReject(List<string> recipients)
    {
        var reject = false;

        foreach (var recipient in recipients)
        {
            if (!_failuresLeft.TryGetValue(recipient, out var left) || left <= 0) continue;

            reject = true;
            if (left != int.MaxValue) _failuresLeft[recipient] = left - 1;
        }

        return reject;
    }

    /// <summary>
    /// A payment message, confirmed or pending, rather than the sign-in code.
    /// Every test signs in with a code, so there is always one of those to the
    /// same address as well.
    /// </summary>
    public Task<CapturedMail?> WaitForPaymentMailAsync(string toContains, TimeSpan timeout) =>
        WaitForAsync(toContains, timeout, IsPaymentMail);

    /// <summary>Recognises a payment message by its subject, in either language.</summary>
    public static bool IsPaymentMail(CapturedMail mail) =>
        mail.Subject.Contains("плащан", StringComparison.OrdinalIgnoreCase)
        || mail.Subject.Contains("payment", StringComparison.OrdinalIgnoreCase);

    /// <summary>Waits for a message to a given address. No message is a result too.</summary>
    public async Task<CapturedMail?> WaitForAsync(
        string toContains, TimeSpan timeout, Func<CapturedMail, bool>? extra = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var hit = _mail.FirstOrDefault(m =>
                m.To.Contains(toContains, StringComparison.OrdinalIgnoreCase)
                && (extra == null || extra(m)));

            if (hit != null) return hit;
            await Task.Delay(100);
        }

        return null;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException)    { return; }

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };

                await writer.WriteLineAsync("220 confapp-test-sink ESMTP");

                var recipients = new List<string>();

                while (await reader.ReadLineAsync() is { } line)
                {
                    var upper = line.ToUpperInvariant();

                    if (upper.StartsWith("EHLO") || upper.StartsWith("HELO"))
                    {
                        // AUTH and STARTTLS are deliberately NOT advertised: SmtpClient
                        // then makes no attempt to authenticate, and the password from
                        // the configuration never travels over the network.
                        await writer.WriteLineAsync("250-confapp-test-sink");
                        await writer.WriteLineAsync("250 8BITMIME");
                    }
                    else if (upper.StartsWith("MAIL FROM"))
                    {
                        await writer.WriteLineAsync("250 OK");
                    }
                    else if (upper.StartsWith("RCPT TO"))
                    {
                        recipients.Add(ExtractAddress(line));
                        await writer.WriteLineAsync("250 OK");
                    }
                    else if (upper.StartsWith("DATA"))
                    {
                        await writer.WriteLineAsync("354 End data with <CRLF>.<CRLF>");

                        var raw = new StringBuilder();
                        while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                            raw.AppendLine(dataLine);

                        foreach (var recipient in recipients)
                            _attempts.AddOrUpdate(recipient, 1, (_, n) => n + 1);

                        var delay = DelayFor(recipients);
                        if (delay > TimeSpan.Zero) await Task.Delay(delay);

                        if (ShouldReject(recipients))
                        {
                            // 4xx is a temporary failure, which is exactly the one a
                            // retry is expected for.
                            await writer.WriteLineAsync("451 4.3.0 Приемникът отказва нарочно (тест).");
                        }
                        else
                        {
                            Capture(recipients, raw.ToString());
                            await writer.WriteLineAsync("250 Queued");
                        }
                    }
                    else if (upper.StartsWith("QUIT"))
                    {
                        await writer.WriteLineAsync("221 Bye");
                        return;
                    }
                    else if (upper.StartsWith("RSET"))
                    {
                        recipients.Clear();
                        await writer.WriteLineAsync("250 OK");
                    }
                    else
                    {
                        await writer.WriteLineAsync("250 OK");
                    }
                }
            }
            catch (IOException)     { /* the client hung up, which is normal */ }
            catch (ObjectDisposedException) { }
        }
    }

    private void Capture(List<string> recipients, string raw)
    {
        var (headers, body) = SplitHeaders(raw);

        var subject = DecodeHeader(HeaderValue(headers, "Subject"));
        var to      = recipients.Count > 0
                        ? string.Join(", ", recipients)
                        : DecodeHeader(HeaderValue(headers, "To"));

        _mail.Enqueue(new CapturedMail(to, subject, DecodeMime(headers, body), DateTime.UtcNow));
    }

    private static string ExtractAddress(string line)
    {
        var open = line.IndexOf('<');
        var close = line.IndexOf('>');
        return open >= 0 && close > open ? line[(open + 1)..close] : line;
    }

    /// <summary>Splits the headers from the body and joins up folded lines.</summary>
    private static (List<string> Headers, string Body) SplitHeaders(string raw)
    {
        var lines = raw.Replace("\r\n", "\n").Split('\n');
        var headers = new List<string>();
        var i = 0;

        for (; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) { i++; break; }

            // A folded header line starts with a space or a tab.
            if (headers.Count > 0 && (lines[i].StartsWith(' ') || lines[i].StartsWith('\t')))
                headers[^1] += lines[i].TrimStart();
            else
                headers.Add(lines[i]);
        }

        return (headers, string.Join("\n", lines.Skip(i)));
    }

    private static string HeaderValue(List<string> headers, string name)
    {
        var prefix = name + ":";
        var hit = headers.FirstOrDefault(h => h.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return hit == null ? string.Empty : hit[prefix.Length..].Trim();
    }

    /// <summary>
    /// Unpacks the MIME message into a single string. The application's mail is
    /// multipart/alternative with a part in quoted-printable or base64, and
    /// without this neither an amount nor a code is visible in the body.
    /// </summary>
    private static string DecodeMime(List<string> headers, string body)
    {
        var contentType = HeaderValue(headers, "Content-Type");
        var boundary = Boundary(contentType);

        if (boundary == null)
            return DecodePart(HeaderValue(headers, "Content-Transfer-Encoding"), body, contentType);

        var result = new StringBuilder();

        foreach (var chunk in body.Split("--" + boundary))
        {
            var trimmed = chunk.Trim('\n', '\r', '-', ' ');
            if (trimmed.Length == 0) continue;

            var (partHeaders, partBody) = SplitHeaders(chunk.TrimStart('\n', '\r'));
            var partType = HeaderValue(partHeaders, "Content-Type");
            var nested = Boundary(partType);

            result.AppendLine(nested != null
                ? DecodeMime(partHeaders, partBody)
                : DecodePart(HeaderValue(partHeaders, "Content-Transfer-Encoding"), partBody, partType));
        }

        return result.ToString();
    }

    private static string? Boundary(string contentType)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            contentType, @"boundary=""?(?<b>[^"";]+)""?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success ? match.Groups["b"].Value.Trim() : null;
    }

    private static string DecodePart(string transferEncoding, string body, string contentType)
    {
        var encoding = Charset(contentType);

        if (transferEncoding.Contains("base64", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var cleaned = System.Text.RegularExpressions.Regex.Replace(body, @"\s", string.Empty);
                return encoding.GetString(Convert.FromBase64String(cleaned));
            }
            catch (FormatException) { return body; }
        }

        if (transferEncoding.Contains("quoted-printable", StringComparison.OrdinalIgnoreCase))
            return DecodeQuotedPrintable(body.Replace("\n", "\r\n"), encoding);

        return body;
    }

    private static Encoding Charset(string contentType)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            contentType, @"charset=""?(?<c>[^"";\s]+)""?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success) return Encoding.UTF8;

        try { return Encoding.GetEncoding(match.Groups["c"].Value); }
        catch (ArgumentException) { return Encoding.UTF8; }
    }

    /// <summary>RFC 2047 (=?utf-8?B?...?=), without which Cyrillic in a subject is unreadable.</summary>
    private static string DecodeHeader(string value)
    {
        if (!value.Contains("=?")) return value;

        var result = new StringBuilder();
        var rest = value;

        while (rest.Contains("=?"))
        {
            var start = rest.IndexOf("=?", StringComparison.Ordinal);
            result.Append(rest[..start]);

            var end = rest.IndexOf("?=", start + 2, StringComparison.Ordinal);
            if (end < 0) break;

            var token = rest[(start + 2)..end];
            rest = rest[(end + 2)..];

            var parts = token.Split('?');
            if (parts.Length < 3) continue;

            Encoding enc;
            try { enc = Encoding.GetEncoding(parts[0]); }
            catch (ArgumentException) { enc = Encoding.UTF8; }

            var payload = string.Join('?', parts.Skip(2));

            try
            {
                result.Append(parts[1].ToUpperInvariant() == "B"
                    ? enc.GetString(Convert.FromBase64String(payload))
                    : DecodeQuotedPrintable(payload.Replace('_', ' '), enc));
            }
            catch (FormatException) { result.Append(payload); }
        }

        return result + rest;
    }

    private static string DecodeQuotedPrintable(string value, Encoding encoding)
    {
        var bytes = new List<byte>(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '=' && i + 2 < value.Length
                && Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
            {
                bytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16));
                i += 2;
            }
            else if (value[i] == '=' && i + 1 < value.Length && value[i + 1] == '\r')
            {
                i += 2;                                  // a soft line break
            }
            else
            {
                bytes.Add((byte)value[i]);
            }
        }

        return encoding.GetString(bytes.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        if (_loop != null)
        {
            try { await _loop; } catch (OperationCanceledException) { }
        }
        _cts.Dispose();
    }
}
