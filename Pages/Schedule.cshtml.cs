using ConferenceApp.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Pages
{
    public class ScheduleModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        // Използваме пълния път до модела на базата данни
        public List<ConferenceApp.Models.ScheduleModel> Day1Events { get; set; } = new();
        public List<ConferenceApp.Models.ScheduleModel> Day2Events { get; set; } = new();
        public List<ConferenceApp.Models.ScheduleModel> Day3Events { get; set; } = new();

        public ScheduleModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task OnGetAsync()
        {
            var allEvents = await _context.Set<ConferenceApp.Models.ScheduleModel>().ToListAsync();

            // Строго филтриране, за да не се застъпват дните
            Day1Events = allEvents.Where(e => e.Day == "1" || e.Day.Contains("29") || e.Day.ToLower().Contains("day 1"))
                                  .OrderBy(e => e.StartTime).ToList();
                                  
            Day2Events = allEvents.Where(e => e.Day == "2" || e.Day.Contains("30") || e.Day.ToLower().Contains("day 2"))
                                  .OrderBy(e => e.StartTime).ToList();
                                  
            Day3Events = allEvents.Where(e => e.Day == "3" || e.Day.Contains("31") || e.Day.ToLower().Contains("day 3"))
                                  .OrderBy(e => e.StartTime).ToList();
        }
    }
}