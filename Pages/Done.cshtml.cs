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
            // /Done е само крайната стъпка на регистрационния flow (виж
            // редиректа в Verification.cshtml.cs — само purpose=="Registration"
            // стига дотук), не самостоятелна публична страница. Изисква
            // потребителят вече да е signed-in (случва се в OnPostAsync на
            // Verification, точно преди редиректа насам) — ако някой отвори
            // /Done директно без да мине оттам, връщаме към Login.
            if (User.Identity?.IsAuthenticated != true)
            {
                return RedirectToPage("/Login");
            }

            // БЪГ ФИКС: /Done трябва да се вижда САМО веднага след успешна
            // регистрация, не като страница, която всеки логнат потребител
            // може да отвори по всяко време от адресната лента. TempData се
            // чете само веднъж (Verification.cshtml.cs слага флага точно
            // преди редиректа насам, без .Keep()) — директен GET тук без да
            // си минал през флоу-а просто няма да намери флага.
            // Странична полза: ако презаредиш /Done ръчно (F5), веднага
            // отиваш в /Profile вместо да видиш пак countdown анимацията —
            // очаквано поведение за еднократна "добре дошъл" страница.
            if (TempData["JustRegistered"] as bool? != true)
            {
                return RedirectToPage("/Profile");
            }

            // Cache-Control: no-store — същата причина като на Verification:
            // back-button не трябва да показва "заредена" версия на тази
            // еднократна welcome страница със стар countdown.
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
