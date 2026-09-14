// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class CookieCategoryConfiguration : IEntityTypeConfiguration<CookieCategory>
    {
        public void Configure(EntityTypeBuilder<CookieCategory> builder)
        {
            // Key has to be unique: the JavaScript uses it as a stable
            // identifier, and the stored consent is keyed on it. DisplayOrder
            // sets the order in the preferences dialog.
            builder.HasIndex(c => c.Key).IsUnique();
            builder.HasIndex(c => c.DisplayOrder);

            // The four standard categories. The texts were moved across from
            // Pages/Shared/_DataNotice.cshtml verbatim, so that nothing changed
            // on screen the day this shipped.
            // "necessary" must have IsToggleable = false and DefaultOn = true;
            // the panel enforces that server-side as well (see Index.cshtml.cs).
            builder.HasData(
                new CookieCategory
                {
                    Id = 1,
                    Key = "necessary",
                    DisplayOrder = 1,
                    NameEn = "Strictly Necessary",
                    NameBg = "Строго необходими",
                    DescriptionEn = "Required for core website functionality, including login sessions, security features, and load balancing. These cookies cannot be disabled.",
                    DescriptionBg = "Необходими за основната функционалност на сайта, включително сесии за вход, сигурност и балансиране на натоварването. Тези бисквитки не могат да бъдат изключени.",
                    IsVisible = true,
                    IsToggleable = false,
                    DefaultOn = true,
                    IsBuiltIn = true
                },
                new CookieCategory
                {
                    Id = 2,
                    Key = "analytics",
                    DisplayOrder = 2,
                    NameEn = "Analytics",
                    NameBg = "Анализи",
                    DescriptionEn = "Helps us understand how visitors interact with the site, such as pages viewed and time spent, so we can continuously improve the user experience.",
                    DescriptionBg = "Помагат ни да разберем как посетителите взаимодействат със сайта, като отчитат кои страници се разглеждат и колко време се прекарва в тях, за да можем да го подобряваме.",
                    IsVisible = true,
                    IsToggleable = true,
                    DefaultOn = false,
                    IsBuiltIn = true
                },
                new CookieCategory
                {
                    Id = 3,
                    Key = "marketing",
                    DisplayOrder = 3,
                    NameEn = "Marketing",
                    NameBg = "Маркетинг",
                    DescriptionEn = "Used to measure the effectiveness of our marketing campaigns and to display relevant information about the conference on other platforms.",
                    DescriptionBg = "Използват се за измерване на ефективността на нашите маркетингови кампании, както и за показване на подходяща информация за конференцията в други платформи.",
                    IsVisible = true,
                    IsToggleable = true,
                    DefaultOn = false,
                    IsBuiltIn = true
                },
                new CookieCategory
                {
                    Id = 4,
                    Key = "preferences",
                    DisplayOrder = 4,
                    NameEn = "Preferences",
                    NameBg = "Предпочитания",
                    DescriptionEn = "Remembers choices you have made, such as your selected language, to provide a more personalized and consistent experience.",
                    DescriptionBg = "Запомнят направените от вас избори, като например предпочитан език, за да осигурят по-персонализирано и последователно изживяване.",
                    IsVisible = true,
                    IsToggleable = true,
                    DefaultOn = false,
                    IsBuiltIn = true
                }
            );
        }
    }
}