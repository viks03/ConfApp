using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Pages
{
    [Authorize(Roles = "Admin")]
    public class BugReportsModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public BugReportsModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public List<BugReport> Reports { get; set; } = new();

        public async Task OnGetAsync()
        {
            Reports = await _context.BugReports
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
        }

        // ── Смяна на статус (+ по избор бележка при затваряне) ──────────────
        public async Task<IActionResult> OnPostUpdateStatusAsync(int id, string status, string? resolutionNotes)
        {
            string[] validStatuses = ["Open", "InProgress", "Resolved", "WontFix"];
            if (!validStatuses.Contains(status))
                return BadRequest(new { success = false, message = "Invalid status." });

            var report = await _context.BugReports.FindAsync(id);
            if (report == null)
                return NotFound(new { success = false, message = "Report not found." });

            report.Status = status;

            if (status == "Resolved" || status == "WontFix")
            {
                report.ResolvedAt = DateTime.UtcNow;
                report.ResolvedByEmail = User.Identity?.Name;
                if (!string.IsNullOrWhiteSpace(resolutionNotes))
                    report.ResolutionNotes = resolutionNotes.Trim();
            }
            else
            {
                // Преотваряне на вече затворен репорт — изчистваме resolution
                // данните, за да не остане "остаряла" бележка/дата от преди.
                report.ResolvedAt = null;
                report.ResolvedByEmail = null;
                report.ResolutionNotes = null;
            }

            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }

        // ── Трайно изтриване на един репорт ──────────────────────────────────
        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var report = await _context.BugReports.FindAsync(id);
            if (report == null)
                return NotFound(new { success = false, message = "Report not found." });

            _context.BugReports.Remove(report);
            await _context.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }
    }
}