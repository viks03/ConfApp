using ConferenceApp.Data;
using ConferenceApp.Models;
using ConferenceApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace ConferenceApp.Controllers
{
    // Приема bug report-и, подадени от плаващия widget (виж
    // Pages/Shared/_BugReportWidget.cshtml) — той се показва на ВСЯКА страница
    // (публична и admin), затова живее в отделен Controller вместо в конкретна
    // Razor Page — не искаме handler-и, копирани по всяка страница в сайта.
    //
    // [Authorize(Roles = "Admin")] — само логнат администратор може да подаде
    // репорт (widget-ът и така се показва само за тях, но проверяваме пак тук,
    // endpoint-ът трябва да е защитен сам по себе си, не само UI-то).
    [Authorize(Roles = "Admin")]
    [Route("api/bug-reports")]
    [ValidateAntiForgeryToken]
    public class BugReportController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly EmailSender _emailSender;
        private readonly ILogger<BugReportController> _logger;

        // Кой получава email известие при всеки нов репорт.
        private const string NotifyEmail = "viktor.georgiev@icbi.bg";

        public BugReportController(
            ApplicationDbContext context,
            EmailSender emailSender,
            ILogger<BugReportController> logger)
        {
            _context     = context;
            _emailSender = emailSender;
            _logger      = logger;
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

            // Whitelist срещу произволни стойности от клиента — падаме си на
            // безопасен default вместо да отхвърляме репорта заради дребна грешка.
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

            // Email известието е "best effort" — репортът вече е записан в базата
            // независимо какво стане тук. Никога не искаме да изгубим репорт само
            // защото SMTP-то временно куца.
            try
            {
                string baseUrl = $"{Request.Scheme}://{Request.Host}";
                string emailBody = BuildNotificationEmail(
                    report, baseUrl, browser, os);

                await _emailSender.SendAsync(NotifyEmail, $"[Bug Report] {title}", emailBody);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send bug report notification email for report Id {Id}", report.Id);
            }

            return new JsonResult(new { success = true, id = report.Id });
        }

        // ── Реалният IP на подателя ────────────────────────────────────────────
        // Сайтът минава зад Cloudflare/reverse proxy (виж cloudflared tunnel-а),
        // затова HttpContext.Connection.RemoteIpAddress би показал IP-то на
        // proxy-то, не на реалния клиент. X-Forwarded-For носи истинската
        // верига (първият адрес е оригиналният клиент); ако липсва (директна
        // връзка, без proxy), падаме си на RemoteIpAddress.
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

        // ── Лек, best-effort UA parser ──────────────────────────────────────
        // Не претендираме за пълна точност (истински UA parsing е дълбока
        // заешка дупка с хиляди edge cases) — просто разпознаваме най-честите
        // browser/OS комбинации, достатъчно за бърз контекст в имейла.
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

        // ── Изгражда добре структуриран, email-safe HTML ─────────────────────
        // Съзнателно НЕ ползваме SVG или flexbox/grid тук — support-ът им в
        // email клиенти (Outlook най-вече) е ненадежден. Table-based layout +
        // inline стилове е стандартният безопасен подход за transactional имейли.
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
            string encPageUrl = string.IsNullOrWhiteSpace(report.PageUrl) ? null : WebUtility.HtmlEncode(report.PageUrl);
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