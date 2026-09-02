using ConferenceApp.Data;
using ConferenceApp.Models;
using ConferenceApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;

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

        public StripeController(
            StripeService stripe,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            ILogger<StripeController> logger,
            ConferenceApp.Services.Email.IMailComposer mail,
            IConfiguration config,
            ConferenceApp.Services.IPaymentGateSettings paymentGates)
        {
            _stripe      = stripe;
            _userManager = userManager;
            _context     = context;
            _logger      = logger;
            _mail        = mail;
            _config      = config;
            _paymentGates = paymentGates;
        }

        // ════════════════════════════════════════════════════════════════
        // POST /api/stripe/create-intent
        // Извиква се от JS при натискане на Pay бутона.
        // Сумата се взима САМО от базата — никога от клиента.
        // Връща { clientSecret, intentId, amount, currency }
        // ════════════════════════════════════════════════════════════════
        [Authorize]
        [HttpPost("create-intent")]
        public async Task<IActionResult> CreateIntent([FromBody] CreateIntentRequest req)
        {
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            var user     = await _userManager.GetUserAsync(User);

            if (user == null) return Unauthorized();

            // Payment Control (Admin панел) — общ ключ или картата конкретно спрени.
            if (!await _paymentGates.IsEnabledAsync("all") || !await _paymentGates.IsEnabledAsync("method.card"))
                return BadRequest(new { error = "payments_disabled" });

            // Идемпотентност — вече платил
            if (user.PaymentStatus == "Confirmed")
                return BadRequest(new { error = "already_confirmed" });

            // Валидираме валутата
            var currency = req.Currency?.ToLower() ?? "eur";
            if (currency is not ("eur" or "usd"))
                return BadRequest(new { error = "invalid_currency" });

            // Взимаме цената от базата (Id=2 = Early Bird Ticket)
            var tier = await _context.TicketTiers.FindAsync(2);
            if (tier == null) return StatusCode(500, new { error = "tier_not_found" });

            var rawPrice  = tier.PromoPriceEn ?? tier.RegularPriceEn;
            var digits    = new string(rawPrice.Where(c => char.IsDigit(c) || c == '.').ToArray());
            var amountEUR = decimal.TryParse(digits,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var p) ? p : 120.00m;

            // USD conversion (placeholder — в продукция от forex API)
            var finalAmount = currency == "usd"
                ? Math.Round(amountEUR * 1.11m, 2)
                : amountEUR;

            try
            {
                var intent = await _stripe.CreatePaymentIntentAsync(
                    finalAmount,
                    currency,
                    user.ReferenceNumber,
                    user.Email ?? string.Empty,
                    $"{user.FirstName} {user.LastName}".Trim());

                // Аудит — intent създаден
                await WriteAuditAsync(
                    userId:    user.Id,
                    email:     user.Email ?? string.Empty,
                    action:    "Stripe PaymentIntent Created",
                    ip:        clientIp,
                    details:   $"IntentId={intent.Id} | Amount={finalAmount} {currency.ToUpper()} | Ref={user.ReferenceNumber}");

                _logger.LogInformation(
                    "Stripe PaymentIntent created. IntentId={Id} Amount={Amt}{Cur} UserId={Uid} Ref={Ref}",
                    intent.Id, finalAmount, currency.ToUpper(), user.Id, user.ReferenceNumber);

                return Ok(new
                {
                    clientSecret = intent.ClientSecret,
                    intentId     = intent.Id,
                    amount       = finalAmount,
                    currency     = currency.ToUpper()
                });
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe CreateIntent failed. UserId={Uid}", user.Id);

                await WriteAuditAsync(
                    userId:    user.Id,
                    email:     user.Email ?? string.Empty,
                    action:    "Stripe PaymentIntent Failed",
                    ip:        clientIp,
                    details:   $"Error={ex.StripeError?.Message ?? ex.Message} | Ref={user.ReferenceNumber}");

                return StatusCode(500, new { error = ex.StripeError?.Message ?? "stripe_error" });
            }
        }

        // ════════════════════════════════════════════════════════════════
        // POST /api/stripe/webhook
        // Stripe праща HTTP POST при всяко събитие.
        // Верифицираме подписа с Stripe-Signature хедър.
        //
        // Обработваме:
        //   checkout.session.completed — основното събитие при Checkout
        //   payment_intent.succeeded   — fallback / директни PaymentIntent flows
        //
        // Трябва да е [AllowAnonymous] — Stripe не е логнат потребител!
        // ════════════════════════════════════════════════════════════════
        [HttpPost("webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> Webhook()
        {
            // Четем raw body — Stripe подписва точно тези байтове
            string payload;
            using (var reader = new System.IO.StreamReader(HttpContext.Request.Body))
                payload = await reader.ReadToEndAsync();

            var signature = Request.Headers["Stripe-Signature"].FirstOrDefault();
            var webhookIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "stripe-webhook";

            // ── Проверка на подписа ──────────────────────────────────
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

            // ── Логваме всяко получено събитие ──────────────────────
            _context.Set<AuditLog>().Add(new AuditLog
            {
                UserId    = null,
                UserEmail = "stripe-webhook",
                Action    = $"Stripe Webhook Received — {stripeEvent.Type}",
                IpAddress = webhookIp,
                Details   = $"EventId={stripeEvent.Id} | Type={stripeEvent.Type}",
                Timestamp = DateTime.UtcNow
            });

            // ════════════════════════════════════════════════════════
            // СЛУЧАЙ 1: checkout.session.completed
            // Това е основното събитие при Stripe Checkout flow.
            // Потребителят се идентифицира по ClientReferenceId = user.Id
            // и по CustomerEmail като fallback.
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

                // Намираме потребителя — първо по ClientReferenceId (user.Id), после по email
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
                    _context.Set<AuditLog>().Add(new AuditLog
                    {
                        UserId    = null,
                        UserEmail = session.CustomerEmail ?? "stripe-webhook",
                        Action    = "Stripe Webhook — User Not Found",
                        IpAddress = webhookIp,
                        Details   = $"SessionId={session.Id} | ClientRefId={session.ClientReferenceId ?? "—"} | Email={session.CustomerEmail ?? "—"}",
                        Timestamp = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();
                    return Ok();
                }

                // Идемпотентност — вече потвърден
                if (user.PaymentStatus == "Confirmed")
                {
                    _logger.LogInformation(
                        "Stripe webhook: user {UserId} already confirmed. Skipping. SessionId={Id}",
                        user.Id, session.Id);
                    await _context.SaveChangesAsync();
                    return Ok();
                }

                // Потвърждаваме плащането
                user.PaymentStatus = "Confirmed";
                user.PaymentMethod = "Card";
                user.PaidAt        = DateTime.UtcNow;

                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    _logger.LogError(
                        "Stripe webhook: failed to update user {UserId}. SessionId={Id} Errors={Errors}",
                        user.Id, session.Id, errors);
                    _context.Set<AuditLog>().Add(new AuditLog
                    {
                        UserId    = user.Id,
                        UserEmail = user.Email ?? string.Empty,
                        Action    = "Stripe Webhook — Update Failed",
                        IpAddress = webhookIp,
                        Details   = $"SessionId={session.Id} | Ref={user.ReferenceNumber} | Errors={errors}",
                        Timestamp = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();
                    return StatusCode(500);
                }

                // Audit log — успешно потвърждение
                _context.Set<AuditLog>().Add(new AuditLog
                {
                    UserId    = user.Id,
                    UserEmail = user.Email ?? string.Empty,
                    Action    = "Payment Confirmed — Stripe Checkout",
                    IpAddress = webhookIp,
                    Details   = $"SessionId={session.Id} | " +
                                $"Amount={session.AmountTotal.GetValueOrDefault() / 100.0m:F2} {session.Currency?.ToUpper() ?? "EUR"} | " +
                                $"Ref={user.ReferenceNumber} | " +
                                $"CustomerEmail={session.CustomerEmail ?? "—"} | " +
                                $"EventId={stripeEvent.Id}",
                    Timestamp = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Payment confirmed via Stripe Checkout. UserId={UserId} Email={Email} Ref={Ref} SessionId={Id}",
                    user.Id, user.Email, user.ReferenceNumber, session.Id);

                // Имейлът е ТУК, след идемпотентния guard по-горе и след успешния
                // UpdateAsync. Ако беше преди guard-а, повторно доставен webhook
                // (Stripe праща и retry-та) щеше да прати второ писмо за същото
                // плащане. Return URL-ът в Payment.cshtml.cs има собствен guard
                // по същата причина.
                await _mail.SendPaymentConfirmedAsync(
                    toEmail:   user.Email ?? string.Empty,
                    firstName: user.FirstName ?? string.Empty,
                    amount:    $"{session.AmountTotal.GetValueOrDefault() / 100.0m:F2} {session.Currency?.ToUpper() ?? "EUR"}",
                    method:    ConferenceApp.Services.Email.MailContext.PaymentMethodName("Card"),
                    reference: user.ReferenceNumber ?? "—",
                    culture:   ConferenceApp.Services.Email.MailContext.CultureFor(user),
                    baseUrl:   ConferenceApp.Services.Email.MailContext.BaseUrl(_config));

                return Ok();
            }

            // ════════════════════════════════════════════════════════
            // СЛУЧАЙ 2: payment_intent.succeeded
            // Fallback за директни PaymentIntent flows (напр. от StripeService).
            // Потребителят се идентифицира по ReferenceNumber в metadata.
            // ════════════════════════════════════════════════════════
            if (stripeEvent.Type == "payment_intent.succeeded")
            {
                var intent = stripeEvent.Data.Object as PaymentIntent;
                if (intent == null)
                {
                    _logger.LogWarning("Stripe webhook: could not cast event object to PaymentIntent.");
                    await _context.SaveChangesAsync();
                    return Ok();
                }

                intent.Metadata.TryGetValue("reference", out var reference);

                _logger.LogInformation(
                    "Stripe payment_intent.succeeded. IntentId={Id} Amount={Amt} Ref={Ref}",
                    intent.Id, intent.Amount / 100.0m, reference ?? "—");

                if (string.IsNullOrEmpty(reference))
                {
                    _logger.LogWarning("Stripe webhook: PaymentIntent {Id} has no 'reference' in metadata.", intent.Id);
                    _context.Set<AuditLog>().Add(new AuditLog
                    {
                        UserId    = null,
                        UserEmail = "stripe-webhook",
                        Action    = "Stripe Webhook — Missing Reference",
                        IpAddress = webhookIp,
                        Details   = $"IntentId={intent.Id} | Amount={intent.Amount / 100.0m:F2} {intent.Currency.ToUpper()}",
                        Timestamp = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();
                    return Ok();
                }

                var intentUser = await _userManager.Users
                    .FirstOrDefaultAsync(u => u.ReferenceNumber == reference);

                if (intentUser == null)
                {
                    _logger.LogWarning("Stripe webhook: no user found for ReferenceNumber={Ref}.", reference);
                    _context.Set<AuditLog>().Add(new AuditLog
                    {
                        UserId    = null,
                        UserEmail = "stripe-webhook",
                        Action    = "Stripe Webhook — User Not Found",
                        IpAddress = webhookIp,
                        Details   = $"IntentId={intent.Id} | Ref={reference}",
                        Timestamp = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();
                    return Ok();
                }

                if (intentUser.PaymentStatus == "Confirmed")
                {
                    _logger.LogInformation(
                        "Stripe webhook: user {UserId} already confirmed. Skipping. IntentId={Id}",
                        intentUser.Id, intent.Id);
                    await _context.SaveChangesAsync();
                    return Ok();
                }

                intentUser.PaymentStatus = "Confirmed";
                intentUser.PaymentMethod = "Card";
                intentUser.PaidAt        = DateTime.UtcNow;

                var intentResult = await _userManager.UpdateAsync(intentUser);
                if (!intentResult.Succeeded)
                {
                    var errors = string.Join(", ", intentResult.Errors.Select(e => e.Description));
                    _logger.LogError("Stripe webhook: failed to update user {UserId}. Errors={Errors}", intentUser.Id, errors);
                    _context.Set<AuditLog>().Add(new AuditLog
                    {
                        UserId    = intentUser.Id,
                        UserEmail = intentUser.Email ?? string.Empty,
                        Action    = "Stripe Webhook — Update Failed",
                        IpAddress = webhookIp,
                        Details   = $"IntentId={intent.Id} | Ref={reference} | Errors={errors}",
                        Timestamp = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();
                    return StatusCode(500);
                }

                _context.Set<AuditLog>().Add(new AuditLog
                {
                    UserId    = intentUser.Id,
                    UserEmail = intentUser.Email ?? string.Empty,
                    Action    = "Payment Confirmed — Stripe PaymentIntent",
                    IpAddress = webhookIp,
                    Details   = $"IntentId={intent.Id} | " +
                                $"Amount={intent.Amount / 100.0m:F2} {intent.Currency.ToUpper()} | " +
                                $"Ref={reference} | " +
                                $"CustomerEmail={intent.ReceiptEmail ?? "—"} | " +
                                $"EventId={stripeEvent.Id}",
                    Timestamp = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Payment confirmed via Stripe PaymentIntent. UserId={UserId} Email={Email} Ref={Ref} IntentId={Id}",
                    intentUser.Id, intentUser.Email, reference, intent.Id);

                // Същото положение като при checkout.session.completed по-горе:
                // след guard-а за вече потвърдено и след успешния Update.
                await _mail.SendPaymentConfirmedAsync(
                    toEmail:   intentUser.Email ?? string.Empty,
                    firstName: intentUser.FirstName ?? string.Empty,
                    amount:    $"{intent.Amount / 100.0m:F2} {intent.Currency.ToUpper()}",
                    method:    ConferenceApp.Services.Email.MailContext.PaymentMethodName("Card"),
                    reference: intentUser.ReferenceNumber ?? reference ?? "—",
                    culture:   ConferenceApp.Services.Email.MailContext.CultureFor(intentUser),
                    baseUrl:   ConferenceApp.Services.Email.MailContext.BaseUrl(_config));

                return Ok();
            }

            // ════════════════════════════════════════════════════════
            // СЛУЧАЙ 3: payment_intent.payment_failed
            // Записваме детайлна информация за провалено плащане.
            // PaymentStatus НЕ се променя — потребителят може да опита отново.
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

                // Взимаме причината за грешката от Stripe
                var failureMessage = intent.LastPaymentError?.Message ?? "Unknown error";
                var failureCode    = intent.LastPaymentError?.Code    ?? "unknown";
                var declineCode    = intent.LastPaymentError?.DeclineCode ?? "—";

                _logger.LogWarning(
                    "Stripe payment failed. IntentId={Id} Amount={Amt} Code={Code} DeclineCode={DC} Message={Msg}",
                    intent.Id, intent.Amount / 100.0m, failureCode, declineCode, failureMessage);

                // Опитваме да намерим потребителя по ReferenceNumber в metadata
                intent.Metadata.TryGetValue("reference", out var reference);
                ApplicationUser? failedUser = null;

                if (!string.IsNullOrEmpty(reference))
                    failedUser = await _userManager.Users
                        .FirstOrDefaultAsync(u => u.ReferenceNumber == reference);

                // Ако не намерим по reference → опитваме по email
                if (failedUser == null && !string.IsNullOrEmpty(intent.ReceiptEmail))
                    failedUser = await _userManager.FindByEmailAsync(intent.ReceiptEmail);

                _context.Set<AuditLog>().Add(new AuditLog
                {
                    UserId    = failedUser?.Id,
                    UserEmail = failedUser?.Email ?? intent.ReceiptEmail ?? "stripe-webhook",
                    Action    = "Payment Failed — Stripe",
                    IpAddress = webhookIp,
                    Details   = $"IntentId={intent.Id} | " +
                                $"Amount={intent.Amount / 100.0m:F2} {intent.Currency.ToUpper()} | " +
                                $"Code={failureCode} | " +
                                $"DeclineCode={declineCode} | " +
                                $"Message={failureMessage} | " +
                                $"Ref={reference ?? "—"} | " +
                                $"EventId={stripeEvent.Id}",
                    Timestamp = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
                return Ok();
            }

            // ── Всички останали събития — логнати вече по-горе, просто Ok() ──
            await _context.SaveChangesAsync();
            return Ok();
        }

        // ════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════

        private async Task WriteAuditAsync(string? userId, string email, string action, string ip, string details)
        {
            _context.Set<AuditLog>().Add(new AuditLog
            {
                UserId    = userId,
                UserEmail = email,
                Action    = action,
                IpAddress = ip,
                Details   = details,
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        // ════════════════════════════════════════════════════════════════
        // Request model
        // ════════════════════════════════════════════════════════════════
        public class CreateIntentRequest
        {
            public string? Currency { get; set; }
        }
    }
}