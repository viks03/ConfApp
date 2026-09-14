// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using ConferenceApp.Tests.Fixtures;
using Microsoft.Playwright;

namespace ConferenceApp.Tests.Payments;

/// <summary>
/// Part 2, paying by card, from opening the form to the row in the database.
/// <para>
/// Stripe is a local stub (see <see cref="StripeStub"/>): the keys configured in
/// the project are live and a test card does not work against them. Stripe's own
/// hosted page is therefore NOT covered; everything else along the path is.
/// </para>
/// </summary>
[Collection(AppCollection.Name)]
public class CardPaymentTests : IAsyncLifetime
{
    private readonly AppFixture _app;
    private readonly List<string> _created = new();

    public CardPaymentTests(AppFixture app) => _app = app;

    public Task InitializeAsync()
    {
        _app.Stripe.Reset();
        _app.Smtp.Clear();
        return Task.CompletedTask;
    }

    /// <summary>Every test cleans up after itself, so that the order they run in means nothing.</summary>
    public async Task DisposeAsync()
    {
        foreach (var email in _created)
            await _app.Db.DeleteParticipantAsync(email);
    }

    private async Task<ConferenceApp.Models.ApplicationUser> NewParticipantAsync(
        string partForm = "1", string paymentStatus = "Pending")
    {
        var email = $"card-{Guid.NewGuid():N}@example.test";
        _created.Add(email);
        return await _app.Db.CreateParticipantAsync(email, partForm, paymentStatus);
    }

    // ════════════════════════════════════════════════════════════════════
    // The form and the amount
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Страницата_за_плащане_се_отваря_за_своята_форма()
    {
        var user = await NewParticipantAsync();

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var response = await session.Client.GetAsync("/Payment/earlybird");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"btn-pay-card\"", html);
        Assert.Contains(user.ReferenceNumber, html);
    }

    [Fact]
    public async Task Сумата_на_екрана_съвпада_с_тарифата_в_базата()
    {
        var user = await NewParticipantAsync();
        var tier = await _app.Db.TierAsync("earlybird");
        Assert.NotNull(tier);

        var expected = tier!.PromoPriceEUR ?? tier.RegularPriceEUR;
        Assert.NotNull(expected);

        await using var context = await _app.NewLoggedInContextAsync(user.Email!);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/Payment/earlybird");

        // The figure next to the button comes from TierPriceEUR, the numeric column.
        var payButton = await page.Locator("#btn-pay-card").InnerTextAsync();
        Assert.Contains(expected!.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                        payButton);

        // The string shown in the summary comes from the text column; the two have
        // to be talking about the same number.
        var summary = await page.Locator("#sum-total").InnerTextAsync();
        Assert.Contains(((int)expected.Value).ToString(), summary);
    }

    // ════════════════════════════════════════════════════════════════════
    // Paying through to confirmation
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Плащане_с_тестова_карта_стига_до_потвърждение()
    {
        var user = await NewParticipantAsync();
        var tier = await _app.Db.TierAsync("earlybird");
        var expectedEur = tier!.PromoPriceEUR ?? tier.RegularPriceEUR;

        var auditFrom = await _app.Db.LastAuditIdAsync();

        await using var context = await _app.NewLoggedInContextAsync(user.Email!);
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Payment/earlybird");
        await page.ClickAsync("#btn-pay-card");

        // The application redirects to Checkout, which here is the stub.
        await page.WaitForSelectorAsync("#stub-pay-form");

        await page.FillAsync("#stub-card", _app.Credentials.TestCard);
        await page.ClickAsync("#stub-pay");

        // And back to /Payment/{slug}?payment=success&session_id=...
        await page.WaitForSelectorAsync("#payment-success-overlay");

        var confirmed = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Confirmed", confirmed!.PaymentStatus);
        Assert.Equal(expectedEur, confirmed.PaidAmountEUR);
        Assert.NotNull(confirmed.PaidAt);

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        var confirmations = audit.Where(a => a.UserEmail == user.Email).ToList();

        Assert.Contains(confirmations, a => a.Action.StartsWith("Payment Confirmed"));
        Assert.DoesNotContain(confirmations, a => a.Action.Contains("Mismatch"));

        var mail = await _app.Smtp.WaitForPaymentMailAsync(user.Email!, TimeSpan.FromSeconds(20));
        Assert.NotNull(mail);
    }

    [Fact]
    public async Task Карта_която_не_минава_не_потвърждава_нищо()
    {
        var user = await NewParticipantAsync();

        await using var context = await _app.NewLoggedInContextAsync(user.Email!);
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Payment/earlybird");
        await page.ClickAsync("#btn-pay-card");
        await page.WaitForSelectorAsync("#stub-pay-form");

        await page.FillAsync("#stub-card", "4000000000000002");   // a declined card
        await page.ClickAsync("#stub-pay");

        await page.WaitForURLAsync(u => u.Contains("cancel=true"));

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Pending", after!.PaymentStatus);
        Assert.Null(after.PaidAmountEUR);
    }

    // ════════════════════════════════════════════════════════════════════
    // Someone else's session_id — [P-02]
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Чужд_session_id_не_потвърждава_нищо()
    {
        var payer   = await NewParticipantAsync();
        var freerider = await NewParticipantAsync();

        // A paid session, but created for a DIFFERENT user.
        var foreign = _app.Stripe.CreateSessionFor(payer.Id, amountCents: 6000, email: payer.Email);

        var auditFrom = await _app.Db.LastAuditIdAsync();

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(freerider.Email!);

        var response = await session.Client.GetAsync(
            $"/Payment/earlybird?payment=success&session_id={foreign.Id}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("id=\"payment-success-overlay\"", html);

        var after = await _app.Db.FindUserAsync(freerider.Email!);
        Assert.Equal("Pending", after!.PaymentStatus);
        Assert.Null(after.PaidAmountEUR);

        // The attempt has to leave a trace rather than pass unnoticed.
        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a =>
            a.UserEmail == freerider.Email &&
            a.Action.Contains("Ownership Mismatch", StringComparison.Ordinal));

        // And the person who actually paid must be left alone.
        var payerAfter = await _app.Db.FindUserAsync(payer.Email!);
        Assert.Equal("Pending", payerAfter!.PaymentStatus);
    }

    [Fact]
    public async Task Непознат_session_id_не_потвърждава_нищо()
    {
        var user = await NewParticipantAsync();

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var response = await session.Client.GetAsync(
            "/Payment/earlybird?payment=success&session_id=cs_test_never_existed");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Pending", after!.PaymentStatus);
    }

    [Fact]
    public async Task Своя_сесия_но_неплатена_не_потвърждава()
    {
        var user = await NewParticipantAsync();
        var unpaid = _app.Stripe.CreateSessionFor(user.Id, 6000, paymentStatus: "unpaid", email: user.Email);

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        await session.Client.GetAsync($"/Payment/earlybird?payment=success&session_id={unpaid.Id}");

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Pending", after!.PaymentStatus);
    }

    // ════════════════════════════════════════════════════════════════════
    // Webhook
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Webhook_с_валиден_подпис_потвърждава_и_записва_сумата()
    {
        var user = await NewParticipantAsync();
        var tier = await _app.Db.TierAsync("earlybird");
        var expected = tier!.PromoPriceEUR ?? tier.RegularPriceEUR;
        var cents = (long)(expected!.Value * 100);

        var auditFrom = await _app.Db.LastAuditIdAsync();

        var response = await SendWebhookAsync(StripeWebhook.CheckoutSessionCompleted(
            sessionId: "cs_test_webhook_ok", clientReferenceId: user.Id,
            amountTotalCents: cents, customerEmail: user.Email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Confirmed", after!.PaymentStatus);
        Assert.Equal(expected, after.PaidAmountEUR);
        // [T-01] The same value as on the browser's return.
        Assert.Equal("Stripe:Card", after.PaymentMethod);

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "Payment Confirmed — Stripe Checkout");
    }

    [Fact]
    public async Task Webhook_с_подправен_подпис_не_потвърждава_нищо()
    {
        var user = await NewParticipantAsync();

        var payload = StripeWebhook.CheckoutSessionCompleted(
            "cs_test_forged", user.Id, 6000, user.Email);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/stripe/webhook")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        // Signed with the WRONG key, which is exactly what an outsider would send.
        request.Headers.Add("Stripe-Signature", StripeWebhook.Sign(payload, "whsec_attacker_guess"));

        using var client = _app.NewClient();
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Pending", after!.PaymentStatus);
    }

    [Fact]
    public async Task Webhook_без_подпис_се_отказва()
    {
        var user = await NewParticipantAsync();

        using var client = _app.NewClient();
        var response = await client.PostAsync("/api/stripe/webhook",
            new StringContent(
                StripeWebhook.CheckoutSessionCompleted("cs_test_nosig", user.Id, 6000),
                System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Pending", after!.PaymentStatus);
    }

    [Fact]
    public async Task Повторен_webhook_за_същото_плащане_не_прави_втора_промяна()
    {
        var user = await NewParticipantAsync();
        var payload = StripeWebhook.CheckoutSessionCompleted(
            "cs_test_replay", user.Id, 6000, user.Email);

        var first = await SendWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var afterFirst = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Confirmed", afterFirst!.PaymentStatus);

        // The mail goes out through a queue (QueuedHostedService), so the first
        // message may arrive after the webhook. It is waited for before the sink
        // is cleared; otherwise the "second" message is just the late first one.
        Assert.NotNull(await _app.Smtp.WaitForPaymentMailAsync(user.Email!, TimeSpan.FromSeconds(20)));

        var auditFrom = await _app.Db.LastAuditIdAsync();
        _app.Smtp.Clear();

        var second = await SendWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var afterSecond = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal(afterFirst.PaidAt, afterSecond!.PaidAt);
        Assert.Equal(afterFirst.PaidAmountEUR, afterSecond.PaidAmountEUR);

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.DoesNotContain(audit, a => a.Action == "Payment Confirmed — Stripe Checkout");

        // The second notification must not reach the participant.
        var mail = await _app.Smtp.WaitForPaymentMailAsync(user.Email!, TimeSpan.FromSeconds(6));
        Assert.Null(mail);
    }

    [Fact]
    public async Task Webhook_за_непознат_потребител_не_пипа_никого()
    {
        var auditFrom = await _app.Db.LastAuditIdAsync();

        var response = await SendWebhookAsync(StripeWebhook.CheckoutSessionCompleted(
            "cs_test_ghost", clientReferenceId: "no-such-user-id",
            amountTotalCents: 6000, customerEmail: "ghost@example.test"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "Stripe Webhook — User Not Found");
    }

    [Fact]
    public async Task Webhook_със_сума_извън_тарифите_потвърждава_но_записва_разминаване()
    {
        // [P-06] is deliberate: Stripe's signature vouches for the origin, so a
        // mismatch is recorded but does not strand a participant who has paid.
        // This test guards that decision — change it and this fails.
        var user = await NewParticipantAsync();
        var auditFrom = await _app.Db.LastAuditIdAsync();

        var response = await SendWebhookAsync(StripeWebhook.CheckoutSessionCompleted(
            "cs_test_odd_amount", user.Id, amountTotalCents: 777, customerEmail: user.Email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Confirmed", after!.PaymentStatus);
        Assert.Equal(7.77m, after.PaidAmountEUR);

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "Payment Amount Mismatch — Stripe Checkout");
    }

    // ════════════════════════════════════════════════════════════════════
    // Creating a session
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Сесия_за_чуждо_ниво_не_се_създава()
    {
        // [T-03] A forged form asking for a different tier. The redirect in
        // OnGetAsync is for the person only; the amount is decided by the form of
        // participation inside the handler as well.
        var user = await NewParticipantAsync();

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var before = _app.Stripe.Sessions.Count;
        var auditFrom = await _app.Db.LastAuditIdAsync();

        var token = await session.AntiforgeryTokenAsync("/Payment/earlybird");

        using var client = session.NoRedirectClient();
        var response = await client.PostAsync(
            "/Payment/viewer?handler=CreateStripeSession",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("earlybird", response.Headers.Location!.ToString());

        // Nothing was ordered from Stripe.
        Assert.Equal(before, _app.Stripe.Sessions.Count);

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "Stripe Session Refused — Tier Mismatch");
    }

    [Fact]
    public async Task Форма_която_не_плаща_не_получава_сесия()
    {
        // A student: TicketPricing.ForUser returns null, so nothing is owed.
        var user = await NewParticipantAsync(partForm: "2");

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        // /Payment no longer shows such a participant a form, so the token is taken
        // from the profile; it belongs to the session rather than to the page.
        var token = await session.AntiforgeryTokenAsync("/Profile");

        using var client = session.NoRedirectClient();
        var response = await client.PostAsync(
            "/Payment/earlybird?handler=CreateStripeSession",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("tier_not_payable", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Вече_платил_потребител_не_създава_нова_сесия()
    {
        var user = await NewParticipantAsync(paymentStatus: "Confirmed");

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var before = _app.Stripe.Sessions.Count;

        using var client = session.NoRedirectClient();
        // [T-02] A confirmed participant sees a message rather than a form, so the
        // token comes from the profile.
        var token = await session.AntiforgeryTokenAsync("/Profile");

        var response = await client.PostAsync(
            "/Payment/earlybird?handler=CreateStripeSession",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(before, _app.Stripe.Sessions.Count);
    }

    // ════════════════════════════════════════════════════════════════════
    // The two paths by which one payment can be confirmed
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Едно_и_също_картово_плащане_дава_един_и_същ_метод_в_базата()
    {
        // A card payment is confirmed by two independent paths: the browser's
        // return (/Payment?payment=success) and the webhook. Whichever arrives
        // first decides, so the PaymentMethod column for one and the same payment
        // would otherwise depend on chance.
        var viaReturn  = await NewParticipantAsync();
        var viaWebhook = await NewParticipantAsync();

        // Path 1: the browser's return.
        var own = _app.Stripe.CreateSessionFor(viaReturn.Id, 6000, email: viaReturn.Email);
        using (var session = _app.NewSession())
        {
            await session.LoginParticipantAsync(viaReturn.Email!);
            await session.Client.GetAsync($"/Payment/earlybird?payment=success&session_id={own.Id}");
        }

        // Path 2: the webhook.
        await SendWebhookAsync(StripeWebhook.CheckoutSessionCompleted(
            "cs_test_method_compare", viaWebhook.Id, 6000, viaWebhook.Email));

        var a = await _app.Db.FindUserAsync(viaReturn.Email!);
        var b = await _app.Db.FindUserAsync(viaWebhook.Email!);

        Assert.Equal("Confirmed", a!.PaymentStatus);
        Assert.Equal("Confirmed", b!.PaymentStatus);

        Assert.Equal(b.PaymentMethod, a.PaymentMethod);

        // The resolution: both paths write "Stripe:Card".
        Assert.Equal("Stripe:Card", a.PaymentMethod);

        // And the admin panel has to read it as a card rather than as "Manual".
        Assert.Equal("card", ConferenceApp.Services.Payments.PaymentMethodDisplay.Family(a.PaymentMethod));
    }

    [Fact]
    public async Task Вече_платил_потребител_не_вижда_жива_форма_за_плащане()
    {
        // The page used to have a state only for "just paid" (PaymentSuccess, on
        // the return from Stripe), so a confirmed participant who opened the
        // address again was shown a working form and a "Pay €60" button. This
        // test holds the page to the corrected behaviour.
        var user = await NewParticipantAsync(paymentStatus: "Confirmed");

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var html = await session.Client.GetStringAsync("/Payment/earlybird");

        Assert.DoesNotContain("id=\"btn-pay-card\"", html);
        Assert.DoesNotContain("id=\"btn-iban-done\"", html);
        Assert.DoesNotContain("class=\"crypto-item\"", html);

        // A message and a link to the profile.
        Assert.Contains("id=\"pay-done-profile\"", html);
        Assert.Contains("/Profile", html);
        Assert.Contains(user.ReferenceNumber, html);
    }

    // ════════════════════════════════════════════════════════════════════

    private async Task<HttpResponseMessage> SendWebhookAsync(string payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/stripe/webhook")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature",
            StripeWebhook.Sign(payload, AppFixture.StripeWebhookSecret));

        using var client = _app.NewClient();
        return await client.SendAsync(request);
    }
}
