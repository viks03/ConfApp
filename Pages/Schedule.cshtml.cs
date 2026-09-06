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

        /// <summary>Програмата за сваляне — качва се от админ панела.</summary>
        public ConferenceApp.Models.DownloadableFile? ProgrammeBg { get; set; }
        public ConferenceApp.Models.DownloadableFile? ProgrammeEn { get; set; }

        public ScheduleModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task OnGetAsync()
        {
            // Пътищата бяха твърдо изписани в разметката. При смяна на файла
            // трябваше да се качи по FTP с ТОЧНО същото име — сбъркано име
            // значеше бутон, който води наникъде, без грешка никъде.
            var files = await _context.Set<ConferenceApp.Models.DownloadableFile>()
                            .AsNoTracking()
                            .Where(f => f.FileKey == ConferenceApp.Models.DownloadableFile.ProgrammeBg
                                     || f.FileKey == ConferenceApp.Models.DownloadableFile.ProgrammeEn)
                            .ToListAsync();

            ProgrammeBg = files.FirstOrDefault(f => f.FileKey == ConferenceApp.Models.DownloadableFile.ProgrammeBg);
            ProgrammeEn = files.FirstOrDefault(f => f.FileKey == ConferenceApp.Models.DownloadableFile.ProgrammeEn);

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