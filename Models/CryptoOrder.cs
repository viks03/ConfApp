namespace ConferenceApp.Models
{
    // ════════════════════════════════════════════════════════════════
    // CryptoOrder — пази история на всички крипто orders за потребител.
    //
    // Защо е нужна:
    //   1. PaymentMethod пазеше само ЕДИН orderId — при смяна на валута
    //      старият order се губеше от базата, но оставаше активен в Go28
    //      и заемаше място от лимита (maxActiveOrders: 2).
    //   2. Сега можем локално да проверим колко активни orders има
    //      потребителят за дадена валута ПРЕДИ да викаме Go28.
    //   3. При рефреш зареждаме активния order от базата — по-бързо,
    //      без излишни заявки към Go28.
    //   4. Пълна история за одит.
    // ════════════════════════════════════════════════════════════════
    public class CryptoOrder
    {
        public int    Id           { get; set; }

        // Връзка към потребителя
        public string UserId       { get; set; } = string.Empty;

        // Go28 order ID (от response-а на POST /gateway/orders)
        public int    Go28OrderId  { get; set; }

        // ExternalId пратен към Go28: "BCE2026-XXXXX-USDC-1715255741"
        public string ExternalId   { get; set; } = string.Empty;

        // Валута и мрежа
        public string Currency     { get; set; } = string.Empty;
        public string Network      { get; set; } = string.Empty;

        // Суми
        public string AmountEUR    { get; set; } = string.Empty;
        public string CryptoAmount { get; set; } = string.Empty;  // Брутна сума
        public string NetAmount    { get; set; } = string.Empty;
        public string FeeAmount    { get; set; } = string.Empty;

        // Wallet адрес и QR код от Go28
        public string WalletAddress { get; set; } = string.Empty;
        public string? QrCode       { get; set; }  // Base64 SVG

        // Статус: InProcess | Confirmed | Expired | Cancelled
        public string Status        { get; set; } = "InProcess";

        // Времена (UTC)
        public DateTime  CreatedAt   { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt   { get; set; }
        public DateTime? CompletedAt { get; set; }

        // Navigation property
        public ApplicationUser? User { get; set; }
    }
}