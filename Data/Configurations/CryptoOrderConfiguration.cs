// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class CryptoOrderConfiguration : IEntityTypeConfiguration<CryptoOrder>
    {
        public void Configure(EntityTypeBuilder<CryptoOrder> builder)
        {
            // SQLite has no decimal type; EF stores it as TEXT. Saying so
            // explicitly silences the model validation warning.
            builder.Property(o => o.AmountEUR).HasColumnType("TEXT");

            // The two ways an order is looked up: by participant (the page) and
            // by gateway id (the webhook and the polling).
            builder.HasIndex(o => o.UserId);
            builder.HasIndex(o => o.Go28OrderId);
            // [D-06] SetNull, not Cascade. The panel deletes participants
            // regardless of payment status, and the cascade took the whole
            // crypto history with them. An order now outlives the user, the same
            // way an AuditLogs row does.
            builder.HasOne(o => o.User)
                .WithMany()
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
