// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // The content of the Terms of Use page. A single row (always Id = 1),
    // edited from the admin panel instead of being fixed at compile time in
    // resx files. It replaces the old Pages.Terms_Sec1..9 keys in
    // Pages.Terms.en/bg.resx, and mirrors PrivacyPolicyContent.
    public class TermsOfUseContent
    {
        [Key]
        public int Id { get; set; }

        // The full HTML of the terms, from the Quill rich-text editor.
        [Required]
        public string ContentEn { get; set; } = string.Empty;

        // The same, in Bulgarian.
        [Required]
        public string ContentBg { get; set; } = string.Empty;

        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
