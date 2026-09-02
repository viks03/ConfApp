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

        /// <summary>Състоянието на осемте ключа от Payment Control (Admin панел).</summary>
        public Dictionary<string, bool> PaymentGates { get; set; } = new();

        /// <summary>Липсващ ключ се чете като включено — огледално на Admin панела.</summary>
        private bool Gate(string key) => !PaymentGates.TryGetValue(key, out var g) || g;

        /// <summary>Общият ключ „all“ — когато е изключен, /Payment показва _PaymentsDisabled.</summary>
        public bool PaymentsEnabled => Gate("all");

        /// <summary>
        /// method е "card" | "crypto" | "iban". Йерархията не е каскадна в базата —
        /// проверява се тук: методът е достъпен само ако и "all", и самият метод са включени.
        /// </summary>
        public bool IsMethodEnabled(string method) => Gate("all") && Gate($"method.{method}");

        [BindProperty(SupportsGet = true)]
        public string Slug { get; set; }

        public string ReferenceNumber   { get; set; } = string.Empty;
        public string PaymentStatus     { get; set; } = "Pending";
        public string ParticipationType { get; set; } = string.Empty;
        
        // ── Изчистени стойности по подразбиране ──
        public decimal TotalEUR         { get; set; } = 0m;
        
        public DateTime? IbanTransferSubmittedAt { get; set; }

        public string TierName         { get; set; } = string.Empty;
        public string TierRegularPrice { get; set; } = string.Empty;
        public string TierPromoPrice   { get; set; } = string.Empty;
        public string TierDescription  { get; set; } = string.Empty;
        public List<string> TierPerks  { get; set; } = new();

        public decimal TierPriceEUR    { get; set; } = 0m;
        public bool HasPromoPrice      { get; set; } = false;

        // ── Aliases за съвместимост с Payment.cshtml ──
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
            ConferenceApp.Services.IPaymentGateSettings paymentGates)
        {
            _userManager = userManager;
            _go28        = go28;
            _stripe      = stripe;
            _context     = context;
            _config      = config;
            _mail        = mail;
            _paymentGates = paymentGates;
        }

        public async Task<IActionResult> OnGetAsync(string? payment, string? session_id, bool? cancel)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

            PaymentGates = await _paymentGates.GetAllAsync();

            ReferenceNumber   = user.ReferenceNumber;
            PaymentStatus     = user.PaymentStatus;
            
            // ── Проверяваме какъв е езикът на страницата в момента ──
            var isBulgarian = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "bg";

            // ── Превеждаме PartForm към четим текст спрямо езика ──
            ParticipationType = user.PartForm switch
            {
                "1" => isBulgarian ? "Лектор / Академик"   : "Lector / Academic",
                "2" => isBulgarian ? "Студент / Докторант" : "Student / PhD Candidate",
                "3" => isBulgarian ? "Онлайн участник"     : "Online Participant",
                "4" => isBulgarian ? "Журналист / Медия"   : "Journalist / Media",
                _   => user.PartForm // Fallback
            };
            
            IbanTransferSubmittedAt = user.IbanTransferSubmittedAt;

            // Зареждаме Stripe publishable key за JS
            StripePublishableKey = _config["Stripe:PublishableKey"] ?? string.Empty;

            // Stripe Verification (if redirected back)
            if (payment == "success" && !string.IsNullOrEmpty(session_id))
            {
                var service = new SessionService();
                try
                {
                    var session = await service.GetAsync(session_id);
                    if (session.PaymentStatus == "paid")
                    {
                        if (user.PaymentStatus != "Confirmed")
                        {
                            user.PaymentStatus = "Confirmed";
                            user.PaymentMethod = "Stripe:Card";
                            user.PaidAt = DateTime.UtcNow;
                            await _userManager.UpdateAsync(user);

                            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                            _context.Set<AuditLog>().Add(new AuditLog
                            {
                                UserId = user.Id,
                                UserEmail = user.Email ?? string.Empty,
                                Action = "Payment Confirmed — Stripe Session Sync",
                                IpAddress = clientIp,
                                Details = $"Stripe SessionId={session.Id} Ref={user.ReferenceNumber}",
                                Timestamp = DateTime.UtcNow
                            });
                            await _context.SaveChangesAsync();

                            // ВЪТРЕ в `if (user.PaymentStatus != "Confirmed")`.
                            // Този път се минава при всяко презареждане на
                            // /Payment?payment=success — извън блока потребителят
                            // щеше да получава ново писмо при всяко refresh.
                            // Webhook-ът в StripeController има собствен guard;
                            // който от двата стигне пръв, праща писмото, другият
                            // вижда "Confirmed" и мълчи.
                            await _mail.SendPaymentConfirmedAsync(
                                toEmail:   user.Email ?? string.Empty,
                                firstName: user.FirstName ?? string.Empty,
                                amount:    $"{session.AmountTotal.GetValueOrDefault() / 100.0m:F2} {session.Currency?.ToUpper() ?? "EUR"}",
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
                    // Логване на грешка (опционално)
                }
            }
            else if (cancel == true)
            {
                ModelState.AddModelError(string.Empty, "Payment was cancelled.");
            }

            var ticketTiers = await _context.TicketTiers.ToListAsync();
            var ticket = ticketTiers.FirstOrDefault(t => GenerateSlug(t.NameEn) == Slug)
                         ?? ticketTiers.FirstOrDefault(t => t.Id == 2);

            if (ticket == null) return RedirectToPage("/Attend");

            // ── Тук се пълнят реалните данни за билета, ползвайки същата isBulgarian проверка ──
            TierName         = isBulgarian ? ticket.NameBg : ticket.NameEn;
            TierDescription  = isBulgarian ? ticket.DescriptionBg : ticket.DescriptionEn;
            TierRegularPrice = isBulgarian ? ticket.RegularPriceBg : ticket.RegularPriceEn;
            TierPromoPrice   = isBulgarian ? ticket.PromoPriceBg : ticket.PromoPriceEn;
            var perksStr     = isBulgarian ? ticket.PerksBg : ticket.PerksEn;

            HasPromoPrice = !string.IsNullOrWhiteSpace(TierPromoPrice);

            string priceStr = HasPromoPrice ? TierPromoPrice : TierRegularPrice;
            var match = Regex.Match(priceStr, @"\d+");
            if (match.Success && decimal.TryParse(match.Value, out var parsed))
            {
                TierPriceEUR = parsed;
                TotalEUR     = parsed;
            }

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
            var ticket = ticketTiers.FirstOrDefault(t => GenerateSlug(t.NameEn) == Slug)
                         ?? ticketTiers.FirstOrDefault(t => t.Id == 2);

            decimal amountEUR = 120.0m;
            if (ticket != null)
            {
                var priceStr = !string.IsNullOrWhiteSpace(ticket.PromoPriceEn) ? ticket.PromoPriceEn : ticket.RegularPriceEn;
                var match = Regex.Match(priceStr, @"\d+");
                if (match.Success && decimal.TryParse(match.Value, out var parsed))
                    amountEUR = parsed;
            }

            long amountCents = (long)(amountEUR * 100);

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
                                Name = ticket?.NameEn ?? "Conference Registration",
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

            if (user.IbanTransferSubmittedAt == null)
            {
                user.IbanTransferSubmittedAt = DateTime.UtcNow;

                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                    return new JsonResult(new { success = false, error = "update_failed" });

                var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                _context.Set<AuditLog>().Add(new AuditLog
                {
                    UserId    = user.Id,
                    UserEmail = user.Email ?? string.Empty,
                    Action    = "IBAN Transfer Submitted",
                    IpAddress = clientIp,
                    Details   = $"User clicked 'I Have Completed the Transfer'. Ref={user.ReferenceNumber}",
                    Timestamp = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                // TierPriceEUR се пълни само в OnGetAsync. Този handler е
                // отделна заявка, така че тук стойността е 0 и имейлът щеше да
                // казва "0.00 EUR". Затова цената се зарежда наново.
                var ibanAmount = await ResolveTierPriceAsync();

                // ВЪТРЕ в `if (user.IbanTransferSubmittedAt == null)` — второ
                // натискане на бутона не праща второ писмо.
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
            // Йерархията от Payment Control: "all" && "method.crypto" && "currency.<ISO>",
            // не само самата валута — виж бележката в INTEGRATION.md.
            if (!IsMethodEnabled("crypto")) return false;
            if (!Gate($"currency.{iso}")) return false;

            if (!SupportedCurrencies.Any()) return true;
            return SupportedCurrencies.Any(c =>
                string.Equals(c.Iso,     iso,     StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Network, network, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Цената на билета като текст за имейла. Ползва същата логика като
        /// OnGetAsync (език -> tier -> промо или редовна цена -> число).
        /// </summary>
        private async Task<string> ResolveTierPriceAsync()
        {
            try
            {
                var isBulgarian = System.Globalization.CultureInfo.CurrentUICulture
                                      .TwoLetterISOLanguageName == "bg";

                var tiers  = await _context.TicketTiers.ToListAsync();
                var ticket = tiers.FirstOrDefault(t => GenerateSlug(t.NameEn) == Slug)
                             ?? tiers.FirstOrDefault(t => t.Id == 2);
                if (ticket == null) return "—";

                var regular = isBulgarian ? ticket.RegularPriceBg : ticket.RegularPriceEn;
                var promo   = isBulgarian ? ticket.PromoPriceBg   : ticket.PromoPriceEn;
                var priceStr = !string.IsNullOrWhiteSpace(promo) ? promo : regular;

                var match = Regex.Match(priceStr ?? string.Empty, @"\d+");
                return match.Success && decimal.TryParse(match.Value, out var parsed)
                    ? $"{parsed:F2} EUR"
                    : "—";
            }
            catch
            {
                // Цената е информативна — липсата ѝ не бива да проваля заявката.
                return "—";
            }
        }

        private string GenerateSlug(string phrase)
        {
            if (string.IsNullOrEmpty(phrase)) return "";
            string str = phrase.ToLower();
            str = Regex.Replace(str, @"[^a-z0-9\s-]", ""); 
            str = Regex.Replace(str, @"\s+", " ").Trim(); 
            str = str.Replace(" ", "-"); 
            return str;
        }
    }
}