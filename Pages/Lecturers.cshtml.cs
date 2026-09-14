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
            // No filtering and no ordering here: the view groups the lecturers
            // by Category itself.
            Lecturers = await _context.Lecturers.ToListAsync();
        }
    }
}