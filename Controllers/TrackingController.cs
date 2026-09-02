using ConferenceApp.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Controllers
{
    // ── ПУБЛИЧЕН endpoint (нарочно БЕЗ [Authorize]) ────────────────────────────
    // Извиква се директно от mail клиента на получателя, не от логнат админ.
    // Всичко тук трябва да работи дори при невалиден/непознат token — никога
    // не хвърляме грешка навън, винаги връщаме валиден отговор (pixel/redirect),
    // за да не изглежда счупено писмото на получателя.
    [ApiController]
    [Route("track")]
    public class TrackingController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<TrackingController> _logger;

        // Най-малкия валиден 1x1 прозрачен GIF (34 байта) — стандартен,
        // добре познат base64 за точно тази цел.
        private static readonly byte[] TransparentPixel = Convert.FromBase64String(
            "R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");

        public TrackingController(ApplicationDbContext context, ILogger<TrackingController> logger)
        {
            _context = context;
            _logger  = logger;
        }

        // GET /track/open/{token} — вика се от <img> tag-а в писмото.
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
                // Никога не проваляме показването на пиксела заради грешка в лога.
                _logger.LogWarning(ex, "Failed to record open for tracking token {Token}", token);
            }

            // no-store: за да не кешира клиентът/прокси първия отговор и да
            // пропусне следващи реални отваряния (forward, повторно четене и т.н.)
            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";
            return File(TransparentPixel, "image/gif");
        }

        // GET /track/click/{token}?url=... — вика се от пренаписаните линкове
        // в писмото (виж InjectTracking в SendInvitations.cshtml.cs). Клик е
        // по-силен сигнал от pixel-а, затова маркира и OpenedAt, ако липсва.
        [HttpGet("click/{token:guid}")]
        public async Task<IActionResult> Click(Guid token, [FromQuery] string? url)
        {
            // Само абсолютен http/https адрес — блокира javascript:/data: и
            // други схеми, за да не се превърне endpoint-ът в inject вектор.
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
                    if (log.OpenedAt == null) log.OpenedAt = now; // клик доказва, че е отворено
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