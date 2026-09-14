// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // One row per attempt to send an invitation, successful or not. BatchId
    // groups every recipient of one send, so that the History tab can show them
    // together as "the last send".
    public class InvitationSendLog
    {
        [Key]
        public int Id { get; set; }

        public Guid BatchId { get; set; }

        [Required]
        public string Email { get; set; } = string.Empty;

        public string? RecipientName { get; set; }

        [Required]
        public string Subject { get; set; } = string.Empty;

        public bool Success { get; set; }

        // "SMTP" / "Network" / "Validation" / "Configuration" / "Unknown".
        // null when Success is true.
        public string? ErrorCategory { get; set; }

        // The full message, shown to the administrator in the History tab.
        public string? ErrorMessage { get; set; }

        // The rendered HTML this recipient was actually sent, placeholders
        // already substituted — ALWAYS the clean version, without the tracking
        // pixel or the rewritten links, so that what History offers for download
        // is the letter itself. The tracking is injected into a separate copy at
        // the moment of sending (see InjectTracking in the page model) and is
        // never stored anywhere.
        public string? SentBody { get; set; }

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        // Which administrator sent it, for the audit trail.
        public string? SentByEmail { get; set; }

        // ── Open and click tracking ─────────────────────────────────────────
        // A unique token, embedded in the pixel and click URLs for THIS row.
        // Deliberately separate from Id: the tracking endpoints are anonymous,
        // and with a sequential id anyone could count upwards and fabricate
        // "opens" for other people's rows.
        public Guid TrackingToken { get; set; } = Guid.NewGuid();

        // When the pixel or a link first fired. null means never, as far as we
        // know — a client that blocks images leaves no trace, which is what the
        // note in the History tab says.
        public DateTime? OpenedAt { get; set; }
        public DateTime? LastOpenedAt { get; set; }
        public int OpenCount { get; set; }

        // A click on a link in the letter — a stronger signal than the pixel,
        // which a mail client may fetch on its own.
        public DateTime? ClickedAt { get; set; }
        public int ClickCount { get; set; }

        // The User-Agent of the request that first fetched the pixel. It helps
        // to judge by eye whether this looks like a real mail client or an
        // automatic prefetch or scanner. Not a reliable detection, only a hint.
        public string? OpenedUserAgent { get; set; }
    }
}