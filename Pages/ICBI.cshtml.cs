using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Pages
{
    public class ICBIModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        // Списък, който ще държи събитията за секцията "Track Record"
        public List<EventModel> Events { get; set; } = new();

        public ICBIModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task OnGetAsync()
        {
            // Взимаме всички събития от базата данни
            Events = await _context.Events.ToListAsync();
        }
    }
}