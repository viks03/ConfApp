namespace ConferenceApp.Models
{
    /// <summary>
    /// Включване и изключване на плащанията от админ панела — Payment Control.
    /// <para>
    /// Ред на ключ, а не колона на ключ — огледално на
    /// <see cref="EmailNotificationSetting"/>. Осемте ключа
    /// ("all", "method.card", "method.crypto", "method.iban",
    /// "currency.BTC", "currency.ETH", "currency.EURC", "currency.USDC")
    /// се създават автоматично при първото обръщение, включени по подразбиране,
    /// така че първото зареждане преди тази миграция показва всичко включено.
    /// </para>
    /// </summary>
    public class PaymentGateSetting
    {
        public int Id { get; set; }

        public string GateKey { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;

        /// <summary>Кой и кога го е променил последно — за проследимост.</summary>
        public DateTime? LastChangedAt { get; set; }
        public string? LastChangedBy { get; set; }
    }
}
