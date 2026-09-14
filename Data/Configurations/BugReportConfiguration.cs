// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class BugReportConfiguration : IEntityTypeConfiguration<BugReport>
    {
        public void Configure(EntityTypeBuilder<BugReport> builder)
        {
            // For /Admin/BugReports: filtering by status (the Open, InProgress,
            // Resolved and WontFix groups) and ordering by date.
            builder.HasIndex(b => b.Status);
            builder.HasIndex(b => b.CreatedAt);
        }
    }
}
