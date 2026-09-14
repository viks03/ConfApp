// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using System.Text.Json;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Payments;

/// <summary>
/// Part 2, paying in crypto. Go28 is a local stub (see <see cref="Go28Stub"/>):
/// the real gateway has a live API token and every order created there is a real
/// external record.
/// </summary>
[Collection(AppCollection.Name)]
public class CryptoPaymentTests : IAsyncLifetime
{
    private readonly AppFixture _app;
    private readonly List<string> _created = new();

    public CryptoPaymentTests(AppFixture app) => _app = app;

    public Task InitializeAsync()
    {
        _app.Go28.Reset();
        _app.Smtp.Clear();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var email in _created)
        {
            var user = await _app.Db.FindUserAsync(email);
            if (user != null)
            {
                // Orders are left with UserId = NULL by design ([D-06]); the test
                // removes them so that they do not pile up between runs.
                await _app.Db.WriteAsync(async db =>
                {
                    var orders = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                        .ToListAsync(db.CryptoOrders.Where(o => o.UserId == user.Id));
                    db.CryptoOrders.RemoveRange(orders);
                });
            }

            await _app.Db.DeleteParticipantAsync(email);
        }
    }

    private async Task<ConferenceApp.Models.ApplicationUser> NewParticipantAsync(
        string paymentStatus = "Pending")
    {
        var email = $"crypto-{Guid.NewGuid():N}@example.test";
        _created.Add(email);
        return await _app.Db.CreateParticipantAsync(email, "1", paymentStatus);
    }

    private static string CreateOrderBody(string currency = "USDC", string network = "ETH",
                                          string slug = "earlybird") =>
        JsonSerializer.Serialize(new { currency, network, slug });

    // ════════════════════════════════════════════════════════════════════
    // Creating an order, and picking it back up
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Създаването_дава_адрес_и_сума()
    {
        var user = await NewParticipantAsync();
        var tier = await _app.Db.TierAsync("earlybird");
        var expected = tier!.PromoPriceEUR ?? tier.RegularPriceEUR;

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var response = await session.PostJsonAsync("/api/crypto/create-order", CreateOrderBody());
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("cryptoAddress").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("amount").GetString()));

        // The figure the confirmation is checked against is ours, not the one the
        // gateway's answer claims.
        var orders = await _app.Db.CryptoOrdersAsync(user.Id);
        var order = Assert.Single(orders);
        Assert.Equal(expected, order.AmountEUR);
        Assert.Equal("InProcess", order.Status);
        Assert.StartsWith(user.ReferenceNumber, order.ExternalId);
    }

    [Fact]
    public async Task Поръчката_се_възстановява_при_ново_отваряне_на_страницата()
    {
        var user = await NewParticipantAsync();

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var created = JsonDocument.Parse(await (await session.PostJsonAsync(
            "/api/crypto/create-order", CreateOrderBody())).Content.ReadAsStringAsync()).RootElement;
        var orderId = created.GetProperty("orderId").GetInt32();

        // A new session stands for closing the page and opening it again.
        using var reopened = _app.NewSession();
        await reopened.LoginParticipantAsync(user.Email!);

        var active = JsonDocument.Parse(await reopened.Client.GetStringAsync(
            "/api/crypto/active-order")).RootElement;

        Assert.True(active.GetProperty("success").GetBoolean());
        Assert.Equal(orderId, active.GetProperty("orderId").GetInt32());
        Assert.Equal(created.GetProperty("cryptoAddress").GetString(),
                     active.GetProperty("cryptoAddress").GetString());

        // And only one order: opening it again creates no second one.
        Assert.Single(await _app.Db.CryptoOrdersAsync(user.Id));
    }

    [Fact]
    public async Task Спрян_Go28_връща_503_с_текст_а_не_празен_екран()
    {
        var user = await NewParticipantAsync();
        _app.Go28.CurrenciesUnavailable = true;

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var response = await session.PostJsonAsync("/api/crypto/create-order", CreateOrderBody());
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var error = JsonDocument.Parse(body).RootElement.GetProperty("error").GetString();
        Assert.False(string.IsNullOrWhiteSpace(error));

        // Nothing should have been written.
        Assert.Empty(await _app.Db.CryptoOrdersAsync(user.Id));
    }

    [Fact]
    public async Task Провал_при_създаване_в_gateway_връща_502()
    {
        var user = await NewParticipantAsync();
        _app.Go28.CreateOrderFails = true;

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var response = await session.PostJsonAsync("/api/crypto/create-order", CreateOrderBody());

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Empty(await _app.Db.CryptoOrdersAsync(user.Id));
    }

    [Fact]
    public async Task Неподдържана_валута_се_отказва()
    {
        var user = await NewParticipantAsync();

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var response = await session.PostJsonAsync("/api/crypto/create-order",
            CreateOrderBody(currency: "DOGE", network: "DOGE"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await _app.Db.CryptoOrdersAsync(user.Id));
    }

    [Fact]
    public async Task Поръчка_за_чуждо_ниво_се_отказва()
    {
        // [T-03] The amount is decided by the form of participation rather than by
        // the slug in the request body. Otherwise anyone could order the cheaper
        // tier.
        var user = await NewParticipantAsync();
        var auditFrom = await _app.Db.LastAuditIdAsync();

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var response = await session.PostJsonAsync("/api/crypto/create-order",
            CreateOrderBody(slug: "viewer"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await _app.Db.CryptoOrdersAsync(user.Id));

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "Crypto Order Refused — Tier Mismatch");
    }

    [Fact]
    public async Task Форма_която_не_плаща_не_получава_поръчка()
    {
        var email = $"crypto-{Guid.NewGuid():N}@example.test";
        _created.Add(email);
        var user = await _app.Db.CreateParticipantAsync(email, "4");   // a journalist

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(email);

        var response = await session.PostJsonAsync("/api/crypto/create-order",
            CreateOrderBody(slug: "earlybird"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await _app.Db.CryptoOrdersAsync(user.Id));
    }

    [Fact]
    public async Task Чужда_поръчка_не_се_проверява()
    {
        var owner    = await NewParticipantAsync();
        var stranger = await NewParticipantAsync();

        using var ownerSession = _app.NewSession();
        await ownerSession.LoginParticipantAsync(owner.Email!);

        var created = JsonDocument.Parse(await (await ownerSession.PostJsonAsync(
            "/api/crypto/create-order", CreateOrderBody())).Content.ReadAsStringAsync()).RootElement;
        var orderId = created.GetProperty("orderId").GetInt32();

        using var strangerSession = _app.NewSession();
        await strangerSession.LoginParticipantAsync(stranger.Email!);

        using var client = strangerSession.NoRedirectClient();
        var response = await client.GetAsync($"/api/crypto/check-status/{orderId}");

        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect,
            $"Очаквах отказ, получих {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task Създаване_без_влизане_се_отказва()
    {
        // Redirects are deliberately not followed: [Authorize] sends the request to
        // /Login, which answers 200, so a client that follows them sees "success".
        using var client = _app.NewClient(followRedirects: false);
        var response = await client.PostAsync("/api/crypto/create-order",
            new StringContent(CreateOrderBody(), System.Text.Encoding.UTF8, "application/json"));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login", response.Headers.Location!.ToString());
    }

    // ════════════════════════════════════════════════════════════════════
    // Webhook
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Webhook_с_подправено_тяло_не_потвърждава_нищо()
    {
        var user = await NewParticipantAsync();
        var auditFrom = await _app.Db.LastAuditIdAsync();

        // No order with that number exists anywhere, which is exactly what a forged
        // body sends.
        var response = await PostWebhookAsync(new
        {
            id = 999999,
            externalId = user.ReferenceNumber + "-USDC-1700000000",
            status = "Confirmed",
            amountInEUR = "60.00",
            currency = "USDC",
            network = "ETH"
        });

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Pending", after!.PaymentStatus);

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "Webhook — Unverified, Ignored");
    }

    [Fact]
    public async Task Webhook_който_лъже_за_статуса_не_потвърждава()
    {
        // The order exists, but at the gateway it is still InProcess while the body
        // claims Confirmed. The decision is taken from the gateway's answer alone.
        var user = await NewParticipantAsync();
        var orderId = await CreateOrderAsync(user.Email!);

        var response = await PostWebhookAsync(new
        {
            id = orderId,
            externalId = _app.Go28.Order(orderId).ExternalId,
            status = "Confirmed",
            amountInEUR = "60.00"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Pending", after!.PaymentStatus);
    }

    [Fact]
    public async Task Webhook_с_вярна_поръчка_потвърждава()
    {
        var user = await NewParticipantAsync();
        var tier = await _app.Db.TierAsync("earlybird");
        var expected = tier!.PromoPriceEUR ?? tier.RegularPriceEUR;

        var orderId = await CreateOrderAsync(user.Email!);
        _app.Go28.MarkConfirmed(orderId);

        var auditFrom = await _app.Db.LastAuditIdAsync();

        var response = await PostWebhookAsync(new
        {
            id = orderId,
            externalId = _app.Go28.Order(orderId).ExternalId,
            status = "Confirmed"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Confirmed", after!.PaymentStatus);
        Assert.Equal(expected, after.PaidAmountEUR);
        Assert.Equal("Crypto:USDC", after.PaymentMethod);
        Assert.NotNull(after.PaidAt);

        var order = Assert.Single(await _app.Db.CryptoOrdersAsync(user.Id));
        Assert.Equal("Confirmed", order.Status);
        Assert.NotNull(order.CompletedAt);

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "Payment Confirmed — Webhook");

        var mail = await _app.Smtp.WaitForPaymentMailAsync(user.Email!, TimeSpan.FromSeconds(20));
        Assert.NotNull(mail);
    }

    [Fact]
    public async Task Повторен_webhook_за_същата_поръчка_не_прави_втора_промяна()
    {
        var user = await NewParticipantAsync();
        var orderId = await CreateOrderAsync(user.Email!);
        _app.Go28.MarkConfirmed(orderId);

        var body = new { id = orderId, externalId = _app.Go28.Order(orderId).ExternalId, status = "Confirmed" };

        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(body)).StatusCode);

        var afterFirst = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Confirmed", afterFirst!.PaymentStatus);

        // The mail queue is asynchronous, so the first message is waited for before
        // the sink is cleared.
        Assert.NotNull(await _app.Smtp.WaitForPaymentMailAsync(user.Email!, TimeSpan.FromSeconds(20)));

        var auditFrom = await _app.Db.LastAuditIdAsync();
        _app.Smtp.Clear();

        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(body)).StatusCode);

        var afterSecond = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal(afterFirst.PaidAt, afterSecond!.PaidAt);
        Assert.Equal(afterFirst.PaidAmountEUR, afterSecond.PaidAmountEUR);

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.DoesNotContain(audit, a => a.Action == "Payment Confirmed — Webhook");

        Assert.Null(await _app.Smtp.WaitForPaymentMailAsync(user.Email!, TimeSpan.FromSeconds(6)));
    }

    [Fact]
    public async Task Разминаване_в_сумата_оставя_Pending()
    {
        var user = await NewParticipantAsync();
        var orderId = await CreateOrderAsync(user.Email!);

        // The gateway reports an amount other than the one the order was created
        // for.
        _app.Go28.MarkConfirmed(orderId, reportedAmountEUR: "1.00");

        var auditFrom = await _app.Db.LastAuditIdAsync();

        var response = await PostWebhookAsync(new
        {
            id = orderId,
            externalId = _app.Go28.Order(orderId).ExternalId,
            status = "Confirmed"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Pending", after!.PaymentStatus);
        Assert.Null(after.PaidAmountEUR);

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "Payment Amount Mismatch — Webhook");
    }

    [Fact]
    public async Task Webhook_с_празен_външен_номер_не_потвърждава_никого()
    {
        // [D-03] An empty string used to match the first profile with an empty
        // ReferenceNumber.
        var user = await NewParticipantAsync();
        var orderId = await CreateOrderAsync(user.Email!);

        _app.Go28.MarkConfirmed(orderId);
        _app.Go28.Order(orderId).ExternalId = string.Empty;

        var auditFrom = await _app.Db.LastAuditIdAsync();

        var response = await PostWebhookAsync(new { id = orderId, externalId = "", status = "Confirmed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Pending", after!.PaymentStatus);

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "Webhook — Empty Reference, Ignored");
    }

    [Fact]
    public async Task Webhook_без_номер_на_поръчка_се_отказва()
    {
        var response = await PostWebhookAsync(new { id = 0, externalId = "BCE2026-00000AAA", status = "Confirmed" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════
    // The two entry points that learn of a payment without a webhook — [P-11]
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Проверката_на_статуса_потвърждава_при_платена_поръчка()
    {
        var user = await NewParticipantAsync();

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var orderId = JsonDocument.Parse(await (await session.PostJsonAsync(
                "/api/crypto/create-order", CreateOrderBody())).Content.ReadAsStringAsync())
            .RootElement.GetProperty("orderId").GetInt32();

        _app.Go28.MarkConfirmed(orderId);

        var status = JsonDocument.Parse(await session.Client.GetStringAsync(
            $"/api/crypto/check-status/{orderId}")).RootElement;

        Assert.True(status.GetProperty("success").GetBoolean());
        Assert.True(status.GetProperty("isPaid").GetBoolean());

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Confirmed", after!.PaymentStatus);
    }

    [Fact]
    public async Task Отварянето_на_страницата_потвърждава_при_платена_поръчка()
    {
        var user = await NewParticipantAsync();

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var orderId = JsonDocument.Parse(await (await session.PostJsonAsync(
                "/api/crypto/create-order", CreateOrderBody())).Content.ReadAsStringAsync())
            .RootElement.GetProperty("orderId").GetInt32();

        _app.Go28.MarkConfirmed(orderId);

        // active-order is the path a page reload goes through.
        await session.Client.GetStringAsync("/api/crypto/active-order");

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Confirmed", after!.PaymentStatus);
    }

    [Fact]
    public async Task Разминаване_в_сумата_не_рисува_платено_при_polling()
    {
        var user = await NewParticipantAsync();

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var orderId = JsonDocument.Parse(await (await session.PostJsonAsync(
                "/api/crypto/create-order", CreateOrderBody())).Content.ReadAsStringAsync())
            .RootElement.GetProperty("orderId").GetInt32();

        _app.Go28.MarkConfirmed(orderId, reportedAmountEUR: "3.00");

        var status = JsonDocument.Parse(await session.Client.GetStringAsync(
            $"/api/crypto/check-status/{orderId}")).RootElement;

        Assert.Equal("Confirmed", status.GetProperty("status").GetString());
        Assert.False(status.GetProperty("isPaid").GetBoolean());

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Pending", after!.PaymentStatus);
    }

    // ════════════════════════════════════════════════════════════════════
    // Through the browser
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Изборът_на_монета_показва_адрес_и_сума_на_екрана()
    {
        var user = await NewParticipantAsync();

        await using var context = await _app.NewLoggedInContextAsync(user.Email!);
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Payment/earlybird");
        await page.ClickAsync("#tab-crypto");
        await page.ClickAsync("button.crypto-item:has-text(\"USDC\")");

        var address = await WaitForWalletAddressAsync(page);
        var amount  = (await page.Locator("#cryptoAmount").InnerTextAsync()).Trim();

        Assert.False(string.IsNullOrWhiteSpace(address), "Адресът на портфейла е празен.");
        Assert.False(string.IsNullOrWhiteSpace(amount),  "Сумата в крипто е празна.");
        Assert.Contains("USDC", amount);
    }

    [Fact]
    public async Task Поръчката_се_вижда_отново_след_презареждане()
    {
        var user = await NewParticipantAsync();

        await using var context = await _app.NewLoggedInContextAsync(user.Email!);
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Payment/earlybird");
        await page.ClickAsync("#tab-crypto");
        await page.ClickAsync("button.crypto-item:has-text(\"USDC\")");

        var address = await WaitForWalletAddressAsync(page);

        // Closing and reopening: a new tab in the same session rather than
        // reopening the same one, because Chromium aborts the request in flight
        // and returns ERR_NETWORK_IO_SUSPENDED.
        await page.CloseAsync();

        var reopened = await context.NewPageAsync();
        await reopened.GotoAsync("/Payment/earlybird");
        await reopened.ClickAsync("#tab-crypto");

        Assert.Equal(address, await WaitForWalletAddressAsync(reopened));

        // And only one order: the reload creates no second one.
        Assert.Single(await _app.Db.CryptoOrdersAsync(user.Id));
    }

    [Fact]
    public async Task Спрян_Go28_показва_съобщение_а_не_празен_екран()
    {
        var user = await NewParticipantAsync();
        _app.Go28.CurrenciesUnavailable = true;

        await using var context = await _app.NewLoggedInContextAsync(user.Email!);
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Payment/earlybird");
        await page.ClickAsync("#tab-crypto");
        await page.ClickAsync("button.crypto-item:has-text(\"USDC\")");

        var error = page.Locator("#crypto-error-msg");
        await error.WaitForAsync(new Microsoft.Playwright.LocatorWaitForOptions
        {
            State = Microsoft.Playwright.WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var text = (await error.InnerTextAsync()).Trim();
        Assert.False(string.IsNullOrWhiteSpace(text), "Съобщението за грешка е празно.");
    }

    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// selectCrypto shows the grid immediately, while the address and the amount
    /// are filled in once the request answers. So it is the field that is waited
    /// for, not the grid.
    /// </summary>
    private static async Task<string> WaitForWalletAddressAsync(Microsoft.Playwright.IPage page)
    {
        await page.WaitForFunctionAsync(
            "() => { const el = document.getElementById('walletAddr');" +
            " return el && el.textContent.trim().length > 20 ? el.textContent.trim() : null; }",
            null,
            new Microsoft.Playwright.PageWaitForFunctionOptions { Timeout = 20000 });

        return (await page.Locator("#walletAddr").InnerTextAsync()).Trim();
    }

    private async Task<int> CreateOrderAsync(string email)
    {
        using var session = _app.NewSession();
        await session.LoginParticipantAsync(email);

        var response = await session.PostJsonAsync("/api/crypto/create-order", CreateOrderBody());
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("orderId").GetInt32();
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(object body)
    {
        using var client = _app.NewClient();
        return await client.PostAsync("/api/crypto/webhook",
            new StringContent(JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8, "application/json"));
    }
}
