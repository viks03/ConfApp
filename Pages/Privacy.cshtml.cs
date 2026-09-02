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

        // Вече чете от базата (PrivacyPolicyContent), не от Pages.Privacy.*.resx —
        // редактируемо от админ панела (таб "Privacy Policy").
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
            // FIX: LastUpdatedAt е UTC в базата — без конверсия страницата показваше
            // часа назад спрямо реалното българско време (същия бъг като файла за
            // сваляне в BugReportController — виж TimeZoneHelper).
            LastUpdatedAt = TimeZoneHelper.ToLocal(content.LastUpdatedAt);
        }
    }
}