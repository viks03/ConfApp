using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Pages
{
    public class CookiesModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public CookiesModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public List<CookieCategory> Categories { get; set; } = new();
        public string PolicyHtml { get; set; } = string.Empty;
        public bool IsBg { get; set; }

        public async Task OnGetAsync()
        {
            IsBg = System.Globalization.CultureInfo.CurrentUICulture
                .TwoLetterISOLanguageName.Equals("bg", StringComparison.OrdinalIgnoreCase);

            Categories = await _context.CookieCategories
                .Where(c => c.IsVisible)
                .OrderBy(c => c.DisplayOrder)
                .ToListAsync();

            var policy = await _context.CookiePolicyContents.FirstOrDefaultAsync();
            PolicyHtml = policy == null ? "" : (IsBg ? policy.ContentBg : policy.ContentEn);
        }
    }
}
