// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Models
{
    // ════════════════════════════════════════════════════════════════
    // CryptoOrder — the history of every crypto order a participant has made.
    //
    // Why the table exists:
    //   1. PaymentMethod could hold only ONE order id, so switching currency
    //      lost the old order from the database while it stayed active at Go28
    //      and took up one of the allowed active orders (maxActiveOrders: 2).
    //   2. The number of active orders for a currency can now be checked
    //      locally BEFORE calling Go28.
    //   3. A page reload restores the active order from the database instead of
    //      asking the gateway again.
    //   4. A complete history for the audit.
    // ════════════════════════════════════════════════════════════════
    public class CryptoOrder
    {
        public int    Id           { get; set; }

        // [D-06] Nullable, because the foreign key is SetNull: deleting a
        // participant no longer takes their orders with them. It used to
        // cascade — the wallet address, the amount, the Go28 order id and the
        // timestamps vanished at once, and all that was left in AuditLogs was
        // the free text of the "User Deleted" row. Reason 4 above ("a complete
        // history for the audit") was therefore not true, and in exactly the
        // argument it matters for — "I paid and now I am not in the system" —
        // these rows are the only evidence on our side. AuditLogs keeps UserId
        // the same way: a string with no foreign key, outliving the user.
        public string? UserId      { get; set; }

        // The gateway's own order id, from the response to POST
        // /gateway/orders. This is what check-status and the webhook use.
        public int    Go28OrderId  { get; set; }

        // The external id sent to Go28: "BCE2026-XXXXX-USDC-1715255741".
        // The first two dash-separated parts are the reference number, which is
        // how the webhook finds the participant.
        public string ExternalId   { get; set; } = string.Empty;

        public string Currency     { get; set; } = string.Empty;
        public string Network      { get; set; } = string.Empty;

        // AmountEUR is a number: the confirmation is checked against it (see
        // [P-06]). The other three are in the crypto currency, arrive from Go28
        // as text, and are only ever displayed — hence strings.
        public decimal AmountEUR   { get; set; }
        public string CryptoAmount { get; set; } = string.Empty;  // gross
        public string NetAmount    { get; set; } = string.Empty;
        public string FeeAmount    { get; set; } = string.Empty;

        // The wallet address and QR code, both produced by Go28.
        public string WalletAddress { get; set; } = string.Empty;
        public string? QrCode       { get; set; }  // base64 SVG, used as an <img src>

        // InProcess | Confirmed | Expired | Cancelled — the gateway's own
        // values, stored verbatim.
        public string Status        { get; set; } = "InProcess";

        // All times are UTC.
        public DateTime  CreatedAt   { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt   { get; set; }
        public DateTime? CompletedAt { get; set; }

        public ApplicationUser? User { get; set; }
    }
}