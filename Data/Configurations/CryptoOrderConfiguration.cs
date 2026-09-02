using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class CryptoOrderConfiguration : IEntityTypeConfiguration<CryptoOrder>
    {
        public void Configure(EntityTypeBuilder<CryptoOrder> builder)
        {
            // Индекси за бързо търсене по UserId и Go28OrderId
            builder.HasIndex(o => o.UserId);
            builder.HasIndex(o => o.Go28OrderId);
            builder.HasOne(o => o.User)
                .WithMany()
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
