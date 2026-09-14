// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // The text of the banner and the preferences dialog (see
    // Pages/Shared/_DataNotice.cshtml). A single row (always Id = 1), edited
    // from the admin panel, exactly like PrivacyPolicyContent.
    public class CookieNoticeContent
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string ContentEn { get; set; } = string.Empty;
        [Required]
        public string ContentBg { get; set; } = string.Empty;

        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    }
}