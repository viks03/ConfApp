using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class SocialLinksSettingConfiguration : IEntityTypeConfiguration<SocialLinksSetting>
    {
        public void Configure(EntityTypeBuilder<SocialLinksSetting> builder)
        {
            // SEED: singleton ред за социалните мрежи — винаги трябва да
            // съществува точно ЕДИН ред (Id = 1), за да може
            // OnPostSaveSocialLinksAsync да го намери и обнови, вместо да
            // проверява за null всеки път.
            builder.HasData(
                new SocialLinksSetting { Id = 1 }
            );
        }
    }
}
