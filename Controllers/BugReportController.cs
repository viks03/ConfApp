// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Data;
using ConferenceApp.Models;
using ConferenceApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace ConferenceApp.Controllers
{
    // Receives the bug reports submitted from the floating widget (see
    // Pages/Shared/_BugReportWidget.cshtml). The widget appears on EVERY page,
    // public and admin alike, which is why this is a controller of its own
    // rather than a handler copied into every page in the site.
    //
    // [Authorize(Roles = "Admin")]: only a signed-in administrator can submit.
    // The widget is shown to them alone anyway, but the endpoint has to defend
    // itself — a hidden button is not a permission check.
    [Authorize(Roles = "Admin")]
    [Route("api/bug-reports")]
    [ValidateAntiForgeryToken]
    public class BugReportController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailSender _emailSender;
        private readonly ILogger<BugReportController> _logger;
        private readonly IConfiguration _config;
        private readonly IBackgroundTaskQueue _queue;

        // Who gets a mail on every new report.
        private const string NotifyEmail = "viktor.georgiev@icbi.bg";

        public BugReportController(
            ApplicationDbContext context,
            EmailSender emailSender,
            ILogger<BugReportController> logger,
            IConfiguration config,
            IBackgroundTaskQueue queue)
        {
            _context     = context;
            _emailSender = emailSender;
            _logger      = logger;
            _config      = config;
            _queue       = queue;
        }

        [HttpPost("submit")]
        public async Task<IActionResult> Submit(
            [FromForm] string title,
            [FromForm] string description,
            [FromForm] string category,
            [FromForm] string severity,
            [FromForm] string? pageUrl,
            [FromForm] string? userAgent)
        {
            title       = title?.Trim()       ?? "";
            description = description?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(title))
                return BadRequest(new { success = false, message = "Title is required." });
            if (string.IsNullOrWhiteSpace(description))
                return BadRequest(new { success = false, message = "Description is required." });

            // An allowlist against arbitrary values from the client. An
            // unrecognised value falls back to a safe default rather than
            // rejecting the report: the text is what matters and it is already
            // typed.
            string[] validCategories = ["Bug", "UI", "Content", "Performance", "Other"];
            string[] validSeverities = ["Low", "Medium", "High", "Critical"];
            category = validCategories.Contains(category) ? category : "Other";
            severity = validSeverities.Contains(severity) ? severity : "Medium";

            string ipAddress = GetClientIp();
            var (browser, os) = ParseUserAgent(userAgent);

            var report = new BugReport
            {
                Title           = title,
                Description     = description,
                Category        = category,
                Severity        = severity,
                PageUrl         = pageUrl,
                UserAgent       = userAgent,
                IpAddress       = ipAddress,
                ReportedByEmail = User.Identity?.Name,
                CreatedAt       = DateTime.UtcNow,
                Status          = "Open"
            };

            _context.BugReports.Add(report);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Bug report submitted | Id: {Id} | Title: {Title} | Category: {Category} | Severity: {Severity} | By: {AdminEmail} | Page: {PageUrl} | IP: {Ip}",
                report.Id, title, category, severity, report.ReportedByEmail, pageUrl, ipAddress);

            // The notification goes through the background queue, like every
            // other mail. It used to be sent HERE, inside the request, with the
            // 15-second SMTP timeout: an administrator reporting a problem while
            // the mail server was unreachable watched a spinner for fifteen
            // seconds, for a mail that has nothing to do with the report itself.
            // The report is already saved above, so the wait protected nothing.
            //
            // The queue also brings the three attempts (MailRetry) and records a
            // final failure in its state, which is where the Health tab shows
            // "last failure" from. Before, a failure was one line in the log.
            //
            // Everything the task uses is computed NOW: it runs after the
            // request has ended, when Request and the scoped services are gone.
            var subject   = $"[Bug Report] {title}";
            var baseUrl   = ConferenceApp.Services.Email.MailContext.BaseUrl(_config, Request);
            var emailBody = BuildNotificationEmail(report, baseUrl, browser, os);
            var sender    = _emailSender;      // a singleton, so it outlives the request
            var logger    = _logger;
            var reportId  = report.Id;

            _queue.QueueBackgroundWorkItem(async ct =>
            {
                try
                {
                    await ConferenceApp.Services.Email.MailRetry.RunAsync(
                        token => sender.SendAsync(NotifyEmail, subject, emailBody),
                        "Bug report", NotifyEmail, logger, ct);
                }
                catch (Exception ex)
                {
                    // Logged with the report id and rethrown:
                    // QueuedHostedService counts the failure and surfaces it in
                    // the Health tab.
                    logger.LogError(ex,
                        "Известието за сигнал {Id} не тръгна след всички опити.", reportId);
                    throw;
                }
            });

            return new JsonResult(new { success = true, id = report.Id });
        }

        // ── The submitter's real IP ────────────────────────────────────────────
        // The site runs behind Cloudflare or a reverse proxy, so
        // HttpContext.Connection.RemoteIpAddress would be the proxy's address
        // rather than the client's. X-Forwarded-For carries the real chain, its
        // first entry being the original client; without the header (a direct
        // connection) it falls back to RemoteIpAddress.
        //
        // Reading the header directly is safe only because Program.cs strips it
        // from any request that did not come from a trusted proxy.
        private string GetClientIp()
        {
            var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
            {
                var first = forwardedFor.Split(',')[0].Trim();
                if (!string.IsNullOrWhiteSpace(first)) return first;
            }

            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }

        // ── A light, best-effort user-agent parser ──────────────────────────
        // No claim to full accuracy: real user-agent parsing is a rabbit hole of
        // thousands of edge cases. This recognises the common browser and OS
        // combinations, which is enough context for the mail.
        //
        // The order of the checks matters: Edge and Opera both contain
        // "Chrome/", and Chrome contains "Safari/", so the most specific has to
        // be tested first.
        private static (string Browser, string Os) ParseUserAgent(string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return ("Unknown", "Unknown");

            string browser =
                userAgent.Contains("Edg/")                                  ? "Edge"
                : userAgent.Contains("OPR/") || userAgent.Contains("Opera") ? "Opera"
                : userAgent.Contains("Chrome/")                             ? "Chrome"
                : userAgent.Contains("Firefox/")                            ? "Firefox"
                : userAgent.Contains("Safari/")                             ? "Safari"
                : "Unknown";

            string os =
                userAgent.Contains("Windows")                                    ? "Windows"
                : userAgent.Contains("Mac OS X") || userAgent.Contains("Macintosh") ? "macOS"
                : userAgent.Contains("Android")                                  ? "Android"
                : userAgent.Contains("iPhone") || userAgent.Contains("iPad")      ? "iOS"
                : userAgent.Contains("Linux")                                    ? "Linux"
                : "Unknown";

            return (browser, os);
        }

        // ── Builds the notification as email-safe HTML ───────────────────────
        // SVG, flexbox and grid are deliberately avoided: support for them in
        // mail clients (Outlook above all) is unreliable. A table layout with
        // inline styles is the safe standard for transactional mail.
        private static string BuildNotificationEmail(
            BugReport report, string baseUrl, string browser, string os)
        {
            var (sevBg, sevFg) = report.Severity switch
            {
                "Critical" => ("#fef2f2", "#b91c1c"),
                "High"     => ("#fff7ed", "#c2410c"),
                "Medium"   => ("#fefce8", "#a16207"),
                _          => ("#f1f5f9", "#64748b"), // Low
            };

            string encTitle = WebUtility.HtmlEncode(report.Title);
            string encDescription = WebUtility.HtmlEncode(report.Description).Replace("\n", "<br>");
            string? encPageUrl = string.IsNullOrWhiteSpace(report.PageUrl) ? null : WebUtility.HtmlEncode(report.PageUrl);
            string createdLocal = report.CreatedAt.ToString("dd MMM yyyy, HH:mm") + " UTC";

            // Same brand colors as adminPanel.css (--c-slate / --c-red) — kept as
            // literal hex here since email HTML can't reference CSS variables.
            const string slate = "#1e293b";
            const string brandRed = "#c0392b";

            return $@"
<div style=""font-family:'DM Sans',-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;max-width:560px;margin:0 auto;"">
  <div style=""background:{slate};border-radius:14px 14px 0 0;padding:22px 26px;"">
    <p style=""color:#94a3b8;font-size:10.5px;font-weight:700;text-transform:uppercase;letter-spacing:0.6px;margin:0 0 6px;"">New Bug Report</p>
    <p style=""color:#ffffff;font-size:18px;font-weight:800;margin:0;line-height:1.35;"">{encTitle}</p>
  </div>
  <div style=""background:#ffffff;border:1px solid #e2e8f0;border-top:none;border-radius:0 0 14px 14px;padding:24px 26px;"">
    <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin-bottom:18px;"">
      <tr>
        <td>
          <span style=""display:inline-block;background:#f1f5f9;color:#334155;font-size:11px;font-weight:700;padding:4px 11px;border-radius:999px;"">{report.Category}</span>
        </td>
        <td align=""right"">
          <span style=""display:inline-block;background:{sevBg};color:{sevFg};font-size:10.5px;font-weight:800;text-transform:uppercase;letter-spacing:0.3px;padding:4px 11px;border-radius:999px;"">{report.Severity}</span>
        </td>
      </tr>
    </table>

    <p style=""color:#334155;font-size:14px;line-height:1.65;margin:0 0 22px;"">{encDescription}</p>

    <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f8fafc;border-radius:10px;font-size:12.5px;color:#475569;"">
      <tr>
        <td style=""padding:10px 14px;font-weight:700;width:110px;vertical-align:top;"">Reported by</td>
        <td style=""padding:10px 14px;vertical-align:top;"">{report.ReportedByEmail ?? "Unknown"}</td>
      </tr>
      <tr>
        <td style=""padding:10px 14px;font-weight:700;vertical-align:top;border-top:1px solid #e2e8f0;"">Page</td>
        <td style=""padding:10px 14px;vertical-align:top;border-top:1px solid #e2e8f0;word-break:break-all;"">
          {(encPageUrl == null ? "—" : $"<a href=\"{encPageUrl}\" style=\"color:{brandRed};text-decoration:none;\">{encPageUrl}</a>")}
        </td>
      </tr>
      <tr>
        <td style=""padding:10px 14px;font-weight:700;vertical-align:top;border-top:1px solid #e2e8f0;"">Browser</td>
        <td style=""padding:10px 14px;vertical-align:top;border-top:1px solid #e2e8f0;"">{browser} on {os}</td>
      </tr>
      <tr>
        <td style=""padding:10px 14px;font-weight:700;vertical-align:top;border-top:1px solid #e2e8f0;"">IP address</td>
        <td style=""padding:10px 14px;vertical-align:top;border-top:1px solid #e2e8f0;font-family:monospace;"">{report.IpAddress}</td>
      </tr>
      <tr>
        <td style=""padding:10px 14px;font-weight:700;vertical-align:top;border-top:1px solid #e2e8f0;"">Time</td>
        <td style=""padding:10px 14px;vertical-align:top;border-top:1px solid #e2e8f0;"">{createdLocal}</td>
      </tr>
    </table>

    <div style=""text-align:center;margin-top:26px;"">
      <a href=""{baseUrl}/Admin/BugReports"" style=""display:inline-block;background:{brandRed};color:#ffffff;font-size:13px;font-weight:700;padding:12px 26px;border-radius:999px;text-decoration:none;"">Open in Admin Panel →</a>
    </div>
  </div>
</div>";
        }
    }
}