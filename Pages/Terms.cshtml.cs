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
    public class TermsModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public TermsModel(ApplicationDbContext context)
        {
            _context = context;
        }

        // Read from the database (TermsOfUseContent) rather than from
        // Pages.Terms.*.resx, so that the text is editable from the admin panel
        // (the "Terms of Use" tab). Mirrors PrivacyModel.
        public string ContentHtml { get; set; } = string.Empty;
        public DateTime? LastUpdatedAt { get; set; }

        public async Task OnGetAsync()
        {
            var content = await _context.TermsOfUseContents.FirstOrDefaultAsync();
            if (content == null)
            {
                ContentHtml = string.Empty;
                return;
            }

            var isBulgarian = System.Globalization.CultureInfo.CurrentUICulture
                .TwoLetterISOLanguageName.Equals("bg", StringComparison.OrdinalIgnoreCase);

            ContentHtml = isBulgarian ? content.ContentBg : content.ContentEn;
            // LastUpdatedAt is UTC in the database; without the conversion the
            // page shows a time two or three hours behind. See PrivacyModel.
            LastUpdatedAt = TimeZoneHelper.ToLocal(content.LastUpdatedAt);
        }
    }
}
