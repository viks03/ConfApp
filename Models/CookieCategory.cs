// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // One category in the cookie preferences dialog (see
    // Pages/Shared/_DataNotice.cshtml). The four standard categories
    // (Necessary, Analytics, Marketing, Preferences) are seeded with
    // IsBuiltIn = true; an administrator can add as many more as they like.
    public class CookieCategory
    {
        [Key]
        public int Id { get; set; }

        // A stable slug the JavaScript and the server logic refer to —
        // "necessary", "analytics" and so on. Never shown to the visitor, so
        // renaming the category does not break the consent already stored in
        // their browser.
        [Required]
        public string Key { get; set; } = string.Empty;

        [Required]
        public string NameEn { get; set; } = string.Empty;
        [Required]
        public string NameBg { get; set; } = string.Empty;

        public string DescriptionEn { get; set; } = string.Empty;
        public string DescriptionBg { get; set; } = string.Empty;

        // Whether the category appears in the preferences dialog at all.
        public bool IsVisible { get; set; } = true;

        // Whether the visitor can switch it themselves. "Strictly Necessary"
        // is the one category for which this must ALWAYS be false — the panel
        // forces it server-side for Key == "necessary", so a mistake or an edit
        // through the browser's dev tools cannot turn it on.
        public bool IsToggleable { get; set; } = true;

        // The value used until the visitor has chosen anything — and, for a
        // category that cannot be toggled, its fixed value.
        public bool DefaultOn { get; set; } = false;

        // Tells the four seeded categories from ones an administrator added,
        // mainly so that "necessary" cannot be deleted.
        public bool IsBuiltIn { get; set; } = false;

        public int DisplayOrder { get; set; }
    }
}