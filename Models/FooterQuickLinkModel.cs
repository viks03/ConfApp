// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // The links in the "Quick Links" column of the public footer (see
    // .footer-quicklinks in _Layout.cshtml). Their order on the site is
    // RANDOM on every page load — they are shuffled in _Layout.cshtml — so
    // unlike PromoSlideModel and FaqModel there is NO DisplayOrder column here,
    // and the admin panel deliberately offers no drag-to-reorder for this list.
    //
    // IconSvg holds the inner SVG markup only (elements such as <path>,
    // <circle>, <line>), NOT a whole <svg> tag: it is wrapped automatically in
    // the site's standard 24×24 stroke-based icon frame, both in the public
    // footer and in the admin panel preview.
    public class FooterQuickLinkModel
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(60)]
        public string LabelEn { get; set; } = string.Empty;

        [Required, MaxLength(60)]
        public string LabelBg { get; set; } = string.Empty;

        [Required, MaxLength(300)]
        public string Url { get; set; } = string.Empty;

        // 2000 characters is a deliberate ceiling: the most complex seeded
        // icon is about 250, so this leaves generous room for a more detailed
        // one while keeping a huge SVG or a pasted base64 blob out of the
        // database.
        [Required, MaxLength(2000)]
        public string IconSvg { get; set; } = string.Empty;

        // Hides a link from the public footer without deleting the row (see
        // .toggle-footerlink-active-btn).
        public bool IsVisible { get; set; } = true;
    }
}