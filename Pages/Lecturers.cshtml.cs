using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Pages
{
    public class LecturersModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public List<LecturerModel> Lecturers { get; set; } = new();

        public LecturersModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task OnGetAsync()
        {
            // Извличаме всички лектори от базата данни
            Lecturers = await _context.Lecturers.ToListAsync();
        }
    }
}