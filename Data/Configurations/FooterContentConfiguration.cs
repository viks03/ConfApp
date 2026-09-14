// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    // Seeds the single FooterContent row (Id = 1) with sensible defaults at
    // the first migration — exactly what an administrator would have typed in
    // by hand, so that the forms under Site Settings → Footer Content do not
    // start out empty.
    //
    // HasData needs a fixed date: with DateTime.UtcNow the migration would
    // "detect" a change every time it was regenerated.
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
