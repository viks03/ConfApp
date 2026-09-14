// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Pages
{
    public class ScheduleModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        // Fully qualified: the page model and the entity share the name
        // ScheduleModel.
        public List<ConferenceApp.Models.ScheduleModel> Day1Events { get; set; } = new();
        public List<ConferenceApp.Models.ScheduleModel> Day2Events { get; set; } = new();
        public List<ConferenceApp.Models.ScheduleModel> Day3Events { get; set; } = new();

        /// <summary>The downloadable programme, uploaded from the admin panel.
        /// Either may be null — the view then omits that button.</summary>
        public ConferenceApp.Models.DownloadableFile? ProgrammeBg { get; set; }
        public ConferenceApp.Models.DownloadableFile? ProgrammeEn { get; set; }

        public ScheduleModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task OnGetAsync()
        {
            // The paths used to be written into the markup. Replacing the file
            // then meant uploading it over FTP under EXACTLY the same name, and
            // a mistyped name meant a button that led nowhere, with no error
            // anywhere.
            var files = await _context.Set<ConferenceApp.Models.DownloadableFile>()
                            .AsNoTracking()
                            .Where(f => f.FileKey == ConferenceApp.Models.DownloadableFile.ProgrammeBg
                                     || f.FileKey == ConferenceApp.Models.DownloadableFile.ProgrammeEn)
                            .ToListAsync();

            ProgrammeBg = files.FirstOrDefault(f => f.FileKey == ConferenceApp.Models.DownloadableFile.ProgrammeBg);
            ProgrammeEn = files.FirstOrDefault(f => f.FileKey == ConferenceApp.Models.DownloadableFile.ProgrammeEn);

            var allEvents = await _context.Set<ConferenceApp.Models.ScheduleModel>().ToListAsync();

            // Day is free text typed in the panel, so each day is matched three
            // ways: the bare number, the date of that day of the conference
            // (29, 30, 31 October), and "day N". The filtering runs in memory
            // because SQLite cannot do a case-insensitive Contains in SQL.
            //
            // The dates are the ones this conference is held on; a different
            // date means this page has to change too.
            Day1Events = allEvents.Where(e => e.Day == "1" || e.Day.Contains("29") || e.Day.ToLower().Contains("day 1"))
                                  .OrderBy(e => e.StartTime).ToList();
                                  
            Day2Events = allEvents.Where(e => e.Day == "2" || e.Day.Contains("30") || e.Day.ToLower().Contains("day 2"))
                                  .OrderBy(e => e.StartTime).ToList();
                                  
            Day3Events = allEvents.Where(e => e.Day == "3" || e.Day.Contains("31") || e.Day.ToLower().Contains("day 3"))
                                  .OrderBy(e => e.StartTime).ToList();
        }
    }
}