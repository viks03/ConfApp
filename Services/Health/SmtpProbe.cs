// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace ConferenceApp.Services.Health
{
    /// <summary>
    /// Checks the SMTP connection by speaking the protocol by hand: connect,
    /// EHLO, STARTTLS, AUTH LOGIN, QUIT — <b>without sending a mail</b>.
    ///
    /// <para>
    /// Why by hand rather than through <c>SmtpClient</c>: the application uses
    /// <c>System.Net.Mail.SmtpClient</c>, whose API has no "connect and
    /// authenticate, but do not send". The only way to prove the password is
    /// valid through it is to send a real mail — which is not acceptable for a
    /// health check (the administrator would mail somebody every time they
    /// pressed "Check").
    /// </para>
    ///
    /// <para>
    /// The dialogue therefore runs directly over the socket. It goes exactly as
    /// far as the server's answer to <c>AUTH</c> and ends with <c>QUIT</c>.
    /// Nothing is sent, nothing is created.
    /// </para>
    /// </summary>
    internal static class SmtpProbe
    {
        public sealed record Probe(
            bool Connected,
            bool TlsEstablished,
            bool Authenticated,
            string? ServerGreeting,
            string? FailureCode,
            string? FailureText);

        public static async Task<Probe> RunAsync(
            string host, int port, bool enableSsl,
            string userName, string password,
            CancellationToken ct)
        {
            using var tcp = new TcpClient();

            await tcp.ConnectAsync(host, port, ct);

            using var raw = tcp.GetStream();
            Stream stream = raw;
            var tlsUp = false;

            // Port 465 is implicit TLS — encryption starts immediately.
            // Port 587 (what Office 365 uses) is plaintext until STARTTLS.
            if (port == 465)
            {
                var ssl = new SslStream(raw, leaveInnerStreamOpen: false);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = host,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, ct);
                stream = ssl;
                tlsUp = true;
            }

            var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
            var writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true) { AutoFlush = true };

            // ── Greeting ───────────────────────────────────────────────────
            var greeting = await ReadResponseAsync(reader, ct);
            if (!greeting.StartsWith("220"))
                return new Probe(true, tlsUp, false, greeting, Code(greeting), greeting);

            // ── EHLO ───────────────────────────────────────────────────────
            await writer.WriteAsync("EHLO healthcheck.local\r\n");
            var ehlo = await ReadResponseAsync(reader, ct);
            if (!ehlo.StartsWith("250"))
                return new Probe(true, tlsUp, false, greeting, Code(ehlo), ehlo);

            // ── STARTTLS ───────────────────────────────────────────────────
            if (!tlsUp && enableSsl)
            {
                if (!ehlo.Contains("STARTTLS", StringComparison.OrdinalIgnoreCase))
                    return new Probe(true, false, false, greeting, "no-starttls",
                        "Сървърът не предлага STARTTLS на този порт.");

                await writer.WriteAsync("STARTTLS\r\n");
                var tlsReply = await ReadResponseAsync(reader, ct);
                if (!tlsReply.StartsWith("220"))
                    return new Probe(true, false, false, greeting, Code(tlsReply), tlsReply);

                var ssl = new SslStream(raw, leaveInnerStreamOpen: false);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = host,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, ct);

                stream = ssl;
                tlsUp = true;

                // After STARTTLS the dialogue starts over on the encrypted
                // channel: the reader and writer above are bound to the plain
                // socket and everything announced before TLS is discarded.
                reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true) { AutoFlush = true };

                await writer.WriteAsync("EHLO healthcheck.local\r\n");
                var ehlo2 = await ReadResponseAsync(reader, ct);
                if (!ehlo2.StartsWith("250"))
                    return new Probe(true, true, false, greeting, Code(ehlo2), ehlo2);
            }

            // ── AUTH LOGIN ─────────────────────────────────────────────────
            // This step alone proves the password in the configuration is valid.
            // Without it the check would only say "the server accepts
            // connections".
            await writer.WriteAsync("AUTH LOGIN\r\n");
            var authStart = await ReadResponseAsync(reader, ct);
            if (!authStart.StartsWith("334"))
                return new Probe(true, tlsUp, false, greeting, Code(authStart), authStart);

            await writer.WriteAsync(B64(userName) + "\r\n");
            var userReply = await ReadResponseAsync(reader, ct);
            if (!userReply.StartsWith("334"))
                return new Probe(true, tlsUp, false, greeting, Code(userReply), userReply);

            await writer.WriteAsync(B64(password) + "\r\n");
            var passReply = await ReadResponseAsync(reader, ct);

            var authed = passReply.StartsWith("235");

            // Close politely, so that the server is not left with a dangling
            // session.
            try
            {
                await writer.WriteAsync("QUIT\r\n");
                await ReadResponseAsync(reader, ct);
            }
            catch { /* the close does not affect the result */ }

            return authed
                ? new Probe(true, tlsUp, true, greeting, null, null)
                : new Probe(true, tlsUp, false, greeting, Code(passReply), passReply);
        }

        /// <summary>
        /// Reads one complete SMTP response. A multi-line response (EHLO) has a
        /// dash after the code on every line but the last: "250-STARTTLS" …
        /// "250 OK".
        /// </summary>
        private static async Task<string> ReadResponseAsync(StreamReader reader, CancellationToken ct)
        {
            var sb = new StringBuilder();
            while (true)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;

                sb.AppendLine(line);

                // The last line has a space in the fourth position, not a dash.
                // A line shorter than four characters cannot be a continuation.
                if (line.Length < 4 || line[3] != '-') break;
            }
            return sb.ToString().Trim();
        }

        private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

        private static string? Code(string response)
            => response.Length >= 3 && char.IsDigit(response[0]) ? response[..3] : null;
    }
}
