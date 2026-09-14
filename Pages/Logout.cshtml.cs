// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConferenceApp.Pages
{
    // [IgnoreAntiforgeryToken] switches the antiforgery check OFF for this
    // page; it does not restrict the page to POST. Signing out is POST-only
    // because OnGet below merely redirects — see [T-06] — so a cross-site
    // <img> tag cannot end a session, but a cross-site form post can. Why the
    // check is disabled here is not recorded anywhere; see COMMENTS.md.
    [IgnoreAntiforgeryToken] 
    public class LogoutModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<LogoutModel> _logger;
        private readonly ConferenceApp.Services.AuditService _audit;

        public LogoutModel(
            SignInManager<ApplicationUser> signInManager, 
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            ILogger<LogoutModel> logger,
            ConferenceApp.Services.AuditService audit)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _context = context;
            _logger = logger;
            _audit = audit;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Read before the session is destroyed: afterwards User is anonymous
            // and there is nobody to write the audit row about.
            var user = await _userManager.GetUserAsync(User);
            
            if (user != null)
            {
                await _audit.LogAsync(user.Id, user.Email ?? "Unknown", "Logout",
                    "User successfully logged out.");

                // [T-05] SignOutAsync deletes the cookie from THIS browser but
                // does not invalidate the cookie itself: a copy taken before the
                // sign-out kept opening the profile until it expired, 30 days
                // later. Rolling the security stamp changes the fingerprint
                // SecurityStampValidator checks on every request, so every
                // credential already issued stops working.
                //
                // A deliberate consequence: signing out here signs the person
                // out on their other devices too.
                await _userManager.UpdateSecurityStampAsync(user);
            }

            await _signInManager.SignOutAsync();
            
            // SignOutAsync already expires the cookie; deleting it by name as
            // well covers a browser that kept a copy under the old settings.
            HttpContext.Response.Cookies.Delete(".AspNetCore.Identity.Application");
            
            _logger.LogInformation("User logged out and redirected to Login.");
            
            return RedirectToPage("/Login");
        }

        // [T-06] A GET does NOT sign anybody out. This method used to call
        // OnPostAsync, so a foreign page with <img src="…/Logout"> threw the
        // user out without them asking. Every sign-out in the application goes
        // through a POST form anyway (see _Layout.cshtml and Profile.cshtml), so
        // all that is left here is a redirect.
        public IActionResult OnGet()
        {
            return User.Identity?.IsAuthenticated == true
                ? RedirectToPage("/Profile")
                : RedirectToPage("/Login");
        }
    }
}