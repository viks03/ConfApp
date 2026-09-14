// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using System.Text.Json;
using System.Web;

namespace ConferenceApp.Tests.Fixtures;

/// <summary>
/// The Stripe API, but local. The keys configured in the project are LIVE
/// (<c>sk_live_</c>), so a test card would be declined and every session would
/// be a real record in the live account. Hence:
/// <list type="bullet">
///   <item>the application starts with a fake <c>sk_test_</c> key;</item>
///   <item><c>StripeConfiguration.ApiBase</c> points here, in the same process;</item>
///   <item>the test signs the webhooks with a test <c>WebhookSecret</c>.</item>
/// </list>
/// This covers the Checkout Session from end to end, including a
/// <c>session_id</c> belonging to someone else. It does NOT cover Stripe's own
/// hosted page; in its place the stub serves a form of its own with a field for
/// the card number.
/// </summary>
public sealed class StripeStub : StubServer
{
    private int _next = 1;

    public sealed class StubSession
    {
        public string Id                 { get; set; } = string.Empty;
        public string? ClientReferenceId { get; set; }
        public string? CustomerEmail     { get; set; }
        public long   AmountTotal        { get; set; }
        public string Currency           { get; set; } = "eur";
        public string PaymentStatus      { get; set; } = "unpaid";
        public string Status             { get; set; } = "open";
        public string SuccessUrl         { get; set; } = string.Empty;
        public string CancelUrl          { get; set; } = string.Empty;
    }

    public Dictionary<string, StubSession> Sessions { get; } = new();

    /// <summary>The card the stub accepts. Everything else is declined.</summary>
    public string AcceptedCard { get; set; } = "4242424242424242";

    public void Reset()
    {
        lock (Sessions) Sessions.Clear();
        lock (RequestLog) RequestLog.Clear();
    }

    public StubSession Session(string id)
    {
        lock (Sessions) return Sessions[id];
    }

    /// <summary>
    /// A session created outside the browser, for the case of a
    /// <c>session_id</c> that belongs to a different user.
    /// </summary>
    public StubSession CreateSessionFor(string? clientReferenceId, long amountCents,
                                        string paymentStatus = "paid", string? email = null)
    {
        var session = new StubSession
        {
            Id                = $"cs_test_stub{_next++:0000}",
            ClientReferenceId = clientReferenceId,
            CustomerEmail     = email,
            AmountTotal       = amountCents,
            PaymentStatus     = paymentStatus,
            Status            = paymentStatus == "paid" ? "complete" : "open"
        };

        lock (Sessions) Sessions[session.Id] = session;
        return session;
    }

    protected override async Task HandleAsync(HttpListenerContext ctx, string method, string path)
    {
        var route = path.Trim('/');

        // ── The Stripe API: creating a Checkout Session ──────────────────
        if (method == "POST" && route == "v1/checkout/sessions")
        {
            var form = HttpUtility.ParseQueryString(await ReadBodyAsync(ctx));

            var session = new StubSession
            {
                Id                = $"cs_test_stub{_next++:0000}",
                ClientReferenceId = form["client_reference_id"],
                CustomerEmail     = form["customer_email"],
                AmountTotal       = long.TryParse(form["line_items[0][price_data][unit_amount]"], out var amt) ? amt : 0,
                Currency          = form["line_items[0][price_data][currency]"] ?? "eur",
                SuccessUrl        = form["success_url"] ?? string.Empty,
                CancelUrl         = form["cancel_url"]  ?? string.Empty,
                PaymentStatus     = "unpaid",
                Status            = "open"
            };

            lock (Sessions) Sessions[session.Id] = session;
            await WriteJsonAsync(ctx, Serialize(session, $"{BaseUrl}/hosted/{session.Id}"));
            return;
        }

        // ── The Stripe API: reading a Checkout Session ───────────────────
        if (method == "GET" && route.StartsWith("v1/checkout/sessions/", StringComparison.Ordinal))
        {
            var id = route["v1/checkout/sessions/".Length..];

            StubSession? session;
            lock (Sessions) Sessions.TryGetValue(id, out session);

            if (session == null)
            {
                await WriteJsonAsync(ctx,
                    "{\"error\":{\"type\":\"invalid_request_error\",\"message\":\"No such checkout.session\"}}", 404);
                return;
            }

            await WriteJsonAsync(ctx, Serialize(session, $"{BaseUrl}/hosted/{session.Id}"));
            return;
        }

        // ── The stand-in for the hosted Checkout page ────────────────────
        if (method == "GET" && route.StartsWith("hosted/", StringComparison.Ordinal))
        {
            var id = route["hosted/".Length..];
            await WriteAsync(ctx, "text/html; charset=utf-8", HostedPage(id));
            return;
        }

        if (method == "POST" && route.StartsWith("pay/", StringComparison.Ordinal))
        {
            var id = route["pay/".Length..];
            var form = HttpUtility.ParseQueryString(await ReadBodyAsync(ctx));
            var card = (form["card"] ?? string.Empty).Replace(" ", string.Empty);

            StubSession? session;
            lock (Sessions) Sessions.TryGetValue(id, out session);

            if (session == null)
            {
                ctx.Response.StatusCode = 404;
                await WriteAsync(ctx, "text/plain", "no such session");
                return;
            }

            string target;
            if (card == AcceptedCard)
            {
                session.PaymentStatus = "paid";
                session.Status        = "complete";
                target = session.SuccessUrl.Replace("{CHECKOUT_SESSION_ID}", session.Id);
            }
            else
            {
                target = session.CancelUrl;
            }

            ctx.Response.StatusCode = 303;
            ctx.Response.Headers["Location"] = target;
            return;
        }

        await WriteJsonAsync(ctx, "{\"error\":{\"message\":\"unknown stub route\"}}", 404);
    }

    /// <summary>
    /// The stand-in for the hosted Checkout page: one field for the card number
    /// and a button. The test types 4242 4242 4242 4242 exactly as a person
    /// would, and the stub accepts the payment only for that card; everything
    /// else comes back through cancel_url.
    /// </summary>
    private string HostedPage(string id)
    {
        StubSession? session;
        lock (Sessions) Sessions.TryGetValue(id, out session);

        if (session == null)
            return "<!doctype html><title>Stripe stub</title><p id=\"stub-missing\">no such session</p>";

        var amount = (session.AmountTotal / 100.0m).ToString("F2",
            System.Globalization.CultureInfo.InvariantCulture);

        return $"""
            <!doctype html>
            <title>Stripe Checkout (stub)</title>
            <h1 id="stub-checkout-title">Stripe Checkout — local stub</h1>
            <p>Session <code id="stub-session-id">{session.Id}</code></p>
            <p>Amount <span id="stub-amount">{amount}</span> {session.Currency.ToUpperInvariant()}</p>
            <form id="stub-pay-form" method="post" action="{BaseUrl}/pay/{session.Id}">
              <label for="stub-card">Card number</label>
              <input id="stub-card" name="card" autocomplete="off" />
              <button id="stub-pay" type="submit">Pay</button>
            </form>
            """;
    }

    private static string Serialize(StubSession s, string url) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["id"]                  = s.Id,
        ["object"]              = "checkout.session",
        ["amount_total"]        = s.AmountTotal,
        ["amount_subtotal"]     = s.AmountTotal,
        ["currency"]            = s.Currency,
        ["client_reference_id"] = s.ClientReferenceId,
        ["customer_email"]      = s.CustomerEmail,
        ["payment_status"]      = s.PaymentStatus,
        ["status"]              = s.Status,
        ["mode"]                = "payment",
        ["url"]                 = url,
        ["livemode"]            = false,
        ["created"]             = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
    });
}
