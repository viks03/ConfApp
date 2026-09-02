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

        // Първите 4 лектора за initial render (разбъркани)
        public List<LecturerModel> TopLecturers { get; set; } = new();

        // Всички лектори за JS ротацията
        public List<LecturerModel> AllLecturers { get; set; } = new();

        // ДОБАВЕНО: Списък, който ще държи логата на партньорите за началната страница
        public List<HomePageLogo> PartnersLogos { get; set; } = new();

        public IndexModel(ILogger<IndexModel> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
        }

        public async Task OnGetAsync()
        {
            var rng = new Random();

            // Зареждаме всички лектори и ги разбъркваме
            AllLecturers = (await _context.Lecturers.ToListAsync())
                .OrderBy(_ => rng.Next())
                .ToList();

            // Първите 4 от разбърканите за initial render
            TopLecturers = AllLecturers.Take(4).ToList();

            // ДОБАВЕНО: Взимаме всички лога за началната страница от базата данни
            PartnersLogos = await _context.HomePageLogos.ToListAsync();
        }
    }
}