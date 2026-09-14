// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Data;
using ConferenceApp.Models;
using ConferenceApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using ConferenceApp.Services.Payments;

namespace ConferenceApp.Controllers
{
    [ApiController]
    [Route("api/stripe")]
    public class StripeController : ControllerBase
    {
        private readonly StripeService                _stripe;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext         _context;
        private readonly ILogger<StripeController>    _logger;
        private readonly ConferenceApp.Services.Email.IMailComposer _mail;
        private readonly IConfiguration               _config;
        private readonly ConferenceApp.Services.IPaymentGateSettings _paymentGates;
        private readonly ConferenceApp.Services.AuditService        _audit;

        public StripeController(
            StripeService stripe,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            ILogger<StripeController> logger,
            ConferenceApp.Services.Email.IMailComposer mail,
            IConfiguration config,
            ConferenceApp.Services.IPaymentGateSettings paymentGates,
            ConferenceApp.Services.AuditService audit)
        {
            _stripe      = stripe;
            _userManager = userManager;
            _context     = context;
            _logger      = logger;
            _mail        = mail;
            _config      = config;
            _paymentGates = paymentGates;
            _audit       = audit;
        }

        // ════════════════════════════════════════════════════════════════
        // POST /api/stripe/webhook
        //
        // The whole card-payment flow in one place: Stripe POSTs here on every
        // event, the signature proves the request really came from Stripe, and
        // the participant behind it is confirmed. The browser returning to
        // /Payment is the second, independent path to the same confirmation —
        // whichever arrives first wins, and both are written to be safe to run
        // twice.
        //
        // Handled events:
        //   checkout.session.completed    — the main Checkout event
        //   payment_intent.payment_failed — records a failed attempt
        //
        // The payment_intent.succeeded branch went with the dead
        // /api/stripe/create-intent ([P-10]). The live path is a Checkout
        // Session, where the intent carries no 'reference' in its metadata — so
        // the branch confirmed nobody anyway. Such events fall through to the
        // Ok() at the end, which stops Stripe from retrying them.
        //
        // [AllowAnonymous] is required: Stripe is not a signed-in user.
        //
        // [P-01] An anonymous endpoint, with the same rate and size ceilings as
        // the crypto webhook. The signature can only be checked after the body
        // has been read, so the limit has to come before that.
        [HttpPost("webhook")]
        [AllowAnonymous]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("webhook")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> Webhook()
        {
            // The raw body: Stripe signs exactly these bytes, so nothing may be
            // re-serialized before the check.
            string payload;
            using (var reader = new System.IO.StreamReader(HttpContext.Request.Body))
                payload = await reader.ReadToEndAsync();

            var signature = Request.Headers["Stripe-Signature"].FirstOrDefault();
            var webhookIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "stripe-webhook";

            // ── Signature check ──────────────────────────────────────
            if (string.IsNullOrEmpty(signature))
            {
                _logger.LogWarning("Stripe webhook: missing Stripe-Signature header. IP={IP}", webhookIp);
                return BadRequest("Missing Stripe-Signature header.");
            }

            Stripe.Event stripeEvent;
            try
            {
                stripeEvent = _stripe.ConstructWebhookEvent(payload, signature);
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "Stripe webhook: invalid signature. IP={IP}", webhookIp);
                return BadRequest("Invalid signature.");
            }

            _logger.LogInformation(
                "Stripe webhook received. Type={Type} EventId={Id}",
                stripeEvent.Type, stripeEvent.Id);

            // Every received event is recorded, handled or not: without this the
            // audit shows only the events we happened to act on.
            _audit.Add(null, "stripe-webhook",
                $"Stripe Webhook Received — {stripeEvent.Type}",
                $"EventId={stripeEvent.Id} | Type={stripeEvent.Type}", webhookIp);

            // ════════════════════════════════════════════════════════
            // CASE 1: checkout.session.completed
            // The main event in the Checkout flow. The participant is found by
            // ClientReferenceId (which carries user.Id), falling back to
            // CustomerEmail — a person can pay from an address other than the
            // one they registered with, but not with another user's id.
            // ════════════════════════════════════════════════════════
            if (stripeEvent.Type == "checkout.session.completed")
            {
                var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
                if (session == null)
                {
                    _logger.LogWarning("Stripe webhook: could not cast event object to Session. EventId={Id}", stripeEvent.Id);
                    await _context.SaveChangesAsync();
                    return Ok();
                }

                _logger.LogInformation(
                    "Stripe checkout.session.completed. SessionId={Id} ClientRefId={Ref} Email={Email} Amount={Amt}",
                    session.Id, session.ClientReferenceId ?? "—", session.CustomerEmail ?? "—",
                    session.AmountTotal.HasValue ? session.AmountTotal.Value / 100.0m : 0);

                // By id first, by address second: the id is ours, the address is
                // whatever the payer typed into Stripe.
                ApplicationUser? user = null;

                if (!string.IsNullOrEmpty(session.ClientReferenceId))
                    user = await _userManager.FindByIdAsync(session.ClientReferenceId);

                if (user == null && !string.IsNullOrEmpty(session.CustomerEmail))
                    user = await _userManager.FindByEmailAsync(session.CustomerEmail);

                if (user == null)
                {
                    _logger.LogWarning(
                        "Stripe webhook: no user found. ClientRefId={Ref} Email={Email} SessionId={Id}",
                        session.ClientReferenceId ?? "—", session.CustomerEmail ?? "—", session.Id);
                    _audit.Add(null, session.CustomerEmail ?? "stripe-webhook",
                        "Stripe Webhook — User Not Found",
                        $"SessionId={session.Id} | ClientRefId={session.ClientReferenceId ?? "—"} | Email={session.CustomerEmail ?? "—"}",
                        webhookIp);
                    await _context.SaveChangesAsync();
                    return Ok();
                }

                // Idempotency: Stripe retries a webhook until it gets a 2xx, and
                // the browser return path confirms the same payment. Confirming
                // twice would send a second mail for one payment.
                if (user.PaymentStatus == "Confirmed")
                {
                    _logger.LogInformation(
                        "Stripe webhook: user {UserId} already confirmed. Skipping. SessionId={Id}",
                        user.Id, session.Id);
                    await _context.SaveChangesAsync();
                    return Ok();
                }

                var paidEUR = session.AmountTotal.GetValueOrDefault() / 100.0m;

                // [P-06] The amount paid is checked against the prices that exist
                // in TicketTiers at all. Stripe's signature already guarantees
                // where the event came from, so a mismatch is recorded but does
                // not stop the confirmation — otherwise editing a price in the
                // panel while someone was paying would leave a participant who
                // had paid stuck in Pending.
                var payableEUR = (await _context.TicketTiers.ToListAsync())
                    .Select(TicketPricing.PriceEUR)
                    .Where(a => a.HasValue)
                    .Select(a => a!.Value)
                    .ToList();

                if (payableEUR.Count > 0 && !payableEUR.Contains(paidEUR))
                {
                    _logger.LogWarning(
                        "Stripe webhook: amount {Paid} matches no ticket tier. SessionId={Id} UserId={Uid}",
                        paidEUR, session.Id, user.Id);
                    _audit.Add(user.Id, user.Email ?? string.Empty,
                        "Payment Amount Mismatch — Stripe Checkout",
                        $"SessionId={session.Id} | Paid={TicketPricing.Format(paidEUR)} | " +
                        $"Known tier prices: {string.Join(", ", payableEUR.Select(a => TicketPricing.Format(a)))} | " +
                        $"Ref={user.ReferenceNumber} | Confirmed anyway", webhookIp);
                }

                user.PaymentStatus = "Confirmed";
                // [T-01] The same literal the browser return path writes
                // (Payment.cshtml.cs). One payment is confirmed by two
                // independent routes and whichever arrives first decides; with
                // two different spellings the column depended on which that
                // was.
                user.PaymentMethod = "Stripe:Card";
                user.PaidAt        = DateTime.UtcNow;
                user.PaidAmountEUR = paidEUR;

                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    _logger.LogError(
                        "Stripe webhook: failed to update user {UserId}. SessionId={Id} Errors={Errors}",
                        user.Id, session.Id, errors);
                    _audit.Add(user.Id, user.Email ?? string.Empty,
                        "Stripe Webhook — Update Failed",
                        $"SessionId={session.Id} | Ref={user.ReferenceNumber} | Errors={errors}", webhookIp);
                    await _context.SaveChangesAsync();
                    return StatusCode(500);
                }

                // The audit row for a successful confirmation.
                _audit.Add(user.Id, user.Email ?? string.Empty,
                    "Payment Confirmed — Stripe Checkout",
                    $"SessionId={session.Id} | " +
                    $"Amount={session.AmountTotal.GetValueOrDefault() / 100.0m:F2} {session.Currency?.ToUpper() ?? "EUR"} | " +
                    $"Ref={user.ReferenceNumber} | " +
                    $"CustomerEmail={session.CustomerEmail ?? "—"} | " +
                    $"EventId={stripeEvent.Id}", webhookIp);

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Payment confirmed via Stripe Checkout. UserId={UserId} Email={Email} Ref={Ref} SessionId={Id}",
                    user.Id, user.Email, user.ReferenceNumber, session.Id);

                // The mail is sent HERE, after the idempotency guard above and
                // after a successful UpdateAsync. Before the guard, a redelivered
                // webhook — Stripe does retry — would send a second mail for the
                // same payment. The return URL in Payment.cshtml.cs has a guard
                // of its own for the same reason.
                await _mail.SendPaymentConfirmedAsync(
                    toEmail:   user.Email ?? string.Empty,
                    firstName: user.FirstName ?? string.Empty,
                    // [T-28] As in Pages/Payment.cshtml.cs: the format of the
                    // amount must not depend on the culture of the request — the
                    // webhook and the browser return are two routes to one and
                    // the same payment.
                    amount:    ConferenceApp.Services.Payments.TicketPricing.Format(
                                   session.AmountTotal.GetValueOrDefault() / 100.0m),
                    method:    ConferenceApp.Services.Email.MailContext.PaymentMethodName("Card"),
                    reference: user.ReferenceNumber ?? "—",
                    culture:   ConferenceApp.Services.Email.MailContext.CultureFor(user),
                    baseUrl:   ConferenceApp.Services.Email.MailContext.BaseUrl(_config));

                return Ok();
            }

            // ════════════════════════════════════════════════════════
            // CASE 2: payment_intent.payment_failed
            // Records a failed attempt in detail. PaymentStatus is NOT changed —
            // the person can try again, and a declined card is not a state the
            // participant should be left in.
            // ════════════════════════════════════════════════════════
            if (stripeEvent.Type == "payment_intent.payment_failed")
            {
                var intent = stripeEvent.Data.Object as PaymentIntent;
                if (intent == null)
                {
                    _logger.LogWarning("Stripe webhook: could not cast payment_failed object to PaymentIntent.");
                    await _context.SaveChangesAsync();
                    return Ok();
                }

                // The decline reason, as Stripe reports it. The decline code is
                // what the bank said; the message is what Stripe made of it.
                var failureMessage = intent.LastPaymentError?.Message ?? "Unknown error";
                var failureCode    = intent.LastPaymentError?.Code    ?? "unknown";
                var declineCode    = intent.LastPaymentError?.DeclineCode ?? "—";

                _logger.LogWarning(
                    "Stripe payment failed. IntentId={Id} Amount={Amt} Code={Code} DeclineCode={DC} Message={Msg}",
                    intent.Id, intent.Amount / 100.0m, failureCode, declineCode, failureMessage);

                // The participant, if they can be identified at all: a failed
                // intent may carry neither our reference nor a receipt address,
                // in which case the row is still written without a user.
                intent.Metadata.TryGetValue("reference", out var reference);
                ApplicationUser? failedUser = null;

                if (!string.IsNullOrEmpty(reference))
                    failedUser = await _userManager.Users
                        .FirstOrDefaultAsync(u => u.ReferenceNumber == reference);

                // Failing that, by the receipt address.
                if (failedUser == null && !string.IsNullOrEmpty(intent.ReceiptEmail))
                    failedUser = await _userManager.FindByEmailAsync(intent.ReceiptEmail);

                _audit.Add(failedUser?.Id,
                    failedUser?.Email ?? intent.ReceiptEmail ?? "stripe-webhook",
                    "Payment Failed — Stripe",
                    $"IntentId={intent.Id} | " +
                    $"Amount={intent.Amount / 100.0m:F2} {intent.Currency.ToUpper()} | " +
                    $"Code={failureCode} | " +
                    $"DeclineCode={declineCode} | " +
                    $"Message={failureMessage} | " +
                    $"Ref={reference ?? "—"} | " +
                    $"EventId={stripeEvent.Id}", webhookIp);

                await _context.SaveChangesAsync();
                return Ok();
            }

            // Every other event: already recorded above. Ok() tells Stripe not
            // to retry it.
            await _context.SaveChangesAsync();
            return Ok();
        }

    }
}