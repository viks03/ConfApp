// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Data;
using ConferenceApp.Models;
using ConferenceApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;

namespace ConferenceApp.Pages
{
    [Authorize(Roles = "Admin")]
    public class SendInvitationsModel : PageModel
    {
        private readonly EmailSender _emailSender;
        private readonly ILogger<SendInvitationsModel> _logger;
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;

        // Bulk invitations: the browser posts one recipient at a time (see
        // sendEmail.js) and this handler sends one mail per request, so a
        // failure stops one recipient rather than the whole send and the page
        // can show progress as it goes.
        //
        // Every attempt is written to InvitationSendLogs, successful or not:
        // the History tab is the only record that a given person was written to.

        // The validation limits, kept in one place so that the client-side and
        // server-side checks can agree on them.
        private const int MaxSubjectLength = 200;
        private const int MaxTemplateSizeBytes = 750 * 1024; // 750 KB of decoded HTML

        public SendInvitationsModel(
            EmailSender emailSender,
            ILogger<SendInvitationsModel> logger,
            ApplicationDbContext context,
            IConfiguration config)
        {
            _emailSender = emailSender;
            _logger      = logger;
            _context     = context;
            _config      = config;
        }

        // ── Sends ONE mail; the page calls it once per recipient ────────────
        public async Task<IActionResult> OnPostSendOneAsync(
            [FromForm] string email,
            [FromForm] string? name,
            [FromForm] string subject,
            [FromForm] string htmlTemplateBase64,
            [FromForm] Guid batchId)
        {
            var adminEmail = User.Identity?.Name;

            // ── 1. Input validation, field by field ─────────────────────────────
            // Each field gets its own message: the result is shown next to the
            // recipient in the progress list, where "invalid input" would say
            // nothing.
            email   = email?.Trim()   ?? "";
            subject = subject?.Trim() ?? "";
            name    = name?.Trim();

            if (string.IsNullOrWhiteSpace(email))
                return await FailAsync(batchId, email, name, subject, "Validation",
                    "Missing recipient email address.", adminEmail);

            if (!TryParseEmail(email, out var emailError))
                return await FailAsync(batchId, email, name, subject, "Validation",
                    $"Invalid recipient email address: {emailError}", adminEmail);

            if (string.IsNullOrWhiteSpace(subject))
                return await FailAsync(batchId, email, name, subject, "Validation",
                    "Missing email subject.", adminEmail);

            if (subject.Length > MaxSubjectLength)
                return await FailAsync(batchId, email, name, subject, "Validation",
                    $"Subject is too long ({subject.Length} characters, max {MaxSubjectLength}).", adminEmail);

            if (string.IsNullOrWhiteSpace(htmlTemplateBase64))
                return await FailAsync(batchId, email, name, subject, "Validation",
                    "Missing HTML template.", adminEmail);

            // The template travels base64-encoded (see sendEmail.js). Raw
            // <html>/<style> in a POST body is read by the WAF in front of the
            // production site as an injection attempt, and the request never
            // arrives at all. A base64 blob looks like nothing in particular, so
            // it is decoded here instead.
            string htmlTemplate;
            try
            {
                var decodedBytes = Convert.FromBase64String(htmlTemplateBase64);
                if (decodedBytes.Length > MaxTemplateSizeBytes)
                    return await FailAsync(batchId, email, name, subject, "Validation",
                        $"Template is too large ({decodedBytes.Length / 1024}KB, max {MaxTemplateSizeBytes / 1024}KB).", adminEmail);

                htmlTemplate = System.Text.Encoding.UTF8.GetString(decodedBytes);
            }
            catch (FormatException)
            {
                return await FailAsync(batchId, email, name, subject, "Validation",
                    "Template could not be decoded (invalid base64 payload).", adminEmail);
            }

            if (string.IsNullOrWhiteSpace(htmlTemplate) || !htmlTemplate.Contains('<'))
                return await FailAsync(batchId, email, name, subject, "Validation",
                    "Template does not look like valid HTML (no tags found).", adminEmail);

            // ── 2. Send, and categorise whatever goes wrong ─────────────────────
            var trackingToken = Guid.NewGuid();

            try
            {
                // This used to be a local Scheme+Host that ignored ForceBaseUrl, so
                // running locally put localhost links into the invitations.
                string baseUrl = ConferenceApp.Services.Email.MailContext.BaseUrl(_config, Request);

                string greeting = !string.IsNullOrWhiteSpace(name)
                    ? $"Dear {name},"
                    : "Dear colleague,";

                // The clean version: exactly what the administrator wrote or
                // uploaded, with the placeholders substituted. This is also what
                // is stored and what History offers for download — no tracking
                // in it at all.
                string body = htmlTemplate
                    .Replace("{BaseUrl}",        baseUrl)
                    .Replace("{EmailSubject}",   subject)
                    .Replace("{Greeting}",       greeting)
                    .Replace("{RecipientName}",  name ?? "")
                    .Replace("{RecipientEmail}", email);

                // A separate, throwaway copy for the send itself: the pixel and
                // the rewritten links. It is never stored anywhere.
                string trackedBody = InjectTracking(body, trackingToken, baseUrl);

                var startedAt = DateTime.UtcNow;
                await _emailSender.SendAsync(email, subject, trackedBody);
                var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;

                _logger.LogInformation(
                    "Invitation sent | To: {Email} | Subject: {Subject} | BatchId: {BatchId} | " +
                    "SentBy: {AdminEmail} | TrackingToken: {TrackingToken} | Elapsed: {ElapsedMs}ms",
                    email, subject, batchId, adminEmail, trackingToken, elapsedMs);

                await LogAsync(batchId, email, name, subject, success: true,
                    category: null, message: null, adminEmail: adminEmail,
                    trackingToken: trackingToken, sentBody: body);

                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                var (category, detailedMessage) = CategorizeError(ex);

                _logger.LogError(ex,
                    "Invitation FAILED | To: {Email} | Subject: {Subject} | BatchId: {BatchId} | " +
                    "SentBy: {AdminEmail} | Category: {Category} | ExceptionType: {ExceptionType}",
                    email, subject, batchId, adminEmail, category, ex.GetType().Name);

                return await FailAsync(batchId, email, name, subject, category, detailedMessage, adminEmail, trackingToken);
            }
        }

        // ── The most recent rows for the History tab ────────────────────────
        public async Task<IActionResult> OnGetHistoryAsync(int take = 500)
        {
            take = Math.Clamp(take, 1, 1000);

            var logs = await _context.InvitationSendLogs
                .OrderByDescending(l => l.SentAt)
                .Take(take)
                .Select(l => new
                {
                    l.Id,
                    l.BatchId,
                    l.Email,
                    l.RecipientName,
                    l.Subject,
                    l.Success,
                    l.ErrorCategory,
                    l.ErrorMessage,
                    SentAt = l.SentAt, // UTC — клиентът форматира в локална зона
                    l.SentByEmail,
                    HasSentBody = l.SentBody != null,
                    l.OpenedAt,
                    l.OpenCount,
                    l.ClickedAt,
                    l.ClickCount
                })
                .ToListAsync();

            return new JsonResult(logs);
        }

        // The zone the table shows "Time Sent" in — the browser formats it
        // locally — used here so that the time in a downloaded file name matches
        // what the administrator sees in the row. Otherwise SentAt (UTC) puts a
        // time hours behind the Bulgarian clock on the file.
        //
        // Helpers/TimeZoneHelper does the same thing; this copy predates it.
        private static readonly TimeZoneInfo DisplayTimeZone = ResolveDisplayTimeZone();

        private static TimeZoneInfo ResolveDisplayTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Sofia"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
            catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
        }

        // ── Downloads the exact HTML this recipient was sent ──────────────────
        // The clean copy, without the tracking: what was written, not what was
        // instrumented.
        public async Task<IActionResult> OnGetDownloadSentEmailAsync(int id)
        {
            var log = await _context.InvitationSendLogs.FindAsync(id);
            if (log == null || string.IsNullOrEmpty(log.SentBody))
                return NotFound();

            var safeName = string.Join("_", log.Email.Split(Path.GetInvalidFileNameChars()));
            // log.SentAt is UTC; without the conversion the file came out with a
            // time behind the "Time Sent" column, which the browser renders
            // locally.
            var localSentAt = TimeZoneInfo.ConvertTimeFromUtc(log.SentAt, DisplayTimeZone);
            var fileName = $"invitation_{safeName}_{localSentAt:yyyyMMdd_HHmm}.html";
            var bytes = System.Text.Encoding.UTF8.GetBytes(log.SentBody);
            return File(bytes, "text/html", fileName);
        }

        // ── Deletes the WHOLE send history. There is no way back ──────────────
        public async Task<IActionResult> OnPostClearHistoryAsync()
        {
            var count = await _context.InvitationSendLogs.ExecuteDeleteAsync();
            var adminEmail = User.Identity?.Name;
            _logger.LogWarning("Invitation history CLEARED | {Count} records deleted | By: {AdminEmail}", count, adminEmail);
            return new JsonResult(new { success = true, deleted = count });
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static bool TryParseEmail(string email, out string error)
        {
            try
            {
                var addr = new MailAddress(email);
                if (addr.Address != email) { error = "unexpected format"; return false; }
                error = "";
                return true;
            }
            catch (FormatException)
            {
                error = "does not match a valid email format";
                return false;
            }
        }

        // Turns an exception into a readable (Category, Message) pair for the
        // administrator. The shapes come from EmailSender, which:
        // - lets an ArgumentException through unchanged (its own validation);
        // - wraps an SmtpException in an InvalidOperationException;
        // - lets anything else (socket, timeout) through unchanged.
        //
        // The timeout text mentions the number of seconds from
        // EmailSender.SmtpTimeoutMs: change it there and this has to follow.
        private static (string Category, string Message) CategorizeError(Exception ex)
        {
            switch (ex)
            {
                case ArgumentException argEx:
                    return ("Validation", argEx.Message);

                case InvalidOperationException ioEx when ioEx.InnerException is SmtpException smtpEx:
                    return ("SMTP", DescribeSmtpError(smtpEx));

                case InvalidOperationException ioEx:
                    // Missing or invalid EmailSettings (Host, Port, UserName,
                    // Password, From), or another wrapped problem with no
                    // SmtpException inside.
                    return ("Configuration", $"Mail server configuration problem: {ioEx.Message}");

                case SmtpException smtpEx:
                    return ("SMTP", DescribeSmtpError(smtpEx));

                case System.Net.Sockets.SocketException sockEx:
                    return ("Network", $"Could not reach the mail server ({sockEx.SocketErrorCode}). Check network connectivity, DNS, or firewall rules on the server.");

                case TimeoutException:
                    return ("Network", "The mail server took too long to respond (timeout after 15s). It may be overloaded, blocking the connection, or unreachable.");

                default:
                    return ("Unknown", $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string DescribeSmtpError(SmtpException ex)
        {
            string detail = ex.StatusCode switch
            {
                SmtpStatusCode.MailboxBusy =>
                    "the recipient's mailbox is temporarily busy — this often resolves itself on retry",
                SmtpStatusCode.MailboxUnavailable =>
                    "the recipient's mailbox is unavailable or the address doesn't exist — double-check the email address",
                SmtpStatusCode.ExceededStorageAllocation =>
                    "the recipient's mailbox is full and cannot accept more mail",
                SmtpStatusCode.TransactionFailed =>
                    "the mail server rejected the message outright — it may have been flagged as spam or bulk mail",
                SmtpStatusCode.GeneralFailure =>
                    "a general SMTP failure occurred on the server side",
                SmtpStatusCode.ServiceNotAvailable =>
                    "the mail server is temporarily unavailable",
                SmtpStatusCode.MustIssueStartTlsFirst =>
                    "the server requires STARTTLS but the connection wasn't upgraded to it",
                SmtpStatusCode.InsufficientStorage =>
                    "the mail server itself has run out of storage",
                SmtpStatusCode.LocalErrorInProcessing =>
                    "the mail server hit a local processing error while handling this message",
                _ => ex.Message
            };
            return $"SMTP error ({(int)ex.StatusCode} {ex.StatusCode}): {detail}.";
        }

        // Records a failed attempt and returns the JSON the page expects, so
        // that the two never drift apart between the call sites.
        private async Task<IActionResult> FailAsync(
            Guid batchId, string email, string? name, string subject,
            string category, string message, string? adminEmail,
            Guid? trackingToken = null)
        {
            await LogAsync(batchId, email, name, subject, success: false,
                category: category, message: message, adminEmail: adminEmail,
                trackingToken: trackingToken ?? Guid.NewGuid());
            return new JsonResult(new { success = false, message, category });
        }

        private async Task LogAsync(
            Guid batchId, string email, string? name, string subject,
            bool success, string? category, string? message, string? adminEmail,
            Guid trackingToken, string? sentBody = null)
        {
            try
            {
                _context.InvitationSendLogs.Add(new InvitationSendLog
                {
                    BatchId        = batchId,
                    Email          = email,
                    RecipientName  = name,
                    Subject        = subject,
                    Success        = success,
                    ErrorCategory  = category,
                    ErrorMessage   = message,
                    SentBody       = success ? sentBody : null,
                    SentAt         = DateTime.UtcNow,
                    SentByEmail    = adminEmail,
                    TrackingToken  = trackingToken
                });
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // The mail has already gone out: a failure to record it must not
                // be reported to the administrator as a failure to send.
                _logger.LogWarning(ex, "Failed to write InvitationSendLog entry for {Email}", email);
            }
        }

        // Builds the throwaway, send-only copy of the letter: every absolute
        // http(s) link is rewritten through the click tracker (a stronger signal
        // than the pixel), and an invisible 1x1 pixel is inserted just before
        // </body> — or at the very end, if the template has no closing tag.
        // The result is NEVER stored; only the original clean body is.
        private static string InjectTracking(string html, Guid token, string baseUrl)
        {
            string tracked = System.Text.RegularExpressions.Regex.Replace(
                html,
                "href\\s*=\\s*([\"'])(https?://[^\"']+)\\1",
                m =>
                {
                    var quote = m.Groups[1].Value;
                    var originalUrl = m.Groups[2].Value;
                    var trackedUrl = $"{baseUrl}/track/click/{token}?url={Uri.EscapeDataString(originalUrl)}";
                    return $"href={quote}{trackedUrl}{quote}";
                },
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            string pixelTag =
                $"<img src=\"{baseUrl}/track/open/{token}\" width=\"1\" height=\"1\" alt=\"\" " +
                "style=\"display:block;border:0;width:1px;height:1px;\">";

            int bodyCloseIdx = tracked.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            tracked = bodyCloseIdx >= 0
                ? tracked.Insert(bodyCloseIdx, pixelTag)
                : tracked + pixelTag;

            return tracked;
        }
    }
}