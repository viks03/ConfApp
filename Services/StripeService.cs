// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using Stripe;

namespace ConferenceApp.Services
{
    // ════════════════════════════════════════════════════════════════
    // StripeService — a thin wrapper over the Stripe .NET SDK. The keys are
    // read once here, so that a missing one is reported at startup rather than
    // at the first payment.
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

            // IsNullOrWhiteSpace rather than ?? — appsettings.json holds "" for
            // these keys and IConfiguration returns the empty string, not null.
            // With ?? the check would pass, the Stripe SDK would start with an
            // empty key, and the failure would surface at the first payment.
            var secretKey = config["Stripe:SecretKey"];
            if (string.IsNullOrWhiteSpace(secretKey))
                throw new InvalidOperationException("Stripe:SecretKey is not configured.");

            PublishableKey = config["Stripe:PublishableKey"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(PublishableKey))
                throw new InvalidOperationException("Stripe:PublishableKey is not configured.");

            // The SDK keeps the secret key in a static: setting it here covers
            // every call made anywhere in the process.
            StripeConfiguration.ApiKey = secretKey;
        }

        // ── Builds an Event from a webhook payload ───────────────────────────
        // Throws unless the signature matches the raw body, which is why the
        // Stripe webhook path enables request buffering in Program.cs.
        public Event ConstructWebhookEvent(string payload, string signature)
        {
            var webhookSecret = _config["Stripe:WebhookSecret"];
            if (string.IsNullOrWhiteSpace(webhookSecret))
                throw new InvalidOperationException("Stripe:WebhookSecret is not configured.");

            return EventUtility.ConstructEvent(payload, signature, webhookSecret);
        }
    }
}
