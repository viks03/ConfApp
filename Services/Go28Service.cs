// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConferenceApp.Services
{
    // ════════════════════════════════════════════════════════════════
    // Go28 API response models. The gateway sends every number as a string,
    // which is why the amount fields below are strings and not decimals.
    // ════════════════════════════════════════════════════════════════

    public class Go28Currency
    {
        [JsonPropertyName("iso")]
        public string Iso { get; set; } = string.Empty;

        [JsonPropertyName("network")]
        public string Network { get; set; } = string.Empty;

        [JsonPropertyName("maxActiveOrders")]
        public int MaxActiveOrders { get; set; }

        [JsonPropertyName("minAmountInEUR")]
        public string MinAmountInEUR { get; set; } = string.Empty;

        [JsonPropertyName("expirationTimeInMinutes")]
        public int ExpirationTimeInMinutes { get; set; }

        [JsonPropertyName("deviationPercent")]
        public string DeviationPercent { get; set; } = string.Empty;
    }

    public class Go28Order
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("externalId")]
        public string ExternalId { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        // One of: InProcess | Confirmed | Expired | Cancelled. The same values
        // are stored in CryptoOrder.Status.
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("amountInEUR")]
        public string AmountInEUR { get; set; } = string.Empty;

        // The gross amount in crypto — this is the figure shown to the user.
        [JsonPropertyName("amount")]
        public string Amount { get; set; } = string.Empty;

        // The net amount, after the gateway fee.
        [JsonPropertyName("netAmount")]
        public string NetAmount { get; set; } = string.Empty;

        // The gateway fee itself.
        [JsonPropertyName("feeAmount")]
        public string FeeAmount { get; set; } = string.Empty;

        // What actually arrived. Empty until the payment lands, and it can
        // differ from Amount within the currency's deviationPercent.
        [JsonPropertyName("receivedAmount")]
        public string? ReceivedAmount { get; set; }

        [JsonPropertyName("currency")]
        public string Currency { get; set; } = string.Empty;

        [JsonPropertyName("network")]
        public string Network { get; set; } = string.Empty;

        [JsonPropertyName("cryptoAddress")]
        public string CryptoAddress { get; set; } = string.Empty;

        // A base64 SVG from the gateway, used directly as an <img src="…">.
        [JsonPropertyName("cryptoAddressQrCode")]
        public string? CryptoAddressQrCode { get; set; }

        [JsonPropertyName("createdAt")]
        public string? CreatedAt { get; set; }

        [JsonPropertyName("completedAt")]
        public string? CompletedAt { get; set; }

        [JsonPropertyName("expiresAt")]
        public string? ExpiresAt { get; set; }
    }

    // ════════════════════════════════════════════════════════════════
    // Go28Service
    // ════════════════════════════════════════════════════════════════

    public class Go28Service
    {
        private readonly HttpClient             _http;
        private readonly ILogger<Go28Service>   _logger;

        // Configuration comes from appsettings.json / user secrets:
        // "Go28": { "BaseUrl": "https://crm.go28.io/api/v1", "ApiToken": "…" }
        // The token is a secret and is never in appsettings.json — see README,
        // "Configuration & secrets".
        public Go28Service(HttpClient http, IConfiguration config, ILogger<Go28Service> logger)
        {
            _http   = http;
            _logger = logger;

            var baseUrl  = config["Go28:BaseUrl"]  ?? throw new InvalidOperationException("Go28:BaseUrl is not configured.");
            var apiToken = config["Go28:ApiToken"] ?? throw new InvalidOperationException("Go28:ApiToken is not configured.");

            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("Go28:BaseUrl is empty. Set it via User Secrets or environment variables.");
            if (string.IsNullOrWhiteSpace(apiToken))
                throw new InvalidOperationException("Go28:ApiToken is empty. Set it via User Secrets or environment variables.");

            _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            _http.DefaultRequestHeaders.Add("x-api-token", apiToken);
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        // ────────────────────────────────────────────────────────────
        // GET /gateway/currencies
        // The currencies the gateway accepts, with their limits. Read when the
        // Payment page loads and again before an order is created.
        //
        // Every failure returns an empty list rather than throwing: the Payment
        // page has to render even with the gateway down, just without the crypto
        // option.
        // ────────────────────────────────────────────────────────────
        public async Task<List<Go28Currency>> GetCurrenciesAsync()
        {
            try
            {
                var response = await _http.GetAsync("gateway/currencies");

                LogRateLimit(response);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogError(
                        "Go28 GetCurrencies failed. Status={Status} Body={Body}",
                        (int)response.StatusCode, errorBody);
                    return new List<Go28Currency>();
                }

                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<Go28Currency>>(json)
                    ?? new List<Go28Currency>();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Go28 GetCurrencies — HTTP error. Is the API reachable?");
                return new List<Go28Currency>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Go28 GetCurrencies — unexpected error.");
                return new List<Go28Currency>();
            }
        }

        // ────────────────────────────────────────────────────────────
        // POST /gateway/orders
        // Creates a new order. The gateway expects multipart/form-data, not
        // JSON.
        //
        // externalId  = the user's ReferenceNumber (BCE2026-XXXXX). It is what
        //               the webhook sends back, and the only link between an
        //               order and a participant.
        // amountInEUR = the amount in euro, formatted "120.00"
        // currency    = "BTC" | "ETH" | "EURC" | "USDC"
        // network     = "BTC" | "ETH" (the network code, not its full name)
        // ────────────────────────────────────────────────────────────
        public async Task<Go28Order?> CreateOrderAsync(
            string  externalId,
            decimal amountInEUR,
            string  currency,
            string  network)
        {
            try
            {
                using var form = new MultipartFormDataContent
                {
                    { new StringContent(externalId),                 "externalId"  },
                    { new StringContent(amountInEUR.ToString("F2",
                        System.Globalization.CultureInfo.InvariantCulture)), "amountInEUR" },
                    { new StringContent(currency),                   "currency"    },
                    { new StringContent(network),                    "network"     }
                };

                var response = await _http.PostAsync("gateway/orders", form);

                LogRateLimit(response);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogError(
                        "Go28 CreateOrder failed. Status={Status} ExternalId={ExternalId} " +
                        "Currency={Currency} Network={Network} Body={Body}",
                        (int)response.StatusCode, externalId, currency, network, errorBody);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var order = JsonSerializer.Deserialize<Go28Order>(json);

                if (order != null)
                {
                    _logger.LogInformation(
                        "Go28 order created successfully. OrderId={OrderId} ExternalId={ExternalId} " +
                        "Currency={Currency} Network={Network} Amount={Amount} ExpiresAt={ExpiresAt}",
                        order.Id, externalId, currency, network, order.Amount, order.ExpiresAt);
                }

                return order;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex,
                    "Go28 CreateOrder — HTTP error. ExternalId={ExternalId} Currency={Currency} Network={Network}",
                    externalId, currency, network);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Go28 CreateOrder — unexpected error. ExternalId={ExternalId} Currency={Currency}",
                    externalId, currency);
                return null;
            }
        }

        // ────────────────────────────────────────────────────────────
        // GET /gateway/orders/{id}
        // The status of an existing order. Used both by the page polling every
        // 15 seconds and by the webhook handler, which re-reads the order rather
        // than trusting the body it was sent.
        // ────────────────────────────────────────────────────────────
        public async Task<Go28Order?> GetOrderAsync(int orderId)
        {
            try
            {
                var response = await _http.GetAsync($"gateway/orders/{orderId}");

                LogRateLimit(response);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogError(
                        "Go28 GetOrder failed. OrderId={OrderId} Status={Status} Body={Body}",
                        orderId, (int)response.StatusCode, errorBody);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<Go28Order>(json);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Go28 GetOrder — HTTP error. OrderId={OrderId}", orderId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Go28 GetOrder — unexpected error. OrderId={OrderId}", orderId);
                return null;
            }
        }

        // ────────────────────────────────────────────────────────────
        // Logs the X-RateLimit headers the gateway returns. The quota is 120
        // requests; fewer than 20 left is logged as a warning, so that a polling
        // loop gone wrong is visible before the gateway starts refusing.
        // ────────────────────────────────────────────────────────────
        private void LogRateLimit(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("X-RateLimit-Limit", out var limitVals) &&
                response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingVals))
            {
                var limitStr     = System.Linq.Enumerable.FirstOrDefault(limitVals);
                var remainingStr = System.Linq.Enumerable.FirstOrDefault(remainingVals);

                if (int.TryParse(limitStr,     out var limit) &&
                    int.TryParse(remainingStr, out var remaining))
                {
                    if (remaining < 20)
                    {
                        _logger.LogWarning(
                            "Go28 rate limit low! Remaining={Remaining}/{Limit}",
                            remaining, limit);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Go28 rate limit. Remaining={Remaining}/{Limit}",
                            remaining, limit);
                    }
                }
            }
        }
    }
}