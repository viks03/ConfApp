// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class TicketTierModelConfiguration : IEntityTypeConfiguration<TicketTierModel>
    {
        public void Configure(EntityTypeBuilder<TicketTierModel> builder)
        {
            // SQLite has no numeric type for decimal — EF stores it as TEXT with
            // a reversible conversion. Saying so explicitly silences the model
            // validation warning. The amounts are compared in memory rather than
            // in SQL, so the lack of numeric comparison in the database does not
            // matter here.
            builder.Property(t => t.RegularPriceEUR).HasColumnType("TEXT");
            builder.Property(t => t.PromoPriceEUR).HasColumnType("TEXT");

            // The key has to be unique: it is how a tier is found, and a
            // duplicate would make the lookup arbitrary.
            builder.HasIndex(t => t.TierKey).IsUnique();

            // The tiers the site starts with. Prices and texts are edited from
            // the panel; TierKey is not, because the code depends on it.
            builder.HasData(
                new TicketTierModel
                {
                    Id = 1,
                    TierKey = "viewer",
                    NameEn = "Viewer Pass",
                    NameBg = "Пропуск за зрител",
                    DescriptionEn = "On-site access to the core conference program at UNWE. Join the audience to listen, learn, and experience the presentations in person.",
                    DescriptionBg = "Присъствен достъп до основната програма на конференцията в УНСС. Присъединете се към публиката, за да слушате, да научите нови неща и да проследите презентациите на място",
                    RegularPriceEn = "Free",
                    RegularPriceBg = "Безплатно",
                    PromoPriceEn = null,
                    PromoPriceBg = null,
                    RegularPriceEUR = null,   // безплатно — не е платимо ниво
                    PromoPriceEUR = null,
                    PerksEn = "- On-site access to all presentation sessions\r\n- Access to the moderated Q&A discussions\r\n- Access to open networking areas",
                    PerksBg = "- Присъствен достъп до всички презентационни сесии\r\n- Достъп до модерираните дискусии с въпроси и отговори (Q&A)\r\n- Достъп до отворените нетуъркинг зони"
                },
                new TicketTierModel
                {
                    Id = 2,
                    TierKey = "earlybird",
                    NameEn = "Early Bird Ticket",
                    NameBg = "Билет за ранно записване",
                    DescriptionEn = "Full on-site experience for presenting authors, academics, and industry professionals.",
                    DescriptionBg = "Пълен присъствен достъп за автори, академици и професионалисти от индустрията.",
                    RegularPriceEn = "€100",
                    RegularPriceBg = "€100",
                    PromoPriceEn = "€60",
                    PromoPriceBg = "€60",
                    RegularPriceEUR = 100m,
                    PromoPriceEUR = 60m,
                    PerksEn = "- Paper presentation slot (On-site or Online)\r\n- Publication opportunity in the official conference proceedings\r\n- Full on-site access & networking opportunities\r\n- Conference materials, catering & coffee breaks included",
                    PerksBg = "- Слот за представяне на доклад (на място или онлайн)\r\n- Възможност за публикуване в официалния сборник с доклади от конференцията\r\n- Пълен достъп на място и възможности за нетуъркинг\r\n- Включени конференция материали, кетъринг и кафе-паузи"
                },
                new TicketTierModel
                {
                    Id = 3,
                    TierKey = "student",
                    NameEn = "Students & PhD",
                    NameBg = "Студенти и докторанти",
                    DescriptionEn = "Subsidized access for young researchers presenting a paper. Valid student ID required.",
                    DescriptionBg = "Преференциален достъп за млади изследователи, представящи доклад. Изисква се валидна студентска лична карта/книжка.",
                    RegularPriceEn = "Fully Subsidized",
                    RegularPriceBg = "Напълно субсидиран",
                    PromoPriceEn = null,
                    PromoPriceBg = null,
                    RegularPriceEUR = null,   // субсидирано — не е платимо ниво
                    PromoPriceEUR = null,
                    PerksEn = "- Paper presentation slot (On-site or Online)\r\n- Publication opportunity in the official conference proceedings\r\n- Full on-site access & networking opportunities\r\n- Conference materials, catering & coffee breaks included",
                    PerksBg = "- Слот за представяне на доклад (на място или онлайн)\r\n- Възможност за публикуване в официалния сборник с доклади от конференцията\r\n- Пълен достъп на място и възможности за нетуъркинг\r\n- Включени конферентни материали, кетъринг и кафе-паузи"
                }
            );
        }
    }
}
