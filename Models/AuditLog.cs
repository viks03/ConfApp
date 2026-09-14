// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Models
{
    // One row per recorded action. Written only through AuditService, which is
    // what keeps "nobody" spelled one way (null) across the table.
    public class AuditLog
    {
        public int Id { get; set; }

        // A plain string with no foreign key, on purpose: the row has to outlive
        // the account it is about. Deleting a participant leaves their audit
        // trail behind — which is the point of having one.
        public string? UserId { get; set; }
        public string UserEmail { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        // "System" for the background services, which have no request.
        public string IpAddress { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? Details { get; set; }
    }
}