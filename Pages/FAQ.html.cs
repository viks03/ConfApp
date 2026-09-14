// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
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
            // Hidden questions stay in the database; the order is the one set by
            // drag-and-drop in the panel.
            Faqs = await _context.Faqs
                .Where(f => f.IsActive)
                .OrderBy(f => f.DisplayOrder)
                .ToListAsync();
        }
    }
}