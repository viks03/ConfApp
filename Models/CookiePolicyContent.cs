// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // The long text of the /Cookies page: what cookies are, how they are used
    // here, third parties, managing them in the browser, changes to the policy,
    // contact. A single row, edited from the admin panel with Quill, exactly
    // like PrivacyPolicyContent.
    //
    // NOT the same as CookieNoticeContent, which is the short banner text. This
    // is the page itself, wrapped around the dynamic list of categories.
    public class CookiePolicyContent
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
