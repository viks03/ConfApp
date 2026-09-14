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
using ConferenceApp.Services.Payments;

namespace ConferenceApp.Controllers
{
    // ════════════════════════════════════════════════════════════════
    // DTOs — the request and response shapes the crypto payment page speaks.
    // ════════════════════════════════════════════════════════════════

    public class CreateCryptoOrderRequest
    {
        public string Currency { get; set; } = string.Empty;
        public string Network  { get; set; } = string.Empty;

        // The tier whose page the request came from. The controller used to be
        // fixed on Id == 2 and the slug in the address meant nothing, which is
        // where the third different amount for one and the same payment came
        // from ([P-03]). It is now only cross-checked: the amount itself comes
        // from the participation form.
        public string? Slug    { get; set; }
    }

    public class CryptoOrderResponse
    {
        public bool    Success           { get; set; }
        public string? Error             { get; set; }
        public int?    OrderId           { get; set; }
        public string? CryptoAddress     { get; set; }
        public string? QrCode            { get; set; }
        public string? Amount            { get; set; }
        public string? AmountInEUR       { get; set; }
        public string? Currency          { get; set; }
        public string? Network           { get; set; }
        public string? ExpiresAt         { get; set; }
        public string? Status            { get; set; }
        public string? DeviationPercent  { get; set; }
        public int?    ExpirationMinutes { get; set; }
    }

    public class CheckOrderStatusResponse
    {
        public bool    Success   { get; set; }
        public string? Error     { get; set; }
        public string? Status    { get; set; }
        public bool    IsPaid    { get; set; }
        public bool    IsExpired { get; set; }
    }

    // ════════════════════════════════════════════════════════════════
    // CryptoController — the crypto half of the payment flow.
    //
    // A payment lives in three places at once: the order at Go28, the row in
    // CryptoOrders, and PaymentStatus on the user. Three different routes learn
    // that money has arrived — the page polling check-status every 15 seconds,
    // the page reloading and calling active-order, and Go28's webhook — and any
    // of them can be first. Each therefore re-reads the order from Go28,
    // compares the amount against the row we wrote when we created it, and
    // refuses to confirm twice.
    //
    // Nothing in an incoming body is trusted beyond the order number: see the
    // comment in Webhook().
    // ════════════════════════════════════════════════════════════════

    [ApiController]
    [Route("api/crypto")]
    [Authorize]
    public class CryptoController : ControllerBase
    {
        private readonly Go28Service                  _go28;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext         _context;
        private readonly ILogger<CryptoController>   _logger;
        private readonly ConferenceApp.Services.Email.IMailComposer _mail;
        private readonly IConfiguration               _config;
        private readonly ConferenceApp.Services.IPaymentGateSettings _paymentGates;
        private readonly ConferenceApp.Services.AuditService        _audit;

        public CryptoController(
            Go28Service go28,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            ILogger<CryptoController> logger,
            ConferenceApp.Services.Email.IMailComposer mail,
            IConfiguration config,
            ConferenceApp.Services.IPaymentGateSettings paymentGates,
            ConferenceApp.Services.AuditService audit)
        {
            _go28        = go28;
            _userManager = userManager;
            _context     = context;
            _logger      = logger;
            _mail        = mail;
            _config      = config;
            _paymentGates = paymentGates;
            _audit       = audit;
        }

        // ────────────────────────────────────────────────────────────
        // GET /api/crypto/active-order
        // Restores the active order after a page reload without creating a new
        // one. Reads the local row, then asks Go28 to confirm what it says.
        // ────────────────────────────────────────────────────────────
        [HttpGet("active-order")]
        public async Task<IActionResult> GetActiveOrder()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized(new CryptoOrderResponse { Success = false, Error = "Unauthorized." });

            if (user.PaymentStatus == "Confirmed")
                return Ok(new CryptoOrderResponse { Success = false, Error = "already_confirmed" });

            var localOrder = await _context.CryptoOrders
                .Where(o => o.UserId == user.Id && o.Status == "InProcess")
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

            if (localOrder == null)
                return Ok(new CryptoOrderResponse { Success = false, Error = "no_active_order" });

            // Expired by our own clock: no point asking Go28.
            if (localOrder.ExpiresAt.HasValue && localOrder.ExpiresAt.Value < DateTime.UtcNow)
            {
                localOrder.Status = "Expired";
                await _context.SaveChangesAsync();
                return Ok(new CryptoOrderResponse { Success = false, Error = "order_expired" });
            }

            // The real status comes from Go28; the local row can be stale.
            var go28Order = await _go28.GetOrderAsync(localOrder.Go28OrderId);
            if (go28Order == null)
                return Ok(new CryptoOrderResponse { Success = false, Error = "no_active_order" });

            if (go28Order.Status != "InProcess")
            {
                localOrder.Status = go28Order.Status;
                if (go28Order.Status == "Confirmed") localOrder.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                if (go28Order.Status == "Expired" || go28Order.Status == "Cancelled")
                    return Ok(new CryptoOrderResponse { Success = false, Error = "order_expired" });

                // [P-11] This branch used to return without touching the user:
                // the page showed "Confirmed" while PaymentStatus stayed
                // "Pending". The scenario is real — paying from a wallet in
                // another tab and coming back to the page while the webhook got
                // lost. It now takes the same path as the polling route.
                if (string.Equals(go28Order.Status, "Confirmed", StringComparison.OrdinalIgnoreCase))
                {
                    var activeIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                    await ConfirmCryptoPaymentAsync(user, localOrder, go28Order, activeIp, "Active Order");
                }
            }

            var currencies = await _go28.GetCurrenciesAsync();
            var supported  = currencies.FirstOrDefault(c =>
                string.Equals(c.Iso,     localOrder.Currency, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Network, localOrder.Network,  StringComparison.OrdinalIgnoreCase));

            return Ok(new CryptoOrderResponse
            {
                Success           = true,
                OrderId           = localOrder.Go28OrderId,
                CryptoAddress     = localOrder.WalletAddress,
                QrCode            = localOrder.QrCode,
                Amount            = $"{localOrder.CryptoAmount} {localOrder.Currency}",
                AmountInEUR       = FormatEur(localOrder.AmountEUR),
                Currency          = localOrder.Currency,
                Network           = localOrder.Network,
                ExpiresAt         = localOrder.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                Status            = localOrder.Status,
                DeviationPercent  = supported?.DeviationPercent,
                ExpirationMinutes = supported?.ExpirationTimeInMinutes
            });
        }

        // ────────────────────────────────────────────────────────────
        // POST /api/crypto/create-order
        //
        // In order:
        //   1. Validate the currency and network against the Go28 currency list.
        //   2. Mark locally expired orders, by ExpiresAt.
        //   3. Check the per-currency limit BEFORE calling Go28.
        //   4. Return the existing order if one is active for the SAME currency.
        //   5. A different currency means a new order; the old one expires by
        //      itself at Go28.
        //   6. Store the new order in CryptoOrders.
        // ────────────────────────────────────────────────────────────
        [HttpPost("create-order")]
        public async Task<IActionResult> CreateOrder([FromBody] CreateCryptoOrderRequest request)
        {
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            if (string.IsNullOrWhiteSpace(request.Currency) || string.IsNullOrWhiteSpace(request.Network))
                return BadRequest(new CryptoOrderResponse { Success = false, Error = "Currency and network are required." });

            // Payment Control in the admin panel: the master switch, the
            // "crypto" method, or this one currency can each be off.
            if (!await _paymentGates.IsEnabledAsync("all")
                || !await _paymentGates.IsEnabledAsync("method.crypto")
                || !await _paymentGates.IsEnabledAsync($"currency.{request.Currency.ToUpperInvariant()}"))
            {
                return BadRequest(new CryptoOrderResponse { Success = false, Error = "payments_disabled" });
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized(new CryptoOrderResponse { Success = false, Error = "User not found." });

            if (string.IsNullOrEmpty(user.ReferenceNumber))
            {
                await WriteAuditAsync(user, "Crypto Order Failed",
                    $"No ReferenceNumber assigned. Currency={request.Currency}", clientIp);
                return BadRequest(new CryptoOrderResponse
                {
                    Success = false,
                    Error   = "Your account has no reference number. Please contact support."
                });
            }

            if (user.PaymentStatus == "Confirmed")
                return BadRequest(new CryptoOrderResponse { Success = false, Error = "Payment already confirmed." });

            // ── The price comes from the tier's numeric column ───────
            // [T-03] The tier is decided by the participation form, not by the
            // slug in the request body. The slug used to decide the amount, and
            // it comes from the client: a student or a journalist — forms that
            // do not pay at all — could create a €60 order, and with a second
            // payable tier anyone could order the cheaper one.
            //
            // There is no fallback number: a tier without a price is a refusal,
            // not 120 EUR.
            var tiers = await _context.TicketTiers.ToListAsync();
            var tier  = TicketPricing.ForUser(tiers, user);

            var tierPrice = TicketPricing.PriceEUR(tier);
            if (tier == null || tierPrice == null)
            {
                _logger.LogWarning(
                    "Crypto order refused — no payable tier. PartForm={PartForm} Slug={Slug} User={Email}",
                    user.PartForm, request.Slug ?? "—", user.Email);
                await WriteAuditAsync(user, "Crypto Order Failed — Tier Not Payable",
                    $"PartForm={user.PartForm} | Slug={request.Slug ?? "—"} | Tier={tier?.TierKey ?? "—"}", clientIp);
                return BadRequest(new CryptoOrderResponse
                {
                    Success = false,
                    Error   = "This ticket tier is not payable."
                });
            }

            // A request for a different tier is not quietly served at another
            // amount — it is refused and recorded. The live client sends the
            // slug of the page it is on, and that page is already the one for
            // the tier owed.
            var requestedTier = TicketPricing.BySlug(tiers, request.Slug);
            if (!string.IsNullOrWhiteSpace(request.Slug)
                && (requestedTier == null || requestedTier.Id != tier.Id))
            {
                _logger.LogWarning(
                    "Crypto order refused — tier mismatch. Requested={Slug} Owed={Owed} User={Email}",
                    request.Slug, tier.TierKey, user.Email);
                await WriteAuditAsync(user, "Crypto Order Refused — Tier Mismatch",
                    $"Requested={request.Slug} | Owed={tier.TierKey} | Ref={user.ReferenceNumber}", clientIp);
                return BadRequest(new CryptoOrderResponse
                {
                    Success = false,
                    Error   = "This ticket tier does not match your participation form."
                });
            }

            var conferenceAmountEUR = tierPrice.Value;

            // ── The currencies Go28 accepts ─────────────────────────
            var currencies = await _go28.GetCurrenciesAsync();

            // [P-04] An empty list means Go28 did not answer — not that
            // anything goes. Every check below used to sit inside
            // `if (currencies.Any())`, so with Go28 unreachable or slow an order
            // was created with an unchecked currency and network taken from the
            // request body, and without the local MaxActiveOrders limit — the
            // one defence that is under our control.
            if (!currencies.Any())
            {
                await WriteAuditAsync(user, "Crypto Order Failed — Go28 Unavailable",
                    $"Currency list empty. Currency={request.Currency} Network={request.Network}", clientIp);
                return StatusCode(503, new CryptoOrderResponse
                {
                    Success = false,
                    Error   = "Crypto payments are temporarily unavailable. Please try again in a few minutes."
                });
            }

            Go28Currency? supported = currencies.FirstOrDefault(c =>
                string.Equals(c.Iso,     request.Currency, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Network, request.Network,  StringComparison.OrdinalIgnoreCase));

            if (supported == null)
            {
                await WriteAuditAsync(user, "Crypto Order Failed",
                    $"Unsupported currency/network: {request.Currency}/{request.Network}", clientIp);
                return BadRequest(new CryptoOrderResponse
                {
                    Success = false,
                    Error   = $"{request.Currency} on network {request.Network} is not currently supported."
                });
            }

            if (decimal.TryParse(supported.MinAmountInEUR,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var minEur)
                && conferenceAmountEUR < minEur)
            {
                await WriteAuditAsync(user, "Crypto Order Failed",
                    $"Amount €{conferenceAmountEUR} below minimum €{minEur} for {request.Currency}", clientIp);
                return BadRequest(new CryptoOrderResponse
                {
                    Success = false,
                    Error   = $"Minimum amount for {request.Currency} is €{minEur:F2}."
                });
            }

            // ── Mark the locally expired orders ─────────────────
            // Before counting, so that yesterday's abandoned order does not
            // count against today's limit.
            var now = DateTime.UtcNow;
            var expiredLocally = await _context.CryptoOrders
                .Where(o => o.UserId == user.Id
                         && o.Status == "InProcess"
                         && o.ExpiresAt.HasValue
                         && o.ExpiresAt.Value < now)
                .ToListAsync();

            foreach (var exp in expiredLocally) exp.Status = "Expired";
            if (expiredLocally.Any()) await _context.SaveChangesAsync();

            // ── The limit is checked BEFORE Go28 is called ──────
            // Go28 enforces MaxActiveOrders itself, but its refusal costs a
            // round trip and comes back as a bare failure.
            var activeCount = await _context.CryptoOrders
                .CountAsync(o => o.UserId   == user.Id
                              && o.Currency == request.Currency
                              && o.Network  == request.Network
                              && o.Status   == "InProcess");

            if (activeCount >= supported.MaxActiveOrders)
            {
                await WriteAuditAsync(user, "Crypto Order Failed — Limit",
                    $"Active {activeCount}/{supported.MaxActiveOrders} for {request.Currency}/{request.Network}", clientIp);
                return BadRequest(new CryptoOrderResponse
                {
                    Success = false,
                    Error   = $"You already have {activeCount} active {request.Currency} order(s). " +
                              $"Maximum is {supported.MaxActiveOrders}. " +
                              $"Please wait up to {supported.ExpirationTimeInMinutes} min or select a different currency."
                });
            }

            // ── An active order for the SAME currency is returned as it is ─
            // Pressing the button twice must not leave two addresses waiting for
            // the same money.
            var existingOrder = await _context.CryptoOrders
                .Where(o => o.UserId   == user.Id
                         && o.Currency == request.Currency
                         && o.Network  == request.Network
                         && o.Status   == "InProcess")
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

            if (existingOrder != null)
            {
                _logger.LogInformation(
                    "Returning existing active order. User={Email} Go28OrderId={OrderId} Currency={Currency}",
                    user.Email, existingOrder.Go28OrderId, existingOrder.Currency);

                return Ok(new CryptoOrderResponse
                {
                    Success           = true,
                    OrderId           = existingOrder.Go28OrderId,
                    CryptoAddress     = existingOrder.WalletAddress,
                    QrCode            = existingOrder.QrCode,
                    Amount            = $"{existingOrder.CryptoAmount} {existingOrder.Currency}",
                    AmountInEUR       = FormatEur(existingOrder.AmountEUR),
                    Currency          = existingOrder.Currency,
                    Network           = existingOrder.Network,
                    ExpiresAt         = existingOrder.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                    Status            = existingOrder.Status,
                    DeviationPercent  = supported?.DeviationPercent,
                    ExpirationMinutes = supported?.ExpirationTimeInMinutes
                });
            }

            // ── Create the order at Go28 ─────────────────────────────
            // The external id carries the reference number, the currency and a
            // timestamp: the reference is what the webhook is matched on, and
            // the timestamp keeps a second order for the same currency unique.
            var externalId = $"{user.ReferenceNumber}-{request.Currency}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            var order = await _go28.CreateOrderAsync(
                externalId:  externalId,
                amountInEUR: conferenceAmountEUR,
                currency:    request.Currency,
                network:     request.Network);

            if (order == null)
            {
                await WriteAuditAsync(user, "Crypto Order Failed — Go28 Error",
                    $"Go28 returned null. Currency={request.Currency} Network={request.Network}",
                    clientIp);
                return StatusCode(502, new CryptoOrderResponse
                {
                    Success = false,
                    Error   = "Could not create payment order. Please try again in a moment."
                });
            }

            // ── ExpiresAt ────────────────────────────────────────────
            // Go28 sends "yyyy-MM-dd HH:mm:ss" with no zone. It is UTC, so the
            // value is made explicit here rather than left to the local clock.
            DateTime? expiresAtUtc = null;
            if (!string.IsNullOrEmpty(order.ExpiresAt))
            {
                if (DateTime.TryParse(order.ExpiresAt.Replace(" ", "T") + "Z",
                    null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                    expiresAtUtc = parsed;
            }

            // ── Store it ─────────────────────────────────────────────
            var cryptoOrder = new CryptoOrder
            {
                UserId        = user.Id,
                Go28OrderId   = order.Id,
                ExternalId    = externalId,
                Currency      = order.Currency,
                Network       = order.Network,
                // The amount WE asked Go28 for, not the one the response
                // claims. Confirmation is checked against this.
                AmountEUR     = conferenceAmountEUR,
                CryptoAmount  = order.Amount,
                NetAmount     = order.NetAmount,
                FeeAmount     = order.FeeAmount,
                WalletAddress = order.CryptoAddress,
                QrCode        = order.CryptoAddressQrCode,
                Status        = "InProcess",
                CreatedAt     = DateTime.UtcNow,
                ExpiresAt     = expiresAtUtc
            };
            _context.CryptoOrders.Add(cryptoOrder);

            // Until the payment lands, PaymentMethod carries the whole order:
            // the admin panel reads the order number out of it when a payment
            // has to be traced by hand. On confirmation it is shortened to
            // "Crypto:{currency}".
            user.PaymentMethod = $"Crypto:{order.Currency}:{order.Network}:{order.Id}";
            await _userManager.UpdateAsync(user);
            await _context.SaveChangesAsync();

            await WriteAuditAsync(user, "Crypto Order Created",
                $"Go28 OrderId={order.Id} | ExternalId={externalId} | Currency={order.Currency} | " +
                $"Network={order.Network} | EUR={conferenceAmountEUR} | CryptoAmount={order.Amount} | " +
                $"NetAmount={order.NetAmount} | Fee={order.FeeAmount} | Address={order.CryptoAddress} | " +
                $"QR={(order.CryptoAddressQrCode != null ? "Yes" : "No")} | ExpiresAt={order.ExpiresAt}",
                clientIp);

            _logger.LogInformation(
                "Crypto order created. User={Email} Ref={Ref} Go28OrderId={OrderId} Currency={Currency}",
                user.Email, user.ReferenceNumber, order.Id, order.Currency);

            return Ok(new CryptoOrderResponse
            {
                Success           = true,
                OrderId           = order.Id,
                CryptoAddress     = order.CryptoAddress,
                QrCode            = order.CryptoAddressQrCode,
                Amount            = $"{order.Amount} {order.Currency}",
                AmountInEUR       = order.AmountInEUR,
                Currency          = order.Currency,
                Network           = order.Network,
                ExpiresAt         = order.ExpiresAt,
                Status            = order.Status,
                DeviationPercent  = supported?.DeviationPercent,
                ExpirationMinutes = supported?.ExpirationTimeInMinutes
            });
        }

        // ────────────────────────────────────────────────────────────
        // GET /api/crypto/check-status/{orderId:int}
        // ────────────────────────────────────────────────────────────
        [HttpGet("check-status/{orderId:int}")]
        public async Task<IActionResult> CheckStatus(int orderId)
        {
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Unauthorized(new CheckOrderStatusResponse { Success = false, Error = "Unauthorized." });

            // The order is looked up by id AND user: without the second
            // condition anyone signed in could poll anyone else's payment.
            var localOrder = await _context.CryptoOrders
                .FirstOrDefaultAsync(o => o.Go28OrderId == orderId && o.UserId == user.Id);

            if (localOrder == null)
            {
                _logger.LogWarning("Security: User {Email} tried to check order {OrderId} not in their orders.",
                    user.Email, orderId);
                return Forbid();
            }

            var order = await _go28.GetOrderAsync(orderId);
            if (order == null)
            {
                return StatusCode(502, new CheckOrderStatusResponse
                {
                    Success = false,
                    Error   = "Could not check payment status. Please wait and try again."
                });
            }

            var isConfirmed = string.Equals(order.Status, "Confirmed",  StringComparison.OrdinalIgnoreCase);
            var isExpired   = string.Equals(order.Status, "Expired",    StringComparison.OrdinalIgnoreCase);
            var isCancelled = string.Equals(order.Status, "Cancelled",  StringComparison.OrdinalIgnoreCase);

            // Keep the local row in step with Go28.
            if (localOrder.Status != order.Status)
            {
                localOrder.Status = order.Status;
                if (isConfirmed) localOrder.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            if (isConfirmed)
                await ConfirmCryptoPaymentAsync(user, localOrder, order, clientIp, "Polling");

            if (isExpired)
                await WriteAuditAsync(user, "Crypto Order Expired",
                    $"Go28 OrderId={orderId} | Currency={order.Currency} | ExpiresAt={order.ExpiresAt}", clientIp);

            if (isCancelled)
                await WriteAuditAsync(user, "Crypto Order Cancelled",
                    $"Go28 OrderId={orderId} | Currency={order.Currency} | Network={order.Network}", clientIp);

            return Ok(new CheckOrderStatusResponse
            {
                Success   = true,
                // Go28 may say "Confirmed" while the confirmation was stopped
                // by an amount mismatch — the user is then still Pending and the
                // page must not draw "paid".
                Status    = order.Status,
                IsPaid    = isConfirmed && user.PaymentStatus == "Confirmed",
                IsExpired = isExpired || isCancelled
            });
        }

        // ────────────────────────────────────────────────────────────
        // POST /api/crypto/webhook
        // ────────────────────────────────────────────────────────────
        // [P-01] An anonymous endpoint: the rate is limited by the "webhook"
        // policy in Program.cs and the body by RequestSizeLimit. Go28 sends a
        // few kilobytes of JSON; anything larger is not from them.
        //
        // The body arrives as JSON or as form data depending on how the gateway
        // is configured, so both are accepted.
        [HttpPost("webhook")]
        [AllowAnonymous]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("webhook")]
        [RequestSizeLimit(64 * 1024)]
        public async Task<IActionResult> Webhook()
        {
            Go28Order? payload = null;
            var contentType = Request.ContentType ?? string.Empty;

            if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new System.IO.StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(body))
                {
                    try { payload = System.Text.Json.JsonSerializer.Deserialize<Go28Order>(body); }
                    catch (Exception ex) { _logger.LogError(ex, "Webhook: failed to parse JSON body."); }
                }
            }
            else if (contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
                  || contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            {
                var form = Request.Form;
                payload = new Go28Order
                {
                    Id             = int.TryParse(form["id"], out var wId) ? wId : 0,
                    ExternalId     = form["externalId"].ToString(),
                    Status         = form["status"].ToString(),
                    Currency       = form["currency"].ToString(),
                    Network        = form["network"].ToString(),
                    AmountInEUR    = form["amountInEUR"].ToString(),
                    Amount         = form["amount"].ToString(),
                    NetAmount      = form["netAmount"].ToString(),
                    FeeAmount      = form["feeAmount"].ToString(),
                    ReceivedAmount = form["receivedAmount"].ToString(),
                    CompletedAt    = form["completedAt"].ToString(),
                    ExpiresAt      = form["expiresAt"].ToString()
                };
            }

            if (payload == null || payload.Id <= 0)
            {
                _logger.LogWarning("Webhook: null payload or missing order id. ContentType={CT}", contentType);
                return BadRequest();
            }

            var webhookIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            _logger.LogInformation("Webhook received. OrderId={OrderId} ReportedStatus={Status} ReportedExternalId={ExternalId}",
                payload.Id, payload.Status, payload.ExternalId);

            // [P-01] The body is not signed and Go28 sends nothing that could
            // verify it. Only the order number is taken from it; everything else
            // — status, external id, amount, currency — is read back from Go28,
            // and every decision below is made on that answer alone.
            var order = await _go28.GetOrderAsync(payload.Id);

            if (order == null)
            {
                // Either Go28 is unreachable or no such order exists (a forged
                // body). Nothing is confirmed either way. The 502 makes Go28
                // retry; the page polling every 15 seconds is the fallback.
                _logger.LogWarning(
                    "Webhook: Go28 did not return order {OrderId}. Nothing confirmed. ReportedStatus={Status}",
                    payload.Id, payload.Status);
                _audit.Add(null, payload.ExternalId, "Webhook — Unverified, Ignored",
                    $"Go28 OrderId={payload.Id} | ReportedExternalId={payload.ExternalId} | " +
                    $"ReportedStatus={payload.Status} | Go28 lookup returned nothing", webhookIp);
                await _context.SaveChangesAsync();
                return StatusCode(502);
            }

            if (!string.Equals(order.Status, payload.Status, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Webhook: body status {Reported} differs from Go28 status {Verified}. OrderId={OrderId}",
                    payload.Status, order.Status, order.Id);
            }

            _audit.Add(null, order.ExternalId, $"Webhook Received — {order.Status}",
                $"Go28 OrderId={order.Id} | ExternalId={order.ExternalId} | " +
                $"Status={order.Status} (reported={payload.Status}) | Currency={order.Currency} | " +
                $"Network={order.Network} | EUR={order.AmountInEUR} | " +
                $"GrossAmount={order.Amount} | NetAmount={order.NetAmount} | " +
                $"Fee={order.FeeAmount} | Received={order.ReceivedAmount ?? "—"} | " +
                $"CompletedAt={order.CompletedAt ?? "—"}", webhookIp);

            // Bring the local row up to date whatever the status is.
            var localOrder = await _context.CryptoOrders
                .FirstOrDefaultAsync(o => o.Go28OrderId == order.Id);

            if (localOrder != null && localOrder.Status != order.Status)
            {
                localOrder.Status = order.Status;
                if (order.Status == "Confirmed") localOrder.CompletedAt = DateTime.UtcNow;
            }

            if (!string.Equals(order.Status, "Confirmed", StringComparison.OrdinalIgnoreCase))
            {
                await _context.SaveChangesAsync();
                return Ok();
            }

            var referenceNumber = ExtractReferenceNumber(order.ExternalId);

            // [D-03] The empty string is a reachable value:
            // ExtractReferenceNumber returns it for an empty externalId. Passed
            // to the query, it matches the first account that happens to have an
            // empty ReferenceNumber (there are such rows — see [A-09]) and
            // confirms it as paid. StripeController makes exactly this check and
            // gives up; here it was missing.
            if (string.IsNullOrWhiteSpace(referenceNumber))
            {
                _logger.LogWarning(
                    "Webhook: empty reference extracted from ExternalId={ExternalId}. Nothing confirmed. OrderId={OrderId}",
                    order.ExternalId, order.Id);
                _audit.Add(null,
                    string.IsNullOrWhiteSpace(order.ExternalId) ? "Unknown" : order.ExternalId,
                    "Webhook — Empty Reference, Ignored",
                    $"Go28 OrderId={order.Id} | ExternalId={order.ExternalId} | " +
                    "No reference number could be extracted", webhookIp);
                await _context.SaveChangesAsync();
                return Ok();
            }

            var user = await _userManager.Users
                .FirstOrDefaultAsync(u => u.ReferenceNumber == referenceNumber);

            if (user == null)
            {
                _logger.LogWarning("Webhook: No user with ReferenceNumber={Ref}.", referenceNumber);
                _audit.Add(null, order.ExternalId, "Webhook — User Not Found",
                    $"Go28 OrderId={order.Id} | ExternalId={order.ExternalId} | ExtractedRef={referenceNumber}",
                    webhookIp);
                await _context.SaveChangesAsync();
                return Ok();
            }

            if (user.PaymentStatus == "Confirmed")
            {
                await _context.SaveChangesAsync();
                return Ok();
            }

            // [P-06] The amount Go28 reports against the amount WE created the
            // order for. With no local order, or with a mismatch, the user stays
            // Pending for manual review in the panel.
            if (localOrder == null)
            {
                _logger.LogWarning(
                    "Webhook: no local order for Go28OrderId={OrderId}. Not confirming. Ref={Ref}",
                    order.Id, referenceNumber);
                _audit.Add(user.Id, user.Email ?? string.Empty,
                    "Webhook — Unknown Order, Not Confirmed",
                    $"Go28 OrderId={order.Id} | ExternalId={order.ExternalId} | " +
                    $"Ref={referenceNumber} | Status left Pending for manual review", webhookIp);
                await _context.SaveChangesAsync();
                return Ok();
            }

            if (localOrder.UserId != user.Id || !AmountMatches(order.AmountInEUR, localOrder.AmountEUR))
            {
                _logger.LogWarning(
                    "Webhook amount/owner mismatch. OrderId={OrderId} Expected={Exp} Reported={Rep} Ref={Ref}",
                    order.Id, localOrder.AmountEUR, order.AmountInEUR, referenceNumber);
                _audit.Add(user.Id, user.Email ?? string.Empty,
                    "Payment Amount Mismatch — Webhook",
                    $"Go28 OrderId={order.Id} | Expected={FormatEur(localOrder.AmountEUR)} EUR | " +
                    $"Reported={order.AmountInEUR} | OrderUserId={localOrder.UserId} | " +
                    $"Ref={referenceNumber} | Status left Pending for manual review", webhookIp);
                await _context.SaveChangesAsync();
                return Ok();
            }

            user.PaymentStatus = "Confirmed";
            user.PaymentMethod = $"Crypto:{order.Currency}";
            user.PaidAt        = DateTime.UtcNow;
            user.PaidAmountEUR = localOrder.AmountEUR;
            await _userManager.UpdateAsync(user);

            _audit.Add(user.Id, user.Email ?? string.Empty, "Payment Confirmed — Webhook",
                $"Go28 OrderId={order.Id} | Ref={order.ExternalId} | " +
                $"Currency={order.Currency} | Network={order.Network} | " +
                $"EUR={FormatEur(localOrder.AmountEUR)} | Received={order.ReceivedAmount ?? order.Amount} | " +
                $"Net={order.NetAmount} | Fee={order.FeeAmount} | CompletedAt={order.CompletedAt}", webhookIp);

            await _context.SaveChangesAsync();

            _logger.LogInformation("Payment confirmed via webhook. User={Email} Ref={Ref} OrderId={OrderId}",
                user.Email, user.ReferenceNumber, order.Id);

            // Sent after the `if (user.PaymentStatus == "Confirmed")` guard
            // above: Go28 can deliver the same webhook more than once.
            await _mail.SendPaymentConfirmedAsync(
                toEmail:   user.Email ?? string.Empty,
                firstName: user.FirstName ?? string.Empty,
                amount:    $"{FormatEur(localOrder.AmountEUR)} EUR",
                method:    ConferenceApp.Services.Email.MailContext.PaymentMethodName($"Crypto:{order.Currency}"),
                reference: user.ReferenceNumber ?? "—",
                culture:   ConferenceApp.Services.Email.MailContext.CultureFor(user),
                baseUrl:   ConferenceApp.Services.Email.MailContext.BaseUrl(_config));

            return Ok();
        }

        // ════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Confirms the payment for an order Go28 has declared "Confirmed":
        /// checks the amount, raises the user's status, writes the audit row and
        /// sends the mail.
        /// </summary>
        /// <remarks>
        /// [P-11] One path for the two entry points that learn about the payment
        /// from a signed-in browser: the polling (<c>check-status</c>, every 15
        /// seconds) and the page load (<c>active-order</c>). The second one only
        /// displayed "Confirmed" for a long time, without touching the user.
        /// The webhook does not come through here — it has no signed-in user,
        /// finds its own by ReferenceNumber, and writes its own set of audit
        /// rows.
        /// </remarks>
        /// <returns>
        /// <c>true</c> if the user was confirmed by this call. <c>false</c> if
        /// they were already confirmed, or if the amount does not match.
        /// </returns>
        private async Task<bool> ConfirmCryptoPaymentAsync(
            ApplicationUser user,
            CryptoOrder     localOrder,
            Go28Order       order,
            string          clientIp,
            string          source)
        {
            // The two entry points can overlap, and the polling runs every 15
            // seconds — without this guard the user would get a mail on every
            // check.
            if (user.PaymentStatus == "Confirmed") return false;

            // [P-06] The amount Go28 reports against the amount the order was
            // created for. A mismatch means the answer does not describe our
            // order: nothing is confirmed and it is left for manual review.
            if (!AmountMatches(order.AmountInEUR, localOrder.AmountEUR))
            {
                _logger.LogWarning(
                    "Crypto amount mismatch ({Source}). OrderId={OrderId} Expected={Exp} Reported={Rep} User={Email}",
                    source, order.Id, localOrder.AmountEUR, order.AmountInEUR, user.Email);

                await WriteAuditAsync(user, $"Payment Amount Mismatch — {source}",
                    $"Go28 OrderId={order.Id} | Expected={FormatEur(localOrder.AmountEUR)} EUR | " +
                    $"Reported={order.AmountInEUR} | Status left Pending for manual review", clientIp);

                return false;
            }

            user.PaymentStatus = "Confirmed";
            user.PaymentMethod = $"Crypto:{order.Currency}";
            user.PaidAt        = DateTime.UtcNow;
            user.PaidAmountEUR = localOrder.AmountEUR;

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                _logger.LogError(
                    "Crypto confirmation could not be saved ({Source}). OrderId={OrderId} User={Email} Errors={Errors}",
                    source, order.Id, user.Email,
                    string.Join("; ", updateResult.Errors.Select(e => $"{e.Code}: {e.Description}")));

                await WriteAuditAsync(user, $"Payment Confirmation Failed — {source}",
                    $"Go28 OrderId={order.Id} | UpdateAsync failed | " +
                    string.Join("; ", updateResult.Errors.Select(e => e.Description)), clientIp);

                return false;
            }

            await WriteAuditAsync(user, $"Payment Confirmed — {source}",
                $"Go28 OrderId={order.Id} | Currency={order.Currency} | Network={order.Network} | " +
                $"EUR={FormatEur(localOrder.AmountEUR)} | Received={order.ReceivedAmount ?? order.Amount} | " +
                $"Fee={order.FeeAmount} | CompletedAt={order.CompletedAt}",
                clientIp);

            await _mail.SendPaymentConfirmedAsync(
                toEmail:   user.Email ?? string.Empty,
                firstName: user.FirstName ?? string.Empty,
                amount:    $"{FormatEur(localOrder.AmountEUR)} EUR",
                method:    ConferenceApp.Services.Email.MailContext.PaymentMethodName($"Crypto:{order.Currency}"),
                reference: user.ReferenceNumber ?? "—",
                culture:   ConferenceApp.Services.Email.MailContext.CultureFor(user),
                baseUrl:   ConferenceApp.Services.Email.MailContext.BaseUrl(_config));

            return true;
        }

        /// <summary>
        /// The amount Go28 reports against the amount the order was created
        /// for. Both are in euro and both are fixed at creation time, so the
        /// comparison is exact — the tolerance on the crypto side
        /// (deviationPercent) is Go28's concern and does not enter here.
        /// </summary>
        private static bool AmountMatches(string? reported, decimal expected)
        {
            if (string.IsNullOrWhiteSpace(reported)) return false;
            return decimal.TryParse(reported,
                       System.Globalization.NumberStyles.Any,
                       System.Globalization.CultureInfo.InvariantCulture,
                       out var parsed)
                   && parsed == expected;
        }

        private static string FormatEur(decimal amount) =>
            amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

        private static string ExtractReferenceNumber(string externalId)
        {
            if (string.IsNullOrWhiteSpace(externalId)) return string.Empty;
            var parts = externalId.Split('-');
            return parts.Length >= 2 ? $"{parts[0]}-{parts[1]}" : externalId;
        }

        // Kept as a convenience for the thirteen call sites in this file — it
        // saves repeating user.Id and user.Email — but the write itself goes
        // through AuditService and no longer builds an AuditLog by hand.
        private Task WriteAuditAsync(ApplicationUser user, string action, string details, string ip) =>
            _audit.LogAsync(user.Id, user.Email ?? string.Empty, action, details, ip);
    }
}