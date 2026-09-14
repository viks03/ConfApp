// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
    {
        public void Configure(EntityTypeBuilder<AuditLog> builder)
        {
            // The address is required: it is the only identification left once
            // the account it belonged to has been deleted.
            builder.Property(a => a.UserEmail).IsRequired();

            // [A-08] This is the fastest-growing table in the database — 31
            // places in the project write to it — and at the same time the one
            // /Login queries on every visit: is this IP blocked, how many failed
            // attempts are there. Without an index each such check is a full
            // scan.
            builder.HasIndex(a => new { a.IpAddress, a.Action, a.Timestamp });
            builder.HasIndex(a => new { a.UserEmail, a.Action, a.Timestamp });
        }
    }
}
