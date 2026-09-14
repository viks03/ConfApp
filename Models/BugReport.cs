// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // One row per report submitted from the floating widget (see
    // Pages/Shared/_BugReportWidget.cshtml and
    // Controllers/BugReportController.cs). Only the Admin role can submit one.
    // The administrator login is shared, so ReportedByEmail is context for the
    // audit trail and not a way of deciding who sees what.
    public class BugReport
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Description { get; set; } = string.Empty;

        // "Bug" | "UI" | "Content" | "Performance" | "Other". The controller
        // falls back to "Other" for anything it does not recognise.
        public string Category { get; set; } = "Bug";

        // "Low" | "Medium" | "High" | "Critical". The severity picks the colour
        // of the badge in the notification mail.
        public string Severity { get; set; } = "Medium";

        // Captured by the widget's JavaScript at the moment of submission; the
        // administrator types nothing here.
        public string? PageUrl { get; set; }
        public string? UserAgent { get; set; }

        // Taken server-side. An IP supplied by the client would be trivially
        // forged and worth nothing — see GetClientIp in BugReportController.
        public string? IpAddress { get; set; }

        // The shared administrator login, from User.Identity.Name. For the
        // audit trail only: it does NOT identify which person submitted the
        // report.
        public string? ReportedByEmail { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // "Open" | "InProgress" | "Resolved" | "WontFix"
        public string Status { get; set; } = "Open";

        public DateTime? ResolvedAt { get; set; }
        public string? ResolvedByEmail { get; set; }
        public string? ResolutionNotes { get; set; }
    }
}