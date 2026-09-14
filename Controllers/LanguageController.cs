// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Http;

namespace ConferenceApp.Controllers
{
    [Route("[controller]/[action]")]
    public class LanguageController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<LanguageController> _logger;

        public LanguageController(
            UserManager<ApplicationUser> userManager,
            ILogger<LanguageController> logger)
        {
            _userManager = userManager;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> SetLanguage(string? culture, string? returnUrl)
        {
            if (string.IsNullOrEmpty(culture))
                culture = "bg";

            var cookieValue = CookieRequestCultureProvider.MakeCookieValue(
                new RequestCulture(culture));

            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                cookieValue,
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    IsEssential = true,
                    Path = "/"
                });

            await RememberForMailAsync(culture);

            // This used to be `LocalRedirect(returnUrl ?? "/")`, where an empty
            // string or a foreign address threw and returned 500. The form in
            // _Layout does pass a value, so the check goes through
            // Url.IsLocalUrl rather than a null check.
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
        }

        /// <summary>
        /// [T-29] The cookie applies to the browser carrying it. Mail, however,
        /// also goes out when that browser is nowhere in sight — an
        /// administrator approves a verification, Stripe sends a webhook — so
        /// the choice is stored on the user as well.
        /// <para>
        /// Switching language must not fail because of this: an unknown
        /// language, a visitor who is not signed in, or a refused write simply
        /// change nothing. The cookie is already set and the page reloads in the
        /// new language whatever happens here.
        /// </para>
        /// </summary>
        private async Task RememberForMailAsync(string culture)
        {
            var language = ConferenceApp.Services.Email.MailContext.Normalize(culture);
            if (language is null) return;

            if (User?.Identity?.IsAuthenticated != true) return;

            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user is null || user.PreferredLanguage == language) return;

                user.PreferredLanguage = language;
                var result = await _userManager.UpdateAsync(user);

                if (!result.Succeeded)
                    _logger.LogWarning(
                        "Езикът {Language} на {Email} не се записа: {Errors}",
                        language, user.Email,
                        string.Join("; ", result.Errors.Select(e => e.Description)));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Не можах да запиша избрания език {Language}. Бисквитката е сложена.",
                    language);
            }
        }
    }
}
