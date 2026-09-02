using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class CookieCategoryConfiguration : IEntityTypeConfiguration<CookieCategory>
    {
        public void Configure(EntityTypeBuilder<CookieCategory> builder)
        {
            // Key трябва да е уникален (използва се от JavaScript логиката като
            // стабилен идентификатор). DisplayOrder определя подредбата в
            // модала за предпочитания.
            builder.HasIndex(c => c.Key).IsUnique();
            builder.HasIndex(c => c.DisplayOrder);

            // Добавяне на 4-те стандартни категории бисквитки.
            // Текстовете са мигрирани директно от Pages/Shared/_DataNotice.cshtml,
            // за да се запази визуалната консистентност след внедряване.
            // Категорията "necessary" задължително е с IsToggleable = false и
            // DefaultOn = true, което се валидира и на сървъра (вж. Index.cshtml.cs).
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