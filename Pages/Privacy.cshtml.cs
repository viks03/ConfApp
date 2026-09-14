// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Data;
using ConferenceApp.Helpers;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Pages
{
    public class PrivacyModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public PrivacyModel(ApplicationDbContext context)
        {
            _context = context;
        }

        // Read from the database (PrivacyPolicyContent) rather than from
        // Pages.Privacy.*.resx, so that the text is editable from the admin
        // panel (the "Privacy Policy" tab).
        public string ContentHtml { get; set; } = string.Empty;
        public DateTime? LastUpdatedAt { get; set; }

        public async Task OnGetAsync()
        {
            var content = await _context.PrivacyPolicyContents.FirstOrDefaultAsync();
            if (content == null)
            {
                ContentHtml = string.Empty;
                return;
            }

            var isBulgarian = System.Globalization.CultureInfo.CurrentUICulture
                .TwoLetterISOLanguageName.Equals("bg", StringComparison.OrdinalIgnoreCase);

            ContentHtml = isBulgarian ? content.ContentBg : content.ContentEn;
            // LastUpdatedAt is UTC in the database; without the conversion the
            // page showed a time behind the Bulgarian clock — the same mistake
            // as in the download file name in SendInvitations. See
            // TimeZoneHelper.
            LastUpdatedAt = TimeZoneHelper.ToLocal(content.LastUpdatedAt);
        }
    }
}