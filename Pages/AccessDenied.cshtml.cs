using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConferenceApp.Pages
{
    // БЪГ ФИКС: namespace-ът беше "ConferenceApp.Areas.Identity.Pages.Account"
    // (стандартният scaffold-нат Identity boilerplate namespace), но файлът
    // физически е в Pages/, не в Areas/Identity/Pages/Account/ — явно е бил
    // копиран/преместен по-рано без да се оправи namespace-ът. Затова
    // "@model ConferenceApp.Pages.AccessDeniedModel" в новото AccessDenied.cshtml
    // не намираше класа — реално съществуващата пълна класа беше
    // ConferenceApp.Areas.Identity.Pages.Account.AccessDeniedModel, различна
    // от гледна точка на компилатора.
    public class AccessDeniedModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
