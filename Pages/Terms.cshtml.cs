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

        // Вече чете от базата (TermsOfUseContent), не от Pages.Terms.*.resx —
        // редактируемо от админ панела (таб "Terms of Use"). Огледално на
        // PrivacyModel.
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
            // Виж коментара в PrivacyModel — LastUpdatedAt е UTC в базата, преобразуваме
            // към локално време преди да покажем на страницата.
            LastUpdatedAt = TimeZoneHelper.ToLocal(content.LastUpdatedAt);
        }
    }
}
