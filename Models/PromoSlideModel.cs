// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // The slides in the promo carousel of the mobile navigation menu (see
    // .mobile-nav-promo-slider in _Layout.cshtml and mainStyle.css).
    // DisplayOrder controls their order in the carousel and is updated by
    // drag-and-drop in the admin panel (see OnPostReorderPromosAsync).
    public class PromoSlideModel
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string TitleEn { get; set; } = string.Empty;

        [Required, MaxLength(50)]
        public string TitleBg { get; set; } = string.Empty;

        [Required, MaxLength(120)]
        public string DescriptionEn { get; set; } = string.Empty;

        [Required, MaxLength(120)]
        public string DescriptionBg { get; set; } = string.Empty;

        // A transparent PNG or SVG is expected: .mobile-nav-promo-art centres
        // the image in a 3.3×3.3rem box with object-fit: contain and never crops
        // it. The recommendation on size and format is shown in the admin form;
        // it is not enforced at the model level.
        public string? ImagePath { get; set; }

        public int DisplayOrder { get; set; }

        // Hides a slide from the carousel without deleting the row — and
        // without deleting the uploaded image.
        public bool IsActive { get; set; } = true;
    }
}