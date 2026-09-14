// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Controllers
{
    // ── A public endpoint, deliberately WITHOUT [Authorize] ────────────────────
    // It is called by the recipient's mail client, not by a signed-in
    // administrator. Everything here has to work even with an invalid or unknown
    // token: no error ever leaves this controller, the answer is always a valid
    // pixel or redirect, so that the mail does not look broken to the person
    // reading it.
    [ApiController]
    [Route("track")]
    public class TrackingController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<TrackingController> _logger;

        // The smallest valid 1x1 transparent GIF (34 bytes) — the well-known
        // base64 blob used for exactly this purpose.
        private static readonly byte[] TransparentPixel = Convert.FromBase64String(
            "R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");

        public TrackingController(ApplicationDbContext context, ILogger<TrackingController> logger)
        {
            _context = context;
            _logger  = logger;
        }

        // GET /track/open/{token} — called by the <img> tag in the mail.
        [HttpGet("open/{token:guid}")]
        public async Task<IActionResult> Open(Guid token)
        {
            try
            {
                var log = await _context.InvitationSendLogs
                    .FirstOrDefaultAsync(l => l.TrackingToken == token);

                if (log != null)
                {
                    var now = DateTime.UtcNow;
                    if (log.OpenedAt == null) log.OpenedAt = now;
                    log.LastOpenedAt = now;
                    log.OpenCount++;
                    log.OpenedUserAgent = Request.Headers.UserAgent.ToString();
                    await _context.SaveChangesAsync();

                    _logger.LogInformation(
                        "Invitation opened | Email: {Email} | Token: {Token} | OpenCount: {OpenCount} | UA: {UserAgent}",
                        log.Email, token, log.OpenCount, log.OpenedUserAgent);
                }
            }
            catch (Exception ex)
            {
                // The pixel is served whatever happens: a failed write here must
                // not turn into a broken image in someone's mail.
                _logger.LogWarning(ex, "Failed to record open for tracking token {Token}", token);
            }

            // no-store, so that neither the client nor a proxy caches the first
            // response and swallows later real opens (a forward, a second
            // read).
            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";
            return File(TransparentPixel, "image/gif");
        }

        // GET /track/click/{token}?url=… — called by the rewritten links in the
        // mail (see InjectTracking in SendInvitations.cshtml.cs). A click is a
        // stronger signal than the pixel, so it also fills in OpenedAt when that
        // is still empty.
        [HttpGet("click/{token:guid}")]
        public async Task<IActionResult> Click(Guid token, [FromQuery] string? url)
        {
            // An absolute http/https address only. This blocks javascript: and
            // data: and keeps the endpoint from becoming an open redirect that
            // anyone can point anywhere.
            if (string.IsNullOrWhiteSpace(url)
                || !Uri.TryCreate(url, UriKind.Absolute, out var target)
                || (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps))
            {
                return Redirect("/");
            }

            try
            {
                var log = await _context.InvitationSendLogs
                    .FirstOrDefaultAsync(l => l.TrackingToken == token);

                if (log != null)
                {
                    var now = DateTime.UtcNow;
                    if (log.ClickedAt == null) log.ClickedAt = now;
                    log.ClickCount++;
                    if (log.OpenedAt == null) log.OpenedAt = now; // a click proves it was opened
                    await _context.SaveChangesAsync();

                    _logger.LogInformation(
                        "Invitation link clicked | Email: {Email} | Token: {Token} | ClickCount: {ClickCount} | Target: {Target}",
                        log.Email, token, log.ClickCount, url);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record click for tracking token {Token}", token);
            }

            return Redirect(url);
        }
    }
}