using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ConferenceApp.Data;
using ConferenceApp.Models;

namespace ConferenceApp.Pages
{
    public class TravelModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public TravelModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public List<HotelModel> Hotels { get; set; } = new();

        public async Task OnGetAsync()
        {
            // Извличаме хотелите от таблицата Hotels
            Hotels = await _context.Hotels.ToListAsync();
        }
    }
}