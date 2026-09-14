// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ConferenceApp.Services
{
    public class EmailSender
    {
        // How long the SMTP server is given to answer before the send is cut
        // off. Kept as a constant because CategorizeError
        // (SendInvitations.cshtml.cs) reports "timeout after Xs" from the same
        // number — change it here and it has to change there too.
        private const int SmtpTimeoutMs = 15_000;

        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(
            IConfiguration config,
            IHttpContextAccessor httpContextAccessor,
            ILogger<EmailSender> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ────────────────────────────────────────────────────────────────────────
        //  MAIN ENTRY POINT
        // ────────────────────────────────────────────────────────────────────────
        public async Task SendAsync(
            string to,
            string subject,
            string htmlBody,
            string? filePath = null)
        {
            // Everything below is validated before a connection is opened, so
            // that a bad call fails as ArgumentException — which MailRetry
            // treats as not worth retrying.
            if (string.IsNullOrWhiteSpace(to))
                throw new ArgumentException("Recipient address cannot be empty.", nameof(to));
            if (string.IsNullOrWhiteSpace(subject))
                throw new ArgumentException("Email subject cannot be empty.", nameof(subject));
            if (string.IsNullOrWhiteSpace(htmlBody))
                throw new ArgumentException("Email body cannot be empty.", nameof(htmlBody));
            if (!IsValidEmail(to))
                throw new ArgumentException($"Invalid recipient email address: {to}", nameof(to));

            var settings = _config.GetSection("EmailSettings");
            ValidateSettings(settings);

            try
            {
                // A safety net for {BaseUrl}: the callers
                // (EmailTemplateRenderer, SendInvitations, BugReportController)
                // have already substituted the placeholder before they get here.
                // The rule for the address is a single one, though —
                // MailContext.BaseUrl — rather than a local Scheme+Host with a
                // hard-coded fallback, which used to ignore ForceBaseUrl.
                string baseUrl = ConferenceApp.Services.Email.MailContext.BaseUrl(
                    _config, _httpContextAccessor.HttpContext?.Request);
                string resolvedHtml = htmlBody.Replace("{BaseUrl}", baseUrl, StringComparison.OrdinalIgnoreCase);

                using var message = BuildMessage(to, subject, resolvedHtml, settings["From"]!, filePath);

                // SmtpClient.Timeout is documented to apply to the synchronous
                // Send() only — for SendMailAsync it is not guaranteed to break
                // off the connection when the server simply does not answer. The
                // timeout is therefore enforced through a CancellationToken,
                // whatever SmtpClient does internally.
                using var client = BuildSmtpClient(settings);
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(SmtpTimeoutMs));

                try
                {
                    await client.SendMailAsync(message, cts.Token);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    // Our own timeout fired rather than an external
                    // cancellation, so it is turned into a TimeoutException:
                    // CategorizeError in SendInvitations.cshtml.cs already knows
                    // how to show that one to the administrator.
                    throw new TimeoutException(
                        $"The mail server did not respond within {SmtpTimeoutMs / 1000} seconds.");
                }

                _logger.LogInformation("Email sent successfully to {Recipient} | Subject: {Subject}", to, subject);
            }
            catch (SmtpException ex)
            {
                // The original exception goes to the log; the caller gets a
                // message with no server or credential details in it.
                _logger.LogError(ex, "SMTP error while sending email to {Recipient}", to);
                throw new InvalidOperationException("Failed to send email. Please try again later.", ex);
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "SMTP send to {Recipient} timed out after {TimeoutMs}ms", to, SmtpTimeoutMs);
                throw;
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                _logger.LogError(ex, "Unexpected error while sending email to {Recipient}", to);
                throw;
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        //  BUILDERS
        // ────────────────────────────────────────────────────────────────────────
        private static MailMessage BuildMessage(
            string to,
            string subject,
            string htmlBody,
            string fromAddress,
            string? filePath)
        {
            var message = new MailMessage
            {
                From = new MailAddress(fromAddress, "Blockchain Education"),
                Subject = subject,
                // Without an explicit SubjectEncoding, Cyrillic in the subject
                // line arrives mangled.
                SubjectEncoding = System.Text.Encoding.UTF8,
                BodyEncoding = System.Text.Encoding.UTF8,
                IsBodyHtml = true // Outlook / Exchange require this
            };

            message.To.Add(to);

            // A plain-text alternative. Spam filters score a mail without one
            // markedly worse.
            string plainText = StripHTML(htmlBody);
            var plainView = AlternateView.CreateAlternateViewFromString(
                plainText, System.Text.Encoding.UTF8, MediaTypeNames.Text.Plain);

            // The HTML view carries its charset inline — Gmail needs it there.
            var htmlView = AlternateView.CreateAlternateViewFromString(
                htmlBody, System.Text.Encoding.UTF8, MediaTypeNames.Text.Html);

            message.AlternateViews.Add(plainView);
            message.AlternateViews.Add(htmlView);   // Order matters: clients pick the LAST view they understand.

            if (!string.IsNullOrWhiteSpace(filePath) && System.IO.File.Exists(filePath))
            {
                // Not disposed here on purpose: MailMessage owns its attachments
                // and disposes them with itself.
                message.Attachments.Add(new Attachment(filePath));
            }

            return message;
        }

        private SmtpClient BuildSmtpClient(IConfiguration settings)
        {
            // Parsed with explicit checks rather than int.Parse: a typo in
            // configuration should say which key is wrong, not throw a bare
            // FormatException from somewhere inside the send.
            if (!int.TryParse(settings["Port"], out int port) || port <= 0)
                throw new InvalidOperationException("EmailSettings:Port is missing or invalid.");

            if (!bool.TryParse(settings["EnableSsl"], out bool enableSsl))
                throw new InvalidOperationException("EmailSettings:EnableSsl is missing or invalid.");

            return new SmtpClient(settings["Host"])
            {
                Port = port,
                EnableSsl = enableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(
                    settings["UserName"],
                    settings["Password"]),
                // Secondary defence — the timeout that actually applies to
                // SendMailAsync is enforced by the CancellationTokenSource in
                // SendAsync (see SmtpTimeoutMs above). The two are kept equal.
                Timeout = SmtpTimeoutMs
            };
        }

        // ────────────────────────────────────────────────────────────────────────
        //  HELPERS
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Checks that the mandatory EmailSettings keys are present, so that a
        /// missing one is reported by name instead of surfacing as a null
        /// somewhere inside SmtpClient.
        /// </summary>
        private static void ValidateSettings(IConfiguration settings)
        {
            string[] required = ["Host", "Port", "EnableSsl", "UserName", "Password", "From"];
            foreach (var key in required)
            {
                if (string.IsNullOrWhiteSpace(settings[key]))
                    throw new InvalidOperationException(
                        $"EmailSettings:{key} is missing or empty in configuration.");
            }
        }

        /// <summary>
        /// Validates an address by letting <see cref="MailAddress"/> parse it:
        /// the same parser the send itself will use, so nothing is accepted here
        /// that SmtpClient would later reject. The equality check rejects the
        /// forms MailAddress accepts but rewrites, such as "Name &lt;a@b.c&gt;".
        /// </summary>
        private static bool IsValidEmail(string email)
        {
            try
            {
                var addr = new MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Converts the HTML body to plain text for the alternative view.
        /// </summary>
        private static string StripHTML(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // 1. Drop <style>, <script> and <head> whole — without this the CSS
            //    itself ends up as text in the plain-text version.
            string result = Regex.Replace(input, @"<style[\s\S]*?</style>", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<script[\s\S]*?</script>", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<head[\s\S]*?</head>", string.Empty, RegexOptions.IgnoreCase);

            // 2. Drop MSO / HTML conditional comments <!--[if…]>…<![endif]-->.
            result = Regex.Replace(result, @"<!--\[if[\s\S]*?<!\[endif\]-->", string.Empty, RegexOptions.IgnoreCase);

            // 3. Drop every remaining HTML comment.
            result = Regex.Replace(result, @"<!--[\s\S]*?-->", string.Empty);

            // 3.5. Before the blanket tag strip below eats the <a> tags, lift
            //      the href out into visible text. Otherwise the plain-text
            //      version keeps only the link text ("Click here") and a
            //      recipient without an HTML mail client has no way to reach the
            //      link at all. This step has to run before step 5.
            result = Regex.Replace(
                result,
                @"<a\s+[^>]*href\s*=\s*[""']([^""']+)[""'][^>]*>([\s\S]*?)</a>",
                m =>
                {
                    var url = m.Groups[1].Value.Trim();
                    // The link text may contain nested tags (a <span>, say);
                    // they are stripped here so that no tag fragments survive
                    // into the result.
                    var linkText = Regex.Replace(m.Groups[2].Value, "<[^>]+>", string.Empty).Trim();

                    if (linkText.Length == 0 || linkText.Equals(url, StringComparison.OrdinalIgnoreCase))
                        return url;

                    return $"{linkText} ({url})";
                },
                RegexOptions.IgnoreCase);

            // 4. Turn the structural tags into line breaks, before the rest of
            //    the markup is thrown away and the text runs together.
            result = Regex.Replace(result, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</p\s*>", "\n\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</tr\s*>", "\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</h[1-6]\s*>", "\n\n", RegexOptions.IgnoreCase);

            // 5. Drop every tag that is left.
            result = Regex.Replace(result, "<[^>]+>", string.Empty);

            // 6. Decode the HTML entities (&amp; → &, &zwnj; → nothing, …).
            result = WebUtility.HtmlDecode(result);

            // 7. Normalise the whitespace the markup left behind.
            result = Regex.Replace(result, @"[ \t]+", " ");           // runs of spaces → one
            result = Regex.Replace(result, @" *\n *", "\n");          // spaces around line breaks
            result = Regex.Replace(result, @"\n{3,}", "\n\n");        // at most two blank lines in a row

            return result.Trim();
        }
    }
}