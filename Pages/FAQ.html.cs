using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Pages
{
    public class FAQModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public FAQModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<FaqModel> Faqs { get; set; } = new List<FaqModel>();

        public async Task OnGetAsync()
        {
            // Извличаме само активните въпроси и ги сортираме по DisplayOrder
            Faqs = await _context.Faqs
                .Where(f => f.IsActive)
                .OrderBy(f => f.DisplayOrder)
                .ToListAsync();
        }
    }
}