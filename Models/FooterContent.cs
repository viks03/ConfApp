// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // The text of the footer: the tagline under the logo, the "Organized
    // by…" note in the Contact column, and the contact details themselves.
    // A single row (always Id = 1), seeded with sensible defaults in
    // FooterContentConfiguration and edited from the admin panel
    // (Site Settings → Footer Content), exactly like SocialLinksSetting.
    public class FooterContent
    {
        [Key]
        public int Id { get; set; }

        // Optional on purpose: when it is empty the tagline line is simply not
        // rendered in the public footer, and the logo beside the heading shrinks
        // to match one line of text instead of two. That happens by itself —
        // .footer-brand-logo uses align-items: stretch with object-fit: contain
        // — so no logic here is needed for it.
        //
        // MaxLength(45): the tagline is rendered with white-space: nowrap next
        // to the logo, so a longer text would overflow the card rather than
        // wrap.
        [MaxLength(45)]
        public string BrandTaglineEn { get; set; } = string.Empty;
        [MaxLength(45)]
        public string BrandTaglineBg { get; set; } = string.Empty;

        [Required, MaxLength(400)]
        public string OrgNoteEn { get; set; } = string.Empty;
        [Required, MaxLength(400)]
        public string OrgNoteBg { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string ContactLocationEn { get; set; } = string.Empty;
        [Required, MaxLength(100)]
        public string ContactLocationBg { get; set; } = string.Empty;

        [Required, MaxLength(150), EmailAddress]
        public string ContactEmail { get; set; } = string.Empty;
        [Required, MaxLength(30)]
        public string ContactPhone { get; set; } = string.Empty;

        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    }
}