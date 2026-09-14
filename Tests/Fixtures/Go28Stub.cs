// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using System.Text.Json;

namespace ConferenceApp.Tests.Fixtures;

/// <summary>
/// The Go28 crypto gateway, but local. The real one has a live API token and
/// creating an order there is a real external record, so the tests talk to this
/// one instead.
/// <para>
/// The stub is deliberately steerable: a test changes the status of an order,
/// the amount the gateway reports, and whether the service is reachable at all,
/// because those are precisely the cases phase 2 changed.
/// </para>
/// </summary>
public sealed class Go28Stub : StubServer
{
    private int _nextId = 5000;

    public sealed class StubOrder
    {
        public int    Id             { get; set; }
        public string ExternalId     { get; set; } = string.Empty;
        public string Status         { get; set; } = "InProcess";
        public string AmountInEUR    { get; set; } = "0.00";
        public string Amount         { get; set; } = "0.001";
        public string NetAmount      { get; set; } = "0.00095";
        public string FeeAmount      { get; set; } = "0.00005";
        public string? ReceivedAmount { get; set; }
        public string Currency       { get; set; } = string.Empty;
        public string Network        { get; set; } = string.Empty;
        public string CryptoAddress  { get; set; } = "tb1qteststubaddress000000000000000000";
        public string? QrCode        { get; set; } = "PHN2Zy8+";       // base64 „<svg/>"
        public string? CompletedAt   { get; set; }
        public string? ExpiresAt     { get; set; }
    }

    public Dictionary<int, StubOrder> Orders { get; } = new();

    /// <summary>The list of currencies is empty or unreachable; the application should answer 503.</summary>
    public bool CurrenciesUnavailable { get; set; }

    /// <summary>Creating an order fails; the application should answer 502.</summary>
    public bool CreateOrderFails { get; set; }

    /// <summary>A GET of an order finds nothing, which is what a webhook with a forged body sees.</summary>
    public bool HideOrders { get; set; }

    public int ExpirationMinutes { get; set; } = 60;
    public int MaxActiveOrders   { get; set; } = 2;

    public void Reset()
    {
        Orders.Clear();
        CurrenciesUnavailable = false;
        CreateOrderFails      = false;
        HideOrders            = false;
        lock (RequestLog) RequestLog.Clear();
    }

    protected override async Task HandleAsync(HttpListenerContext ctx, string method, string path)
    {
        // The application points at <base>/api/v1/, so the paths arrive with that prefix.
        var route = path.Replace("/api/v1", string.Empty, StringComparison.Ordinal).Trim('/');

        if (method == "GET" && route == "gateway/currencies")
        {
            if (CurrenciesUnavailable)
            {
                await WriteJsonAsync(ctx, "{\"message\":\"service unavailable\"}", 503);
                return;
            }

            await WriteJsonAsync(ctx, JsonSerializer.Serialize(new[]
            {
                Currency("BTC",  "BTC"),
                Currency("ETH",  "ETH"),
                Currency("EURC", "ETH"),
                Currency("USDC", "ETH")
            }));
            return;
        }

        if (method == "POST" && route == "gateway/orders")
        {
            if (CreateOrderFails)
            {
                await WriteJsonAsync(ctx, "{\"message\":\"gateway error\"}", 500);
                return;
            }

            var body = await ReadBodyAsync(ctx);

            var order = new StubOrder
            {
                Id          = ++_nextId,
                ExternalId  = MultipartField(body, "externalId"),
                AmountInEUR = MultipartField(body, "amountInEUR"),
                Currency    = MultipartField(body, "currency"),
                Network     = MultipartField(body, "network"),
                Status      = "InProcess",
                ExpiresAt   = DateTime.UtcNow.AddMinutes(ExpirationMinutes).ToString("yyyy-MM-dd HH:mm:ss")
            };

            lock (Orders) Orders[order.Id] = order;
            await WriteJsonAsync(ctx, Serialize(order));
            return;
        }

        if (method == "GET" && route.StartsWith("gateway/orders/", StringComparison.Ordinal))
        {
            var idPart = route["gateway/orders/".Length..];

            if (HideOrders || !int.TryParse(idPart, out var id))
            {
                await WriteJsonAsync(ctx, "{\"message\":\"not found\"}", 404);
                return;
            }

            StubOrder? order;
            lock (Orders) Orders.TryGetValue(id, out order);

            if (order == null)
            {
                await WriteJsonAsync(ctx, "{\"message\":\"not found\"}", 404);
                return;
            }

            await WriteJsonAsync(ctx, Serialize(order));
            return;
        }

        await WriteJsonAsync(ctx, "{\"message\":\"unknown stub route\"}", 404);
    }

    /// <summary>Declares the order paid, which is what the application learns from a webhook or from polling.</summary>
    public void MarkConfirmed(int orderId, string? reportedAmountEUR = null)
    {
        lock (Orders)
        {
            var order = Orders[orderId];
            order.Status         = "Confirmed";
            order.CompletedAt    = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            order.ReceivedAmount = order.Amount;
            if (reportedAmountEUR != null) order.AmountInEUR = reportedAmountEUR;
        }
    }

    public void MarkStatus(int orderId, string status)
    {
        lock (Orders) Orders[orderId].Status = status;
    }

    public StubOrder Order(int orderId)
    {
        lock (Orders) return Orders[orderId];
    }

    public int LastOrderId
    {
        get { lock (Orders) return Orders.Keys.Max(); }
    }

    private object Currency(string iso, string network) => new
    {
        iso,
        network,
        maxActiveOrders = MaxActiveOrders,
        minAmountInEUR  = "5.00",
        expirationTimeInMinutes = ExpirationMinutes,
        deviationPercent = "1.5"
    };

    private static string Serialize(StubOrder o) => JsonSerializer.Serialize(new
    {
        id = o.Id,
        externalId = o.ExternalId,
        type = "payment",
        status = o.Status,
        amountInEUR = o.AmountInEUR,
        amount = o.Amount,
        netAmount = o.NetAmount,
        feeAmount = o.FeeAmount,
        receivedAmount = o.ReceivedAmount,
        currency = o.Currency,
        network = o.Network,
        cryptoAddress = o.CryptoAddress,
        cryptoAddressQrCode = o.QrCode,
        createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
        completedAt = o.CompletedAt,
        expiresAt = o.ExpiresAt
    });

    /// <summary>
    /// Go28Service sends multipart/form-data. The stub needs no full parser; it
    /// is enough to find the four fields by name.
    /// </summary>
    private static string MultipartField(string body, string name)
    {
        // .NET's MultipartFormDataContent writes name=externalId, WITHOUT quotes,
        // so both forms are searched for rather than only the one in the spec.
        var at = body.IndexOf($"name=\"{name}\"", StringComparison.Ordinal);
        if (at < 0) at = body.IndexOf($"name={name}", StringComparison.Ordinal);
        if (at < 0) return string.Empty;

        var blankLine = body.IndexOf("\r\n\r\n", at, StringComparison.Ordinal);
        if (blankLine < 0) return string.Empty;

        var end = body.IndexOf("\r\n", blankLine + 4, StringComparison.Ordinal);
        if (end < 0) end = body.Length;

        return body[(blankLine + 4)..end].Trim();
    }
}
