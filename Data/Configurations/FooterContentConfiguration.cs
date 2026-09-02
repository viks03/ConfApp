using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    // Seed-ва единствения FooterContent ред (Id = 1) с разумни default
    // стойности при първата миграция — точно каквото щеше да въведе admin
    // ръчно, само за да не тръгват формите в Site Settings → Footer
    // Content напълно празни. HasData изисква фиксирана дата (не
    // DateTime.UtcNow — иначе миграцията "открива" промяна при всяко
    // regenerate).
    public class FooterContentConfiguration : IEntityTypeConfiguration<FooterContent>
    {
        public void Configure(EntityTypeBuilder<FooterContent> builder)
        {
            builder.HasData(new FooterContent
            {
                Id = 1,
                BrandTaglineEn = "Shapes the future of finance education",
                BrandTaglineBg = "Оформя бъдещето на финансовото образование",
                OrgNoteEn = "Organized by the Institute of Cryptoeconomics, Blockchain and Innovations (ICBI) within the University of National and World Economy (UNWE).",
                OrgNoteBg = "Организирано от Института по криптоикономика, блокчейн и иновации (ICBI) към Университета за национално и световно стопанство (УНСС).",
                ContactLocationEn = "Sofia, Bulgaria",
                ContactLocationBg = "София, България",
                ContactEmail = "conference.education@unwe.bg",
                ContactPhone = "+359 98 871 1801",
                LastUpdatedAt = new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc)
            });
        }
    }
}
