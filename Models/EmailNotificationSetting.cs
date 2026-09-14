// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Models
{
    /// <summary>
    /// Switches the individual kinds of mail notification on and off.
    /// <para>
    /// A row per kind of mail rather than a column per kind. The reason: adding
    /// a new mail later needs no migration — the row is created on first use,
    /// switched on by default.
    /// </para>
    /// <para>
    /// The OTP code DELIBERATELY has no row here. Without it nobody can register
    /// or sign in, so switching it off would lock the site.
    /// </para>
    /// </summary>
    public class EmailNotificationSetting
    {
        public int Id { get; set; }

        /// <summary>
        /// Matches a name from the <c>EmailTemplate</c> enum (for example
        /// "PaymentConfirmed"). Unique — see [E-08].
        /// </summary>
        public string TemplateKey { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;

        /// <summary>Who changed it last and when. It is also the tiebreaker
        /// when duplicate rows exist for one key.</summary>
        public DateTime? LastChangedAt { get; set; }
        public string? LastChangedBy { get; set; }
    }
}
