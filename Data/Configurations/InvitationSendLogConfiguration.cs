// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class InvitationSendLogConfiguration : IEntityTypeConfiguration<InvitationSendLog>
    {
        public void Configure(EntityTypeBuilder<InvitationSendLog> builder)
        {
            // For the History tab: newest first, and filtering by one batch.
            // The unique index on TrackingToken is what the anonymous tracking
            // endpoints look a row up by (see TrackingController).
            builder.HasIndex(l => l.SentAt);
            builder.HasIndex(l => l.BatchId);
            builder.HasIndex(l => l.TrackingToken).IsUnique();
        }
    }
}
