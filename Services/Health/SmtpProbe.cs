using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace ConferenceApp.Services.Health
{
    /// <summary>
    /// Проверява SMTP връзката, като води ръчно протоколния диалог:
    /// свързване, EHLO, STARTTLS, AUTH LOGIN, QUIT — <b>без да изпраща писмо</b>.
    ///
    /// <para>
    /// Защо на ръка, вместо през <c>SmtpClient</c>: приложението ползва
    /// <c>System.Net.Mail.SmtpClient</c>, чийто API няма „свържи се и се
    /// удостовери, но не изпращай“. Единственият начин да се провери, че
    /// паролата е валидна, е да се изпрати реално писмо — което за проверка
    /// на състоянието е недопустимо (администраторът би пращал имейл на някого
    /// при всяко натискане на „Провери“).
    /// </para>
    ///
    /// <para>
    /// Затова тук се говори директно по протокола. Диалогът стига точно до
    /// отговора на сървъра за <c>AUTH</c> и приключва с <c>QUIT</c>. Нищо не се
    /// изпраща, нищо не се създава.
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

            // TcpClient.ConnectAsync не спазва CancellationToken на всички
            // платформи по един и същ начин, затова прекъсването е ръчно.
            await tcp.ConnectAsync(host, port, ct);

            using var raw = tcp.GetStream();
            Stream stream = raw;
            var tlsUp = false;

            // Порт 465 е „implicit TLS“ — шифроването започва веднага.
            // Порт 587 (какъвто е Office 365) е plaintext до STARTTLS.
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

            // ── Поздрав ────────────────────────────────────────────────────
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

                // След STARTTLS диалогът започва отначало по шифрования канал.
                reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true) { AutoFlush = true };

                await writer.WriteAsync("EHLO healthcheck.local\r\n");
                var ehlo2 = await ReadResponseAsync(reader, ct);
                if (!ehlo2.StartsWith("250"))
                    return new Probe(true, true, false, greeting, Code(ehlo2), ehlo2);
            }

            // ── AUTH LOGIN ─────────────────────────────────────────────────
            // Само тази стъпка доказва, че паролата в конфигурацията е валидна.
            // Без нея проверката би казвала само "сървърът приема връзки".
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

            // Затваряме учтиво, за да не остави сървърът висяща сесия.
            try
            {
                await writer.WriteAsync("QUIT\r\n");
                await ReadResponseAsync(reader, ct);
            }
            catch { /* затварянето не е важно за резултата */ }

            return authed
                ? new Probe(true, tlsUp, true, greeting, null, null)
                : new Probe(true, tlsUp, false, greeting, Code(passReply), passReply);
        }

        /// <summary>
        /// Чете пълен SMTP отговор. Многоредовите отговори (EHLO) имат тире
        /// след кода на всеки ред освен последния: „250-STARTTLS“ … „250 OK“.
        /// </summary>
        private static async Task<string> ReadResponseAsync(StreamReader reader, CancellationToken ct)
        {
            var sb = new StringBuilder();
            while (true)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;

                sb.AppendLine(line);

                // Последният ред е с интервал на четвърта позиция, не с тире.
                if (line.Length < 4 || line[3] != '-') break;
            }
            return sb.ToString().Trim();
        }

        private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

        private static string? Code(string response)
            => response.Length >= 3 && char.IsDigit(response[0]) ? response[..3] : null;
    }
}
