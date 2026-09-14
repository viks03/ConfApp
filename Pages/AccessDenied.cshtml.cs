// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Pages
{
    // The namespace has to match the folder: the file used to declare
    // "ConferenceApp.Areas.Identity.Pages.Account" — the scaffolded Identity
    // namespace — while living in Pages/, so
    // "@model ConferenceApp.Pages.AccessDeniedModel" in the view did not resolve
    // to it.
    public class AccessDeniedModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public AccessDeniedModel(ApplicationDbContext context)
        {
            _context = context;
        }

        // [T-25] The line "Need help? Contact us" used to point at
        // /BugReports — an address that does not exist (the page is
        // @page "/Admin/BugReports") and which, even spelled correctly, is for
        // administrators only. So somebody who had just been refused access was
        // refused a second time.
        //
        // The contact address is the one in the footer, read from the same row,
        // so that the two cannot drift apart when it is changed in the panel.
        public string ContactEmail { get; private set; } = string.Empty;

        public async Task OnGetAsync()
        {
            var footer = await _context.FooterContents.AsNoTracking().FirstOrDefaultAsync();
            ContactEmail = footer?.ContactEmail ?? string.Empty;
        }
    }
}
