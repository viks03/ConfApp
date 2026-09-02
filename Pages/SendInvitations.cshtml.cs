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

        // Хардовите лимити на самата валидация — държим ги на едно място,
        // за да могат клиентската (JS) и сървърната проверка да им отговарят.
        private const int MaxSubjectLength = 200;
        private const int MaxTemplateSizeBytes = 750 * 1024; // 750KB декодиран HTML

        public SendInvitationsModel(
            EmailSender emailSender,
            ILogger<SendInvitationsModel> logger,
            ApplicationDbContext context)
        {
            _emailSender = emailSender;
            _logger      = logger;
            _context     = context;
        }

        // ── Изпраща ЕДИН имейл (извикван от JS за всеки получател) ───────────
        public async Task<IActionResult> OnPostSendOneAsync(
            [FromForm] string email,
            [FromForm] string? name,
            [FromForm] string subject,
            [FromForm] string htmlTemplateBase64,
            [FromForm] Guid batchId)
        {
            var adminEmail = User.Identity?.Name;

            // ── 1. Валидация на входа (детайлна — всяко поле поотделно) ──────────
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

            // Темплейтът пътува base64-кодиран (виж sendEmail.js) — суров <html>/<style>
            // в тялото на POST заявката на production (зад Cloudflare/reverse proxy) се
            // засича от WAF/security филтър като потенциален XSS/injection опит и
            // заявката никога не стига дотук. Base64 blob-ът не прилича на нищо
            // разпознаваемо, затова декодираме го тук вместо да разчитаме на суров HTML.
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

            // ── 2. Изпращане + детайлна категоризация на грешките ────────────────
            var trackingToken = Guid.NewGuid();

            try
            {
                string baseUrl = GetBaseUrl();

                string greeting = !string.IsNullOrWhiteSpace(name)
                    ? $"Dear {name},"
                    : "Dear colleague,";

                // "Чистата" версия — точно това, което админът е написал/качил,
                // само с плейсхолдърите заместени. Това е и версията, която се
                // пази и сваля от History — без никакъв tracking в нея.
                string body = htmlTemplate
                    .Replace("{BaseUrl}",        baseUrl)
                    .Replace("{EmailSubject}",   subject)
                    .Replace("{Greeting}",       greeting)
                    .Replace("{RecipientName}",  name ?? "")
                    .Replace("{RecipientEmail}", email);

                // Отделно, ЕФЕМЕРНО копие само за самото изпращане — pixel +
                // пренаписани линкове. Никога не се записва никъде.
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

        // ── Връща последните N записа от History таба (реални данни от базата) ──
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

        // Часовата зона, в която UI-то показва "Time Sent" (браузърът форматира
        // локално). Използваме я и тук, за да съвпада часът в името на файла с
        // това, което админът вижда в таблицата — иначе SentAt (UTC) излиза
        // на файла с няколко часа назад спрямо реалното българско време.
        private static readonly TimeZoneInfo DisplayTimeZone = ResolveDisplayTimeZone();

        private static TimeZoneInfo ResolveDisplayTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Sofia"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
            catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
        }

        // ── Сваля точния HTML, изпратен на този получател ─────────────────────
        public async Task<IActionResult> OnGetDownloadSentEmailAsync(int id)
        {
            var log = await _context.InvitationSendLogs.FindAsync(id);
            if (log == null || string.IsNullOrEmpty(log.SentBody))
                return NotFound();

            var safeName = string.Join("_", log.Email.Split(Path.GetInvalidFileNameChars()));
            // FIX: log.SentAt е UTC — без конверсия файлът излизаше с часа
            // назад спрямо "Time Sent" в таблицата (браузърът показва локално).
            var localSentAt = TimeZoneInfo.ConvertTimeFromUtc(log.SentAt, DisplayTimeZone);
            var fileName = $"invitation_{safeName}_{localSentAt:yyyyMMdd_HHmm}.html";
            var bytes = System.Text.Encoding.UTF8.GetBytes(log.SentBody);
            return File(bytes, "text/html", fileName);
        }

        // ── Трие ЦЯЛАТА история на изпращанията от базата (необратимо) ────────
        public async Task<IActionResult> OnPostClearHistoryAsync()
        {
            var count = await _context.InvitationSendLogs.ExecuteDeleteAsync();
            var adminEmail = User.Identity?.Name;
            _logger.LogWarning("Invitation history CLEARED | {Count} records deleted | By: {AdminEmail}", count, adminEmail);
            return new JsonResult(new { success = true, deleted = count });
        }

        // ── Помощни методи ─────────────────────────────────────────────────────

        private string GetBaseUrl()
        {
            var req = HttpContext.Request;
            return $"{req.Scheme}://{req.Host}";
        }

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

        // Категоризира изключението в четим (Category, Message) чифт за
        // показване на админа. EmailSender си има собствена обработка:
        // - ArgumentException            → пропуска се непроменено (валидация)
        // - SmtpException                → увива се в InvalidOperationException
        // - друго (Socket/Timeout и т.н) → пропуска се непроменено
        private static (string Category, string Message) CategorizeError(Exception ex)
        {
            switch (ex)
            {
                case ArgumentException argEx:
                    return ("Validation", argEx.Message);

                case InvalidOperationException ioEx when ioEx.InnerException is SmtpException smtpEx:
                    return ("SMTP", DescribeSmtpError(smtpEx));

                case InvalidOperationException ioEx:
                    // Липсваща/невалидна EmailSettings конфигурация (Host/Port/User/Pass/From),
                    // или друг wrapped проблем без SmtpException вътре.
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

        // Записва резултата (успех или неуспех) в базата и връща JSON резултат
        // за неуспешен опит — извиква се вместо да се дублира навсякъде.
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
                // Не проваляме самото изпращане само защото логването се е
                // счупило — само записваме предупреждение.
                _logger.LogWarning(ex, "Failed to write InvitationSendLog entry for {Email}", email);
            }
        }

        // Изгражда ЕФЕМЕРНОТО, само-за-изпращане копие на писмото: пренаписва
        // всички абсолютни http(s) линкове през click tracker-а (по-силен
        // сигнал от самия pixel), и добавя невидим 1x1 tracking pixel точно
        // преди </body> (или най-отзад, ако темплейтът няма затворен таг).
        // Резултатът НИКОГА не се пази — само оригиналният "чист" body.
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