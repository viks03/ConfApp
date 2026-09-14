// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Services.Payments
{
    // ════════════════════════════════════════════════════════════════
    // PaymentMethodDisplay — how the PaymentMethod column is shown.
    //
    // [T-01] The column holds no canonical value: a card payment is stored as
    // "Stripe:Card", crypto as "Crypto:USDC" (and until the payment lands, as
    // "Crypto:USDC:ETH:5001"), and a manual confirmation from the panel as
    // "Card" / "Crypto" / "IBAN" / "Manual". The admin panel used to pick the
    // badge class by comparing the whole value against five literals, so
    // anything with a prefix fell through to "method-manual" and a card payment
    // was displayed as "Manual".
    //
    // The mail subsystem already knows how to read such a value
    // (Email.MailContext.PaymentMethodName); what is added here is only the
    // family that decides the colour.
    // ════════════════════════════════════════════════════════════════
    public static class PaymentMethodDisplay
    {
        /// <summary>
        /// The family of the method — "card", "crypto", "iban", "subsidised"
        /// or "manual". Used for the CSS class of the badge. Anything
        /// unrecognised falls back to "manual", which is how a hand-confirmed
        /// payment is shown.
        /// </summary>
        public static string Family(string? method)
        {
            if (string.IsNullOrWhiteSpace(method)) return "manual";

            var value = method.Trim();

            if (value.StartsWith("Crypto", StringComparison.OrdinalIgnoreCase)) return "crypto";
            if (value.StartsWith("Stripe", StringComparison.OrdinalIgnoreCase)) return "card";

            return value.ToLowerInvariant() switch
            {
                "card"       => "card",
                "iban"       => "iban",
                "subsidised" => "subsidised",
                _            => "manual"
            };
        }

        /// <summary>The text in the badge — the same wording the participant
        /// sees in their mail.</summary>
        public static string Label(string? method) =>
            ConferenceApp.Services.Email.MailContext.PaymentMethodName(method);
    }
}
