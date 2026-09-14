// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Security.Cryptography;
using System.Text;

namespace ConferenceApp.Tests.Fixtures;

/// <summary>
/// Signs a webhook body the way Stripe signs it, so that it passes
/// <c>EventUtility.ConstructEvent</c>. The key is the test one from
/// <see cref="AppFixture.StripeWebhookSecret"/>; the live key is never used.
/// </summary>
public static class StripeWebhook
{
    public static string Sign(string payload, string secret, DateTimeOffset? at = null)
    {
        var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
        var signed = $"{timestamp}.{payload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signed));

        return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    /// <summary>The body of a <c>checkout.session.completed</c> event with the given values.</summary>
    public static string CheckoutSessionCompleted(
        string sessionId,
        string? clientReferenceId,
        long amountTotalCents,
        string? customerEmail = null,
        string currency = "eur",
        string eventId = "evt_test_confapp")
    {
        string Quote(string? v) => v == null ? "null" : $"\"{v}\"";

        // ConstructEvent compares the event's api_version with the version the
        // SDK was built against and throws when they differ. A real webhook
        // carries the version of the account; here the version the SDK expects
        // is used instead.
        var apiVersion = global::Stripe.StripeConfiguration.ApiVersion;

        return $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "{{apiVersion}}",
          "created": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
          "livemode": false,
          "type": "checkout.session.completed",
          "data": {
            "object": {
              "id": "{{sessionId}}",
              "object": "checkout.session",
              "amount_total": {{amountTotalCents}},
              "amount_subtotal": {{amountTotalCents}},
              "currency": "{{currency}}",
              "client_reference_id": {{Quote(clientReferenceId)}},
              "customer_email": {{Quote(customerEmail)}},
              "mode": "payment",
              "payment_status": "paid",
              "status": "complete",
              "livemode": false
            }
          }
        }
        """;
    }
}
