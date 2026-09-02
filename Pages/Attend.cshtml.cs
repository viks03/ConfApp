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

        // ── Динамични линкове ──
        public bool RedirectJournalistToDocs { get; set; } = false;
        
        // Стойност по подразбиране, ако няма запис в базата данни
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
            // 1. Извличаме билетите от базата данни
            TicketTiers = await _context.TicketTiers.ToListAsync();

            // 2. Проверка за Журналист
            if (_signInManager.IsSignedIn(User))
            {
                var user = await _userManager.GetUserAsync(User);
                if (user != null && user.PartForm == "4")
                {
                    RedirectJournalistToDocs = true;
                }
            }

            // 3. Динамично извличане на линка за онлайн излъчване от новата таблица LinkWatches
            var settings = await _context.LinkWatches.FirstOrDefaultAsync();
            if (settings != null && !string.IsNullOrEmpty(settings.WatchOnlineLink))
            {
                WatchOnlineLink = settings.WatchOnlineLink;
            }
        }
    }
}