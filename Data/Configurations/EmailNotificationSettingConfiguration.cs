// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class EmailNotificationSettingConfiguration
        : IEntityTypeConfiguration<EmailNotificationSetting>
    {
        public void Configure(EntityTypeBuilder<EmailNotificationSetting> builder)
        {
            // [E-08] One row per kind of mail used to be a convention in the
            // code rather than a rule in the schema. Missing rows are created on
            // the read path, so two processes could each insert a row for one
            // key; from then on the read threw, the admin panel returned 500 on
            // every open, and notifications that had been switched off went out
            // again.
            builder.HasIndex(s => s.TemplateKey).IsUnique();
        }
    }
}
