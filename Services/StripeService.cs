using Stripe;

namespace ConferenceApp.Services
{
    // ════════════════════════════════════════════════════════════════
    // StripeService — обвива Stripe .NET SDK
    // Регистрира се като Scoped в Program.cs
    // ════════════════════════════════════════════════════════════════
    public class StripeService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<StripeService> _logger;

        public string PublishableKey { get; }

        public StripeService(IConfiguration config, ILogger<StripeService> logger)
        {
            _config = config;
            _logger = logger;

            var secretKey = config["Stripe:SecretKey"]
                ?? throw new InvalidOperationException("Stripe:SecretKey is not configured.");

            PublishableKey = config["Stripe:PublishableKey"]
                ?? throw new InvalidOperationException("Stripe:PublishableKey is not configured.");

            // Задаваме глобалния API ключ за Stripe SDK
            StripeConfiguration.ApiKey = secretKey;
        }

        // ── Създава PaymentIntent ────────────────────────────────────────────
        // amountEUR  : сумата в EUR (напр. 60.00)
        // reference  : ReferenceNumber на потребителя (BCE2026-XXXXX) — пази се в metadata
        // customerName: за receipt_email и metadata
        public async Task<PaymentIntent> CreatePaymentIntentAsync(
            decimal amountEUR,
            string currency,        // "eur" | "usd"
            string reference,
            string customerEmail,
            string customerName)
        {
            // Stripe работи с цели числа в най-малката единица (стотинки)
            var amountInCents = (long)Math.Round(amountEUR * 100);

            var options = new PaymentIntentCreateOptions
            {
                Amount             = amountInCents,
                Currency           = currency.ToLower(),
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true
                },
                ReceiptEmail       = customerEmail,
                Description        = $"Blockchain Education 2026 — {reference}",
                Metadata           = new Dictionary<string, string>
                {
                    ["reference"]   = reference,
                    ["customerName"] = customerName,
                    ["conference"]  = "BlockchainEducation2026"
                }
            };

            var service = new PaymentIntentService();
            var intent  = await service.CreateAsync(options);

            _logger.LogInformation(
                "Stripe PaymentIntent created. Id={Id} Amount={Amount}{Currency} Reference={Ref}",
                intent.Id, amountEUR, currency.ToUpper(), reference);

            return intent;
        }

        // ── Взима PaymentIntent по Id (за проверка на статус) ───────────────
        public async Task<PaymentIntent?> GetPaymentIntentAsync(string intentId)
        {
            try
            {
                var service = new PaymentIntentService();
                return await service.GetAsync(intentId);
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe GetPaymentIntent failed. Id={Id}", intentId);
                return null;
            }
        }

        // ── Конструира Event от webhook payload ──────────────────────────────
        public Event ConstructWebhookEvent(string payload, string signature)
        {
            var webhookSecret = _config["Stripe:WebhookSecret"]
                ?? throw new InvalidOperationException("Stripe:WebhookSecret is not configured.");

            return EventUtility.ConstructEvent(payload, signature, webhookSecret);
        }
    }
}
