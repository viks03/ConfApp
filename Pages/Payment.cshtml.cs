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
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Stripe.Checkout; 
using System.Text.RegularExpressions;
using Stripe;           
using ConferenceApp.Services.Payments;

namespace ConferenceApp.Pages
{
    [Authorize]
    public class PaymentModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly Go28Service _go28;
        private readonly StripeService _stripe;
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        private readonly ConferenceApp.Services.Email.IMailComposer _mail;
        private readonly ConferenceApp.Services.IPaymentGateSettings _paymentGates;
        private readonly ConferenceApp.Services.AuditService _audit;
        private readonly ILogger<PaymentModel> _logger;

        // The payment page: one tier, three ways to pay it.
        //
        // Two rules run through the whole file. The amount owed comes from the
        // participation form, never from the slug in the address — the slug is
        // the visitor's to choose, and a wrong tier is answered with a redirect
        // to the right one rather than with an error. And confirming a payment
        // is written to be safe to run twice: this page and the Stripe webhook
        // are independent routes to the same confirmation, and whichever arrives
        // first sends the mail while the other finds "Confirmed" and says
        // nothing.

        /// <summary>The state of the eight Payment Control keys (admin panel).</summary>
        public Dictionary<string, bool> PaymentGates { get; set; } = new();

        /// <summary>A missing key reads as switched on, mirroring the admin
        /// panel and PaymentGateSettings.</summary>
        private bool Gate(string key) => !PaymentGates.TryGetValue(key, out var g) || g;

        /// <summary>The master key: when it is off, /Payment renders
        /// _PaymentsDisabled instead of the form.</summary>
        public bool PaymentsEnabled => Gate("all");

        /// <summary>
        /// method is "card" | "crypto" | "iban". The hierarchy is not enforced in
        /// the database — it is applied here: a method is available only if both
        /// "all" and the method's own key are on.
        /// </summary>
        public bool IsMethodEnabled(string method) => Gate("all") && Gate($"method.{method}");

        [BindProperty(SupportsGet = true)]
        public string Slug { get; set; } = string.Empty;

        public string ReferenceNumber   { get; set; } = string.Empty;
        public string PaymentStatus     { get; set; } = "Pending";
        public string ParticipationType { get; set; } = string.Empty;
        
        public decimal TotalEUR         { get; set; } = 0m;
        
        public DateTime? IbanTransferSubmittedAt { get; set; }

        public string TierName         { get; set; } = string.Empty;
        public string TierRegularPrice { get; set; } = string.Empty;
        public string TierPromoPrice   { get; set; } = string.Empty;
        public string TierDescription  { get; set; } = string.Empty;
        public List<string> TierPerks  { get; set; } = new();

        public decimal TierPriceEUR    { get; set; } = 0m;
        public bool HasPromoPrice      { get; set; } = false;

        // Aliases the view reads under these names.
        public bool   TierHasPromo         => HasPromoPrice;
        public bool   IbanTransferSubmitted => IbanTransferSubmittedAt.HasValue;
        public bool   PaymentSuccess        { get; set; } = false;
        public string StripePublishableKey  { get; set; } = string.Empty;

        public List<Go28Currency> SupportedCurrencies { get; set; } = new();

        public PaymentModel(
            UserManager<ApplicationUser> userManager,
            Go28Service go28,
            StripeService stripe,
            ApplicationDbContext context,
            IConfiguration config,
            ConferenceApp.Services.Email.IMailComposer mail,
            ConferenceApp.Services.IPaymentGateSettings paymentGates,
            ConferenceApp.Services.AuditService audit,
            ILogger<PaymentModel> logger)
        {
            _userManager = userManager;
            _go28        = go28;
            _stripe      = stripe;
            _context     = context;
            _config      = config;
            _mail        = mail;
            _paymentGates = paymentGates;
            _audit       = audit;
            _logger      = logger;
        }

        public async Task<IActionResult> OnGetAsync(string? payment, string? session_id, bool? cancel)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

            PaymentGates = await _paymentGates.GetAllAsync();

            ReferenceNumber   = user.ReferenceNumber;
            PaymentStatus     = user.PaymentStatus;
            
            var isBulgarian = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "bg";

            // Translated here rather than through resx: the four labels are also
            // the ones the mail uses, and they live in MailContext for the same
            // reason.
            ParticipationType = user.PartForm switch
            {
                "1" => isBulgarian ? "Лектор / Академик"   : "Lector / Academic",
                "2" => isBulgarian ? "Студент / Докторант" : "Student / PhD Candidate",
                "3" => isBulgarian ? "Онлайн участник"     : "Online Participant",
                "4" => isBulgarian ? "Журналист / Медия"   : "Journalist / Media",
                _   => user.PartForm // an unexpected value is shown as it is
            };
            
            IbanTransferSubmittedAt = user.IbanTransferSubmittedAt;

            // The publishable key is not a secret: the browser needs it to open
            // the Checkout session.
            StripePublishableKey = _config["Stripe:PublishableKey"] ?? string.Empty;

            // ── The browser has come back from Stripe Checkout ──────────────
            // The second route to a confirmation, alongside the webhook.
            if (payment == "success" && !string.IsNullOrEmpty(session_id))
            {
                var service = new SessionService();
                try
                {
                    var session = await service.GetAsync(session_id);

                    // [P-02] session_id sits in the address bar, in the browser
                    // history and in the Referer — it is not a secret. The
                    // session is therefore honoured only if it was created for
                    // THIS user. ClientReferenceId is set when it is created, in
                    // OnPostCreateStripeSessionAsync.
                    if (!string.Equals(session.ClientReferenceId, user.Id, StringComparison.Ordinal))
                    {
                        await _audit.LogAsync(user.Id, user.Email ?? string.Empty,
                            "Stripe Session Ownership Mismatch — Not Confirmed",
                            $"SessionId={session.Id} | ClientReferenceId={session.ClientReferenceId ?? "—"} | " +
                            $"CurrentUserId={user.Id} | Ref={user.ReferenceNumber} | Slug={Slug}");
                    }
                    else if (session.PaymentStatus == "paid")
                    {
                        if (user.PaymentStatus != "Confirmed")
                        {
                            var paidEUR = session.AmountTotal.GetValueOrDefault() / 100.0m;

                            // [P-06] The amount is checked against the price of the
                            // tier. Stripe's own signature already guarantees
                            // where the session came from, so a mismatch is
                            // recorded but does not stop the confirmation: a price
                            // edited in the panel while somebody was paying must
                            // not leave them stuck.
                            // [T-03] It is checked against what the participation
                            // form owes. The slug sits in the address bar next to
                            // session_id, so checking against it would mean
                            // checking against something the sender chooses.
                            var tiersForCheck = await _context.TicketTiers.ToListAsync();
                            var expectedEUR   = TicketPricing.PriceEUR(
                                                    TicketPricing.ForUser(tiersForCheck, user));
                            if (expectedEUR.HasValue && expectedEUR.Value != paidEUR)
                            {
                                // Add, not LogAsync: written below together with
                                // the row for the confirmed payment.
                                _audit.Add(user.Id, user.Email ?? string.Empty,
                                    "Payment Amount Mismatch — Stripe Session Sync",
                                    $"SessionId={session.Id} | Expected={TicketPricing.Format(expectedEUR)} | " +
                                    $"Paid={TicketPricing.Format(paidEUR)} | Slug={Slug} | Ref={user.ReferenceNumber}");
                            }

                            user.PaymentStatus = "Confirmed";
                            user.PaymentMethod = "Stripe:Card";
                            user.PaidAt = DateTime.UtcNow;
                            user.PaidAmountEUR = paidEUR;
                            await _userManager.UpdateAsync(user);

                            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                            _audit.Add(user.Id, user.Email ?? string.Empty,
                                "Payment Confirmed — Stripe Session Sync",
                                $"Stripe SessionId={session.Id} Ref={user.ReferenceNumber}", clientIp);
                            await _context.SaveChangesAsync();

                            // INSIDE `if (user.PaymentStatus != "Confirmed")`.
                            // This path runs on every reload of
                            // /Payment?payment=success, so outside the block the
                            // person would get a fresh mail on every refresh. The
                            // webhook in StripeController has a guard of its own;
                            // whichever arrives first sends the mail, the other
                            // sees "Confirmed" and stays quiet.
                            await _mail.SendPaymentConfirmedAsync(
                                toEmail:   user.Email ?? string.Empty,
                                firstName: user.FirstName ?? string.Empty,
                                // [T-28] TicketPricing.Format rather than
                                // $"{…:F2}": interpolation formats under the
                                // culture of the request and produced "60,00 EUR"
                                // in Bulgarian, while every other path writes
                                // "60.00 EUR". Which path reaches the mail first
                                // is a race ([T-01]), so one and the same payment
                                // produced one spelling or the other.
                                amount:    TicketPricing.Format(
                                               session.AmountTotal.GetValueOrDefault() / 100.0m),
                                method:    ConferenceApp.Services.Email.MailContext.PaymentMethodName("Card"),
                                reference: user.ReferenceNumber ?? "—",
                                culture:   ConferenceApp.Services.Email.MailContext.CultureFor(user),
                                baseUrl:   ConferenceApp.Services.Email.MailContext.BaseUrl(_config, Request));
                        }
                        PaymentStatus  = "Confirmed";
                        PaymentSuccess = true;
                    }
                }
                catch (Exception ex)
                {
                    // [C-19] This catch used to be empty and did not even name
                    // the exception (CS0168). Logging changes no behaviour — the
                    // failure is still swallowed and the page still renders
                    // without an error — but it now leaves a trace. The real fix
                    // is [P-05], which has not been applied.
                    _logger.LogError(ex,
                        "Stripe session sync се провали. SessionId={SessionId} Slug={Slug}",
                        session_id, Slug);
                }
            }
            else if (cancel == true)
            {
                ModelState.AddModelError(string.Empty, "Payment was cancelled.");
            }

            var ticketTiers = await _context.TicketTiers.ToListAsync();

            // [T-03] The tier is decided by the participation form, not by the
            // address. The page used to look only at the slug, and the slug is in
            // the visitor's hands: a student or a journalist — forms that by
            // design do not pay — got a working €60 form, and with a second
            // payable tier anyone could pay the cheaper one and come out
            // Confirmed.
            //
            // Nothing is refused here: a wrong tier is a redirect to the right
            // one.
            var ownTier = TicketPricing.ForUser(ticketTiers, user);

            if (!TicketPricing.IsPayable(ownTier))
            {
                // This participation form owes nothing (student, journalist) or
                // its tier is free. The profile is the page that says what comes
                // instead of a payment: verification with a document.
                return RedirectToPage("/Profile");
            }

            var ticket = TicketPricing.BySlug(ticketTiers, Slug);

            // [P-03] An unknown slug used to fall back to Id == 2, and a tier
            // with no price ("Free", "Fully Subsidized") showed "Pay €0.00" and
            // charged €120 by card or €60 in crypto.
            //
            // [T-03] An unknown or foreign slug now redirects to the page of the
            // tier owed rather than back to /Attend. SlugFor uses TierKey and
            // BySlug finds it by the same key, so the redirect happens once and
            // cannot loop.
            if (ticket == null || ticket.Id != ownTier!.Id)
                return RedirectToPage(new { slug = TicketPricing.SlugFor(ownTier!) });

            // ── The tier as it is displayed ──────────────────────────────────
            TierName         = isBulgarian ? ticket.NameBg : ticket.NameEn;
            TierDescription  = isBulgarian ? ticket.DescriptionBg : ticket.DescriptionEn;
            TierRegularPrice = isBulgarian ? ticket.RegularPriceBg : ticket.RegularPriceEn;
            TierPromoPrice   = (isBulgarian ? ticket.PromoPriceBg : ticket.PromoPriceEn) ?? string.Empty;
            var perksStr     = isBulgarian ? ticket.PerksBg : ticket.PerksEn;

            HasPromoPrice = !string.IsNullOrWhiteSpace(TierPromoPrice);

            // The number comes from the column, not from the display string.
            // IsPayable above has already guaranteed there is one.
            TierPriceEUR = TicketPricing.PriceEUR(ticket) ?? 0m;
            TotalEUR     = TierPriceEUR;

            TierPerks = string.IsNullOrWhiteSpace(perksStr)
                ? new List<string>()
                : perksStr
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.TrimStart('-', ' ').Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();

            try
            {
                SupportedCurrencies = await _go28.GetCurrenciesAsync();
            }
            catch
            {
                SupportedCurrencies = new List<Go28Currency>();
            }

            return Page();
        }

        public async Task<IActionResult> OnPostCreateStripeSessionAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (!await _paymentGates.IsEnabledAsync("all") || !await _paymentGates.IsEnabledAsync("method.card"))
                return BadRequest(new { error = "payments_disabled" });

            if (user.PaymentStatus == "Confirmed")
                return RedirectToPage(new { slug = Slug });

            var ticketTiers = await _context.TicketTiers.ToListAsync();

            // [T-03] The amount is decided by the participation form, not by the
            // slug in the address. Otherwise the redirect in OnGetAsync is
            // cosmetic: a hand-made form post would pay for any tier it liked.
            var ticket = TicketPricing.ForUser(ticketTiers, user);
            var amount = TicketPricing.PriceEUR(ticket);

            // [P-09] The fallback of 120 is gone. A tier without a price is a
            // refusal now, not an amount no tier actually costs.
            if (ticket == null || amount == null)
                return BadRequest(new { error = "tier_not_payable" });

            // A request for another tier is not quietly charged at a different
            // amount: it is recorded and redirected to the tier owed.
            var requested = TicketPricing.BySlug(ticketTiers, Slug);
            if (requested == null || requested.Id != ticket.Id)
            {
                await _audit.LogAsync(user.Id, user.Email ?? string.Empty,
                    "Stripe Session Refused — Tier Mismatch",
                    $"Requested={Slug} | Owed={ticket.TierKey} | Ref={user.ReferenceNumber}",
                    HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown");

                return RedirectToPage(new { slug = TicketPricing.SlugFor(ticket) });
            }

            long amountCents = TicketPricing.ToCents(amount.Value);

            var domain = _config["AppSettings:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}";
            domain = domain.TrimEnd('/');

            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            UnitAmount = amountCents,
                            Currency = "eur",
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = ticket.NameEn,
                            },
                        },
                        Quantity = 1,
                    },
                },
                Mode = "payment",
                SuccessUrl = domain + $"/Payment/{Slug}?payment=success&session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl  = domain + $"/Payment/{Slug}?cancel=true",
                ClientReferenceId = user.Id,
                CustomerEmail = user.Email
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            return Redirect(session.Url);
        }

        public async Task<IActionResult> OnPostSubmitIbanAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return new JsonResult(new { success = false, error = "unauthorized" });

            if (!await _paymentGates.IsEnabledAsync("all") || !await _paymentGates.IsEnabledAsync("method.iban"))
                return new JsonResult(new { success = false, error = "payments_disabled" });

            // [P-14] The same check the other two methods have. Without it
            // somebody who has already paid by card or in crypto can press "I
            // have made the transfer", receive a "payment pending" mail after the
            // "payment confirmed" one, and appear in the panel as having sent a
            // bank transfer.
            if (user.PaymentStatus == "Confirmed")
                return new JsonResult(new { success = false, error = "already_confirmed" });

            if (user.IbanTransferSubmittedAt == null)
            {
                user.IbanTransferSubmittedAt = DateTime.UtcNow;

                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                    return new JsonResult(new { success = false, error = "update_failed" });

                var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                await _audit.LogAsync(user.Id, user.Email ?? string.Empty,
                    "IBAN Transfer Submitted",
                    $"User clicked 'I Have Completed the Transfer'. Ref={user.ReferenceNumber}", clientIp);

                // TierPriceEUR is filled in only by OnGetAsync. This handler is a
                // separate request, where the value is still 0 and the mail would
                // have said "0.00 EUR", so the price is loaded again.
                var ibanAmount = await ResolveTierPriceAsync(user);

                // INSIDE `if (user.IbanTransferSubmittedAt == null)`: pressing
                // the button twice does not send a second mail.
                await _mail.SendPaymentPendingAsync(
                    toEmail:   user.Email ?? string.Empty,
                    firstName: user.FirstName ?? string.Empty,
                    amount:    ibanAmount,
                    method:    ConferenceApp.Services.Email.MailContext.PaymentMethodName("IBAN"),
                    reference: user.ReferenceNumber ?? "—",
                    culture:   ConferenceApp.Services.Email.MailContext.CultureFor(user),
                    baseUrl:   ConferenceApp.Services.Email.MailContext.BaseUrl(_config, Request));
            }

            return new JsonResult(new { success = true });
        }

        public bool IsCurrencySupported(string iso, string network)
        {
            // The Payment Control hierarchy: "all" && "method.crypto" &&
            // "currency.<ISO>", not the currency key alone. An empty
            // SupportedCurrencies means the gateway did not answer, and the page
            // then shows the currencies rather than nothing at all — creating the
            // order is where an unsupported one is refused.
            if (!IsMethodEnabled("crypto")) return false;
            if (!Gate($"currency.{iso}")) return false;

            if (!SupportedCurrencies.Any()) return true;
            return SupportedCurrencies.Any(c =>
                string.Equals(c.Iso,     iso,     StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Network, network, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The price of the ticket as text for the mail. It goes through
        /// TicketPricing, the same place the amount Stripe charges comes from.
        /// </summary>
        private async Task<string> ResolveTierPriceAsync(ApplicationUser user)
        {
            try
            {
                var tiers  = await _context.TicketTiers.ToListAsync();
                // [T-03] What the participation form owes, not the price of the
                // tier in the address: this mail tells the participant how much
                // to transfer.
                return TicketPricing.Format(
                    TicketPricing.PriceEUR(TicketPricing.ForUser(tiers, user)));
            }
            catch
            {
                // The price is informational here; failing to read it must not
                // fail the request that has already been recorded.
                return "—";
            }
        }

    }
}