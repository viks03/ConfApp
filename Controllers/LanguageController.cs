using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Http;

namespace ConferenceApp.Controllers
{
    [Route("[controller]/[action]")]
    public class LanguageController : Controller
    {
        [HttpPost]
        public IActionResult SetLanguage(string culture, string returnUrl)
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

            return LocalRedirect(returnUrl ?? "/");
        }
    }
}