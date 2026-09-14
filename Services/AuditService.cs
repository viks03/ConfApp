// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Data;
using ConferenceApp.Models;

namespace ConferenceApp.Services
{
    /// <summary>
    /// <b>The single door to the <c>AuditLogs</c> table.</b>
    ///
    /// <para>
    /// The class used to exist, be registered in the container, and be injected
    /// nowhere: all 30 real writes built their row in place with
    /// <c>new AuditLog { … }</c>, and the two controllers had each written a
    /// <c>WriteAuditAsync</c> of their own with a different signature. That is
    /// how the "who" column ended up with three conventions for "nobody"
    /// (<c>null</c>, <c>string.Empty</c>, and the value left unset) — there is
    /// one now: <c>null</c>.
    /// </para>
    ///
    /// <para>
    /// The two methods differ only in who saves:
    /// </para>
    /// <list type="bullet">
    ///   <item><see cref="Add"/> — only puts the row in the change tracker. For
    ///   callers that save the audit row together with their own business
    ///   change in one <c>SaveChangesAsync</c> (the webhook, the cleanup
    ///   service, the admin panel).</item>
    ///   <item><see cref="LogAsync"/> — adds and saves immediately.</item>
    /// </list>
    ///
    /// <para>
    /// <c>ip</c> is optional: when <c>null</c> it is read from the current
    /// request. Background services have no request and pass <c>"System"</c>
    /// explicitly — which is why the parameter exists at all, instead of the IP
    /// always coming from <see cref="IHttpContextAccessor"/>.
    /// </para>
    /// </summary>
    public class AuditService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditService(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>The IP of the current request, or <c>"Unknown"</c> when
        /// there is none.</summary>
        public string CurrentIp() =>
            _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

        /// <summary>
        /// Puts the row in the change tracker without saving. The caller saves
        /// it itself, together with its other changes.
        /// </summary>
        public AuditLog Add(
            string? userId,
            string email,
            string action,
            string? details = null,
            string? ip = null)
        {
            var log = new AuditLog
            {
                UserId    = userId,
                UserEmail = email,
                Action    = action,
                IpAddress = ip ?? CurrentIp(),
                Details   = details,
                Timestamp = DateTime.UtcNow
            };

            _context.AuditLogs.Add(log);
            return log;
        }

        /// <summary>Adds the row and saves it immediately.</summary>
        public async Task LogAsync(
            string? userId,
            string email,
            string action,
            string? details = null,
            string? ip = null)
        {
            Add(userId, email, action, details, ip);
            await _context.SaveChangesAsync();
        }
    }
}
