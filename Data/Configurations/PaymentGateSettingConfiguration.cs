// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ConferenceApp.Data.Configurations
{
    public class PaymentGateSettingConfiguration
        : IEntityTypeConfiguration<PaymentGateSetting>
    {
        public void Configure(EntityTypeBuilder<PaymentGateSetting> builder)
        {
            // [D-04] The same as for EmailNotificationSetting, but the failure
            // is more expensive here: the read is swallowed and read as
            // "payments are on", so a single duplicate row permanently and
            // silently disabled the "switch payments off" control.
            builder.HasIndex(s => s.GateKey).IsUnique();
        }
    }
}
