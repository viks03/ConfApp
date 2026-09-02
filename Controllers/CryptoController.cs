using ConferenceApp.Data;
using ConferenceApp.Models;
using ConferenceApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Controllers
{
    // ════════════════════════════════════════════════════════════════
    // DTO — Request / Response модели
    // ════════════════════════════════════════════════════════════════

    public class CreateCryptoOrderRequest
    {
        public string Currency { get; set; } = string.Empty;
        public string Network  { get; set; } = string.Empty;
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
    // CryptoController
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

        public CryptoController(
            Go28Service go28,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            ILogger<CryptoController> logger,
            ConferenceApp.Services.Email.IMailComposer mail,
            IConfiguration config,
            ConferenceApp.Services.IPaymentGateSettings paymentGates)
        {
            _go28        = go28;
            _userManager = userManager;
            _context     = context;
            _logger      = logger;
            _mail        = mail;
            _config      = config;
            _paymentGates = paymentGates;
        }

        // ────────────────────────────────────────────────────────────
        // GET /api/crypto/active-order
        // Зарежда активния order при рефреш без да създава нов.
        // Чете от локалната таблица, вика Go28 само за потвърждение.
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

            // Проверяваме дали е изтекъл локално
            if (localOrder.ExpiresAt.HasValue && localOrder.ExpiresAt.Value < DateTime.UtcNow)
            {
                localOrder.Status = "Expired";
                await _context.SaveChangesAsync();
                return Ok(new CryptoOrderResponse { Success = false, Error = "order_expired" });
            }

            // Проверяваме реалния статус в Go28
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
                AmountInEUR       = localOrder.AmountEUR,
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
        // Логика:
        //   1. Валидира currency/network срещу Go28 currencies.
        //   2. Маркира изтекли локални orders на база ExpiresAt.
        //   3. Проверява локалния лимит ПРЕДИ да вика Go28.
        //   4. Ако вече има активен order за СЪЩАТА валута — връща него.
        //   5. Ако потребителят смени валута — създава нов order.
        //      Старият изтича сам в Go28 след 60 мин.
        //   6. Записва новия order в CryptoOrders таблицата.
        // ────────────────────────────────────────────────────────────
        [HttpPost("create-order")]
        public async Task<IActionResult> CreateOrder([FromBody] CreateCryptoOrderRequest request)
        {
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            if (string.IsNullOrWhiteSpace(request.Currency) || string.IsNullOrWhiteSpace(request.Network))
                return BadRequest(new CryptoOrderResponse { Success = false, Error = "Currency and network are required." });

            // Payment Control (Admin панел) — общ ключ, метод "crypto" или самата валута спрени.
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

            // ── Взимаме цената динамично от TicketTiers (Id=2 = Early Bird Ticket) ─
            // Същата логика като Payment.cshtml.cs — промо цена ако има, иначе редовна.
            var tier = await _context.TicketTiers.FindAsync(2);
            if (tier == null)
                return StatusCode(500, new CryptoOrderResponse { Success = false, Error = "Ticket tier not found." });

            var rawPrice = !string.IsNullOrWhiteSpace(tier.PromoPriceEn)
                ? tier.PromoPriceEn
                : tier.RegularPriceEn;

            var priceDigits = new string(rawPrice.Where(c => char.IsDigit(c) || c == '.').ToArray());
            var conferenceAmountEUR = decimal.TryParse(
                priceDigits,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsedPrice) ? parsedPrice : 120.00m;

            // ── Взимаме поддържаните валути от Go28 ─────────────────
            var currencies = await _go28.GetCurrenciesAsync();
            Go28Currency? supported = null;

            if (currencies.Any())
            {
                supported = currencies.FirstOrDefault(c =>
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

                // ── Маркираме изтекли локални orders ────────────────
                var now = DateTime.UtcNow;
                var expiredLocally = await _context.CryptoOrders
                    .Where(o => o.UserId == user.Id
                             && o.Status == "InProcess"
                             && o.ExpiresAt.HasValue
                             && o.ExpiresAt.Value < now)
                    .ToListAsync();

                foreach (var exp in expiredLocally) exp.Status = "Expired";
                if (expiredLocally.Any()) await _context.SaveChangesAsync();

                // ── Проверяваме лимита ПРЕДИ да викаме Go28 ─────────
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
            }

            // ── Ако вече има активен order за СЪЩАТА валута — връщаме го ─
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
                    AmountInEUR       = existingOrder.AmountEUR,
                    Currency          = existingOrder.Currency,
                    Network           = existingOrder.Network,
                    ExpiresAt         = existingOrder.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                    Status            = existingOrder.Status,
                    DeviationPercent  = supported?.DeviationPercent,
                    ExpirationMinutes = supported?.ExpirationTimeInMinutes
                });
            }

            // ── Създаваме нов order в Go28 ───────────────────────────
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

            // ── Парсваме ExpiresAt ───────────────────────────────────
            DateTime? expiresAtUtc = null;
            if (!string.IsNullOrEmpty(order.ExpiresAt))
            {
                if (DateTime.TryParse(order.ExpiresAt.Replace(" ", "T") + "Z",
                    null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                    expiresAtUtc = parsed;
            }

            // ── Записваме в CryptoOrders ─────────────────────────────
            var cryptoOrder = new CryptoOrder
            {
                UserId        = user.Id,
                Go28OrderId   = order.Id,
                ExternalId    = externalId,
                Currency      = order.Currency,
                Network       = order.Network,
                AmountEUR     = order.AmountInEUR,
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

            // Backward compatibility — PaymentMethod
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

            // Сигурностна проверка — само свои orders
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

            // Обновяваме локалния запис
            if (localOrder.Status != order.Status)
            {
                localOrder.Status = order.Status;
                if (isConfirmed) localOrder.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            if (isConfirmed && user.PaymentStatus != "Confirmed")
            {
                user.PaymentStatus = "Confirmed";
                user.PaymentMethod = $"Crypto:{order.Currency}";
                user.PaidAt        = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);

                await WriteAuditAsync(user, "Payment Confirmed — Polling",
                    $"Go28 OrderId={orderId} | Currency={order.Currency} | Network={order.Network} | " +
                    $"EUR={order.AmountInEUR} | Received={order.ReceivedAmount ?? order.Amount} | " +
                    $"Fee={order.FeeAmount} | CompletedAt={order.CompletedAt}",
                    clientIp);

                // Вътре в `isConfirmed && user.PaymentStatus != "Confirmed"`.
                // Този метод се вика периодично от браузъра, докато плащането
                // виси — извън този блок потребителят щеше да получава писмо
                // при всяка проверка. Webhook-ът долу има собствен guard.
                await _mail.SendPaymentConfirmedAsync(
                    toEmail:   user.Email ?? string.Empty,
                    firstName: user.FirstName ?? string.Empty,
                    amount:    $"{order.AmountInEUR:F2} EUR",
                    method:    ConferenceApp.Services.Email.MailContext.PaymentMethodName($"Crypto:{order.Currency}"),
                    reference: user.ReferenceNumber ?? "—",
                    culture:   ConferenceApp.Services.Email.MailContext.CultureFor(user),
                    baseUrl:   ConferenceApp.Services.Email.MailContext.BaseUrl(_config));
            }

            if (isExpired)
                await WriteAuditAsync(user, "Crypto Order Expired",
                    $"Go28 OrderId={orderId} | Currency={order.Currency} | ExpiresAt={order.ExpiresAt}", clientIp);

            if (isCancelled)
                await WriteAuditAsync(user, "Crypto Order Cancelled",
                    $"Go28 OrderId={orderId} | Currency={order.Currency} | Network={order.Network}", clientIp);

            return Ok(new CheckOrderStatusResponse
            {
                Success   = true,
                Status    = order.Status,
                IsPaid    = isConfirmed,
                IsExpired = isExpired || isCancelled
            });
        }

        // ────────────────────────────────────────────────────────────
        // POST /api/crypto/webhook
        // ────────────────────────────────────────────────────────────
        [HttpPost("webhook")]
        [AllowAnonymous]
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

            if (payload == null || string.IsNullOrWhiteSpace(payload.ExternalId))
            {
                _logger.LogWarning("Webhook: null or missing payload. ContentType={CT}", contentType);
                return BadRequest();
            }

            var webhookIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            _logger.LogInformation("Webhook received. ExternalId={ExternalId} OrderId={OrderId} Status={Status}",
                payload.ExternalId, payload.Id, payload.Status);

            _context.Set<AuditLog>().Add(new AuditLog
            {
                UserId    = null,
                UserEmail = payload.ExternalId,
                Action    = $"Webhook Received — {payload.Status}",
                IpAddress = webhookIp,
                Details   = $"Go28 OrderId={payload.Id} | ExternalId={payload.ExternalId} | " +
                            $"Status={payload.Status} | Currency={payload.Currency} | " +
                            $"Network={payload.Network} | EUR={payload.AmountInEUR} | " +
                            $"GrossAmount={payload.Amount} | NetAmount={payload.NetAmount} | " +
                            $"Fee={payload.FeeAmount} | Received={payload.ReceivedAmount ?? "—"} | " +
                            $"CompletedAt={payload.CompletedAt ?? "—"}",
                Timestamp = DateTime.UtcNow
            });

            // Обновяваме локалния CryptoOrder
            var localOrder = await _context.CryptoOrders
                .FirstOrDefaultAsync(o => o.Go28OrderId == payload.Id);

            if (localOrder != null && localOrder.Status != payload.Status)
            {
                localOrder.Status = payload.Status;
                if (payload.Status == "Confirmed") localOrder.CompletedAt = DateTime.UtcNow;
            }

            if (!string.Equals(payload.Status, "Confirmed", StringComparison.OrdinalIgnoreCase))
            {
                await _context.SaveChangesAsync();
                return Ok();
            }

            var referenceNumber = ExtractReferenceNumber(payload.ExternalId);
            var user = await _userManager.Users
                .FirstOrDefaultAsync(u => u.ReferenceNumber == referenceNumber);

            if (user == null)
            {
                _logger.LogWarning("Webhook: No user with ReferenceNumber={Ref}.", referenceNumber);
                _context.Set<AuditLog>().Add(new AuditLog
                {
                    UserId    = null,
                    UserEmail = payload.ExternalId,
                    Action    = "Webhook — User Not Found",
                    IpAddress = webhookIp,
                    Details   = $"Go28 OrderId={payload.Id} | ExternalId={payload.ExternalId} | ExtractedRef={referenceNumber}",
                    Timestamp = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
                return Ok();
            }

            if (user.PaymentStatus == "Confirmed")
            {
                await _context.SaveChangesAsync();
                return Ok();
            }

            user.PaymentStatus = "Confirmed";
            user.PaymentMethod = $"Crypto:{payload.Currency}";
            user.PaidAt        = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);

            _context.Set<AuditLog>().Add(new AuditLog
            {
                UserId    = user.Id,
                UserEmail = user.Email ?? string.Empty,
                Action    = "Payment Confirmed — Webhook",
                IpAddress = webhookIp,
                Details   = $"Go28 OrderId={payload.Id} | Ref={payload.ExternalId} | " +
                            $"Currency={payload.Currency} | Network={payload.Network} | " +
                            $"EUR={payload.AmountInEUR} | Received={payload.ReceivedAmount ?? payload.Amount} | " +
                            $"Net={payload.NetAmount} | Fee={payload.FeeAmount} | CompletedAt={payload.CompletedAt}",
                Timestamp = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            _logger.LogInformation("Payment confirmed via webhook. User={Email} Ref={Ref} OrderId={OrderId}",
                user.Email, user.ReferenceNumber, payload.Id);

            // След guard-а `if (user.PaymentStatus == "Confirmed") return Ok();`
            // по-горе. Go28 може да достави webhook-а повторно.
            await _mail.SendPaymentConfirmedAsync(
                toEmail:   user.Email ?? string.Empty,
                firstName: user.FirstName ?? string.Empty,
                amount:    $"{payload.AmountInEUR:F2} EUR",
                method:    ConferenceApp.Services.Email.MailContext.PaymentMethodName($"Crypto:{payload.Currency}"),
                reference: user.ReferenceNumber ?? "—",
                culture:   ConferenceApp.Services.Email.MailContext.CultureFor(user),
                baseUrl:   ConferenceApp.Services.Email.MailContext.BaseUrl(_config));

            return Ok();
        }

        // ════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════

        private static string ExtractReferenceNumber(string externalId)
        {
            if (string.IsNullOrWhiteSpace(externalId)) return string.Empty;
            var parts = externalId.Split('-');
            return parts.Length >= 2 ? $"{parts[0]}-{parts[1]}" : externalId;
        }

        private async Task WriteAuditAsync(ApplicationUser user, string action, string details, string ip)
        {
            _context.Set<AuditLog>().Add(new AuditLog
            {
                UserId    = user.Id,
                UserEmail = user.Email ?? string.Empty,
                Action    = action,
                IpAddress = ip,
                Details   = details,
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }
    }
}