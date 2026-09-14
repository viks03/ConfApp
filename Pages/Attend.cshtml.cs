// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Pages
{
    public class AttendModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public List<TicketTierModel> TicketTiers { get; set; } = new();

        // A journalist has nothing to buy: the page sends them to the
        // verification documents instead of to a ticket tier.
        public bool RedirectJournalistToDocs { get; set; } = false;
        
        // "#" until an administrator sets the link: a href that goes nowhere
        // rather than a broken one.
        public string WatchOnlineLink { get; set; } = "#"; 

        public AttendModel(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager)
        {
            _context = context;
            _userManager = userManager;
            _signInManager = signInManager;
        }

        public async Task OnGetAsync()
        {
            TicketTiers = await _context.TicketTiers.ToListAsync();

            // PartForm "4" is the journalist form.
            if (_signInManager.IsSignedIn(User))
            {
                var user = await _userManager.GetUserAsync(User);
                if (user != null && user.PartForm == "4")
                {
                    RedirectJournalistToDocs = true;
                }
            }

            // The row may not exist at all — it is created the first time an
            // administrator saves the link.
            var settings = await _context.LinkWatches.FirstOrDefaultAsync();
            if (settings != null && !string.IsNullOrEmpty(settings.WatchOnlineLink))
            {
                WatchOnlineLink = settings.WatchOnlineLink;
            }
        }
    }
}