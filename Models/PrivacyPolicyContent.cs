// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // The content of the Privacy Policy / GDPR page. A single row (always
    // Id = 1), edited from the admin panel instead of being fixed at compile
    // time in resx files. It replaces the old Pages.Privacy.en/bg.resx.
    public class PrivacyPolicyContent
    {
        [Key]
        public int Id { get; set; }

        // The full HTML of the policy, from the Quill rich-text editor.
        [Required]
        public string ContentEn { get; set; } = string.Empty;

        // The same, in Bulgarian.
        [Required]
        public string ContentBg { get; set; } = string.Empty;

        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    }
}