using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class HomePageLogoConfiguration : IEntityTypeConfiguration<HomePageLogo>
    {
        public void Configure(EntityTypeBuilder<HomePageLogo> builder)
        {
            // SEED данни за логата на началната страница
            builder.HasData(
                new HomePageLogo
                {
                    Id = 1,
                    ImagePath = "/uploads/homepagelogos/GO28.png",
                    PartnerName = "GO28"
                },
                new HomePageLogo
                {
                    Id = 2,
                    ImagePath = "/uploads/homepagelogos/UNWE.png",
                    PartnerName = "NABI"
                },
                new HomePageLogo
                {
                    Id = 3,
                    ImagePath = "/uploads/homepagelogos/NABI.png",
                    PartnerName = "UNWE"
                },
                new HomePageLogo
                {
                    Id = 4,
                    ImagePath = "/uploads/homepagelogos/BurgasUNI.png",
                    PartnerName = "Bourgas University"
                }
            );
        }
    }
}
