namespace ConferenceApp.Helpers
{
    using Microsoft.AspNetCore.Localization;

    public static class CultureHelper
    {
        public static string GetCulture(HttpContext context)
        {
            return context.Features.Get<IRequestCultureFeature>()?
                .RequestCulture.UICulture
                .TwoLetterISOLanguageName ?? "en";
        }
    }
}
