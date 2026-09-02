using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class TicketTierModelConfiguration : IEntityTypeConfiguration<TicketTierModel>
    {
        public void Configure(EntityTypeBuilder<TicketTierModel> builder)
        {
            // SEED данни за билетите
            builder.HasData(
                new TicketTierModel
                {
                    Id = 1,
                    NameEn = "Viewer Pass",
                    NameBg = "Пропуск за зрител",
                    DescriptionEn = "On-site access to the core conference program at UNWE. Join the audience to listen, learn, and experience the presentations in person.",
                    DescriptionBg = "Присъствен достъп до основната програма на конференцията в УНСС. Присъединете се към публиката, за да слушате, да научите нови неща и да проследите презентациите на място",
                    RegularPriceEn = "Free",
                    RegularPriceBg = "Безплатно",
                    PromoPriceEn = null,
                    PromoPriceBg = null,
                    PerksEn = "- On-site access to all presentation sessions\r\n- Access to the moderated Q&A discussions\r\n- Access to open networking areas",
                    PerksBg = "- Присъствен достъп до всички презентационни сесии\r\n- Достъп до модерираните дискусии с въпроси и отговори (Q&A)\r\n- Достъп до отворените нетуъркинг зони"
                },
                new TicketTierModel
                {
                    Id = 2,
                    NameEn = "Early Bird Ticket",
                    NameBg = "Билет за ранно записване",
                    DescriptionEn = "Full on-site experience for presenting authors, academics, and industry professionals.",
                    DescriptionBg = "Пълен присъствен достъп за автори, академици и професионалисти от индустрията.",
                    RegularPriceEn = "€100",
                    RegularPriceBg = "€100",
                    PromoPriceEn = "€60",
                    PromoPriceBg = "€60",
                    PerksEn = "- Paper presentation slot (On-site or Online)\r\n- Publication opportunity in the official conference proceedings\r\n- Full on-site access & networking opportunities\r\n- Conference materials, catering & coffee breaks included",
                    PerksBg = "- Слот за представяне на доклад (на място или онлайн)\r\n- Възможност за публикуване в официалния сборник с доклади от конференцията\r\n- Пълен достъп на място и възможности за нетуъркинг\r\n- Включени конференция материали, кетъринг и кафе-паузи"
                },
                new TicketTierModel
                {
                    Id = 3,
                    NameEn = "Students & PhD",
                    NameBg = "Студенти и докторанти",
                    DescriptionEn = "Subsidized access for young researchers presenting a paper. Valid student ID required.",
                    DescriptionBg = "Преференциален достъп за млади изследователи, представящи доклад. Изисква се валидна студентска лична карта/книжка.",
                    RegularPriceEn = "Fully Subsidized",
                    RegularPriceBg = "Напълно субсидиран",
                    PromoPriceEn = null,
                    PromoPriceBg = null,
                    PerksEn = "- Paper presentation slot (On-site or Online)\r\n- Publication opportunity in the official conference proceedings\r\n- Full on-site access & networking opportunities\r\n- Conference materials, catering & coffee breaks included",
                    PerksBg = "- Слот за представяне на доклад (на място или онлайн)\r\n- Възможност за публикуване в официалния сборник с доклади от конференцията\r\n- Пълен достъп на място и възможности за нетуъркинг\r\n- Включени конферентни материали, кетъринг и кафе-паузи"
                }
            );
        }
    }
}
