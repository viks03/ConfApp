// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class OtpCodeConfiguration : IEntityTypeConfiguration<OtpCode>
    {
        public void Configure(EntityTypeBuilder<OtpCode> builder)
        {
            // [A-08] The table had no index at all, while Login and
            // Verification query it on every code sent (CountAsync over
            // Email + Purpose + CreatedAt) and on every check
            // (OrderByDescending(CreatedAt) over Email + Purpose + IsUsed).
            builder.HasIndex(o => new { o.Email, o.Purpose, o.CreatedAt });

            // [D-09] The cleanup service deletes expired codes by
            // ExpirationTime.
            builder.HasIndex(o => o.ExpirationTime);
        }
    }
}
