using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    // Seed-ва default набор от Quick Links при първата миграция — всички
    // страници на сайта, полезни за посетител (същите 8, които са и в
    // hamburger менюто, плюс Call for Papers). IconSvg е точно вътрешната
    // маркировка на съответната иконка от .mobile-nav-link в
    // _Layout.cshtml (единични кавички вътре, за да не се сблъскват с
    // двойните кавички на самия C# string literal) — гарантира визуална
    // консистентност между hamburger менюто и footer-а за едни и същи
    // страници. Admin може да ги трие/скрива/редактира напълно свободно
    // след това — това е само начална точка, не hardcode-нат списък.
    // Показването на реда на сайта е рандомно (виж _Layout.cshtml), затова
    // няма DisplayOrder тук.
    public class FooterQuickLinkModelConfiguration : IEntityTypeConfiguration<FooterQuickLinkModel>
    {
        public void Configure(EntityTypeBuilder<FooterQuickLinkModel> builder)
        {
            builder.HasData(
                new FooterQuickLinkModel
                {
                    Id = 1,
                    LabelEn = "Home",
                    LabelBg = "Начало",
                    Url = "/Index",
                    IconSvg = "<path d='M3 11l9-8 9 8'></path><path d='M5 10v10a1 1 0 0 0 1 1h4v-6h4v6h4a1 1 0 0 0 1-1V10'></path>",
                    IsVisible = true
                },
                new FooterQuickLinkModel
                {
                    Id = 2,
                    LabelEn = "Conference",
                    LabelBg = "Конференция",
                    Url = "/Conference",
                    IconSvg = "<circle cx='9' cy='8' r='3'></circle><path d='M2 21v-1a6 6 0 0 1 6-6h2a6 6 0 0 1 6 6v1'></path><circle cx='17' cy='8' r='2.5'></circle><path d='M17 13.5c2.5.3 4.5 2.4 4.5 5V21'></path>",
                    IsVisible = true
                },
                new FooterQuickLinkModel
                {
                    Id = 3,
                    LabelEn = "About ICBI",
                    LabelBg = "За ICBI",
                    Url = "/ICBI",
                    IconSvg = "<line x1='3' y1='22' x2='21' y2='22'></line><line x1='6' y1='18' x2='6' y2='11'></line><line x1='10' y1='18' x2='10' y2='11'></line><line x1='14' y1='18' x2='14' y2='11'></line><line x1='18' y1='18' x2='18' y2='11'></line><polygon points='12 2 21 8 3 8'></polygon>",
                    IsVisible = true
                },
                new FooterQuickLinkModel
                {
                    Id = 4,
                    LabelEn = "Lecturers",
                    LabelBg = "Лектори",
                    Url = "/Lecturers",
                    IconSvg = "<circle cx='12' cy='8' r='4'></circle><path d='M4 21c0-4.4 3.6-8 8-8s8 3.6 8 8'></path>",
                    IsVisible = true
                },
                new FooterQuickLinkModel
                {
                    Id = 5,
                    LabelEn = "Schedule",
                    LabelBg = "Програма",
                    Url = "/Schedule",
                    IconSvg = "<rect x='3' y='5' width='18' height='16' rx='2'></rect><line x1='3' y1='10' x2='21' y2='10'></line><line x1='8' y1='2' x2='8' y2='6'></line><line x1='16' y1='2' x2='16' y2='6'></line>",
                    IsVisible = true
                },
                new FooterQuickLinkModel
                {
                    Id = 6,
                    LabelEn = "Attend",
                    LabelBg = "Участие",
                    Url = "/Attend",
                    IconSvg = "<circle cx='9' cy='8' r='4'></circle><path d='M2 21c0-4.4 3.1-8 7-8s7 3.6 7 8'></path><line x1='18' y1='6' x2='18' y2='12'></line><line x1='15' y1='9' x2='21' y2='9'></line>",
                    IsVisible = true
                },
                new FooterQuickLinkModel
                {
                    Id = 7,
                    LabelEn = "Travel",
                    LabelBg = "Пътуване",
                    Url = "/Travel",
                    IconSvg = "<line x1='22' y1='2' x2='11' y2='13'></line><polygon points='22 2 15 22 11 13 2 9 22 2'></polygon>",
                    IsVisible = true
                },
                new FooterQuickLinkModel
                {
                    Id = 8,
                    LabelEn = "FAQ",
                    LabelBg = "FAQ",
                    Url = "/FAQ",
                    IconSvg = "<circle cx='12' cy='12' r='9'></circle><path d='M9.5 9a2.5 2.5 0 1 1 3.4 2.3c-.9.4-1.4 1-1.4 2'></path><line x1='12' y1='17' x2='12.01' y2='17'></line>",
                    IsVisible = true
                },
                new FooterQuickLinkModel
                {
                    Id = 9,
                    LabelEn = "Call for Papers",
                    LabelBg = "Покана за доклади",
                    Url = "/SubmitDocuments",
                    IconSvg = "<path d='M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z'></path><polyline points='14 3 14 9 20 9'></polyline><line x1='12' y1='12' x2='12' y2='18'></line><polyline points='9.5 14.5 12 12 14.5 14.5'></polyline>",
                    IsVisible = true
                }
            );
        }
    }
}
