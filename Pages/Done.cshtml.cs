// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Helpers;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConferenceApp.Pages
{
    public class DoneModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public DoneModel(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public string DisplayEmail { get; set; } = string.Empty;
        public string FormattedDate { get; set; } = string.Empty;
        public int CountdownSeconds { get; set; } = 5;

        public async Task<IActionResult> OnGetAsync()
        {
            // /Done is the last step of the registration flow, not a page of its
            // own: only purpose == "Registration" redirects here from
            // Verification.cshtml.cs, which signs the user in just before doing
            // so. Anyone who opens /Done directly goes to Login.
            if (User.Identity?.IsAuthenticated != true)
            {
                return RedirectToPage("/Login");
            }

            // The page has to be reachable ONCE, straight after a successful
            // registration — not whenever a signed-in person types the address.
            // TempData is read exactly once (Verification.cshtml.cs sets the flag
            // immediately before redirecting here and never calls .Keep()), so a
            // direct GET finds nothing.
            //
            // It also means reloading /Done with F5 goes to /Profile instead of
            // replaying the countdown, which is what a one-off welcome page
            // should do.
            if (TempData["JustRegistered"] as bool? != true)
            {
                return RedirectToPage("/Profile");
            }

            // no-store, for the same reason as on Verification: the back button
            // must not redisplay a cached copy of this one-off page with its old
            // countdown.
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

            DisplayEmail = EmailMaskHelper.Mask(user.Email ?? string.Empty);
            FormattedDate = TimeZoneHelper.ToLocal(DateTime.UtcNow).ToString("dd.MM.yyyy");

            return Page();
        }
    }
}
