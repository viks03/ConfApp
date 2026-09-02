using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class InvitationSendLogConfiguration : IEntityTypeConfiguration<InvitationSendLog>
    {
        public void Configure(EntityTypeBuilder<InvitationSendLog> builder)
        {
            // Индекси за History таба (най-новите отгоре, и филтриране по
            // конкретен batch), плюс уникален индекс за бързо и сигурно
            // намиране по TrackingToken (виж TrackingController).
            builder.HasIndex(l => l.SentAt);
            builder.HasIndex(l => l.BatchId);
            builder.HasIndex(l => l.TrackingToken).IsUnique();
        }
    }
}
