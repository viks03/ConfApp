using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class BugReportConfiguration : IEntityTypeConfiguration<BugReport>
    {
        public void Configure(EntityTypeBuilder<BugReport> builder)
        {
            // Индекси за /Admin/BugReports: филтриране по статус
            // (Open/InProgress/Resolved/WontFix групите) и сортиране по дата.
            builder.HasIndex(b => b.Status);
            builder.HasIndex(b => b.CreatedAt);
        }
    }
}
