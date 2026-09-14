// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Models
{
    /// <summary>
    /// Switches payments on and off from the admin panel — Payment Control.
    /// <para>
    /// A row per key rather than a column per key, mirroring
    /// <see cref="EmailNotificationSetting"/>. The eight keys
    /// ("all", "method.card", "method.crypto", "method.iban",
    /// "currency.BTC", "currency.ETH", "currency.EURC", "currency.USDC")
    /// are created on first use, switched on by default, so a database filled
    /// before this table existed starts with everything enabled.
    /// </para>
    /// </summary>
    public class PaymentGateSetting
    {
        public int Id { get; set; }

        public string GateKey { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;

        /// <summary>Who changed it last and when. It is also the tiebreaker
        /// when duplicate rows exist for one key.</summary>
        public DateTime? LastChangedAt { get; set; }
        public string? LastChangedBy { get; set; }
    }
}
