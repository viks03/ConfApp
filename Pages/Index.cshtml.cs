// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ConferenceApp.Models;

namespace ConferenceApp.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly ApplicationDbContext _context;

        // The four lecturers rendered on the server.
        public List<LecturerModel> TopLecturers { get; set; } = new();

        // The whole set, which the page's JavaScript rotates through.
        public List<LecturerModel> AllLecturers { get; set; } = new();

        public List<HomePageLogo> PartnersLogos { get; set; } = new();

        public IndexModel(ILogger<IndexModel> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task OnGetAsync()
        {
            var rng = new Random();

            // Shuffled on every request, so that the four shown first are not
            // always the same four people. The shuffle happens in memory: the
            // whole list is needed anyway for the rotation.
            AllLecturers = (await _context.Lecturers.ToListAsync())
                .OrderBy(_ => rng.Next())
                .ToList();

            TopLecturers = AllLecturers.Take(4).ToList();

            PartnersLogos = await _context.HomePageLogos.ToListAsync();
        }
    }
}