// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class SocialLinksSettingConfiguration : IEntityTypeConfiguration<SocialLinksSetting>
    {
        public void Configure(EntityTypeBuilder<SocialLinksSetting> builder)
        {
            // The single social links row. Exactly ONE row (Id = 1) must always
            // exist, so that OnPostSaveSocialLinksAsync can find and update it
            // instead of checking for null every time.
            builder.HasData(
                new SocialLinksSetting { Id = 1 }
            );
        }
    }
}
