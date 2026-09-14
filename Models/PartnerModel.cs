// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    public class PartnerModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string NameEn { get; set; } = string.Empty;

        [Required]
        public string NameBg { get; set; } = string.Empty;

        // "Institutional", "Business" or "Media" — the public page groups the
        // partners by this value.
        public string Category { get; set; } = string.Empty;

        // Relative to wwwroot.
        public string? LogoImagePath { get; set; }

        // Optional: without it the logo is rendered without a link.
        public string? WebsiteUrl { get; set; }
    }
}