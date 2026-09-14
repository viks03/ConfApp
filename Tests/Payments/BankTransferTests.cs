// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using System.Text.Json;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Payments;

/// <summary>Part 2, the bank transfer: the participant's declaration and the manual confirmation from the admin panel.</summary>
[Collection(AppCollection.Name)]
public class BankTransferTests : IAsyncLifetime
{
    private readonly AppFixture _app;
    private readonly List<string> _created = new();

    public BankTransferTests(AppFixture app) => _app = app;

    public Task InitializeAsync()
    {
        _app.Smtp.Clear();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var email in _created)
            await _app.Db.DeleteParticipantAsync(email);
    }

    private async Task<ConferenceApp.Models.ApplicationUser> NewParticipantAsync(
        string partForm = "1", string paymentStatus = "Pending")
    {
        var email = $"iban-{Guid.NewGuid():N}@example.test";
        _created.Add(email);
        return await _app.Db.CreateParticipantAsync(email, partForm, paymentStatus);
    }

    [Fact]
    public async Task Извърших_превода_се_записва_и_праща_писмо_с_дължимата_сума()
    {
        var user = await NewParticipantAsync();
        var tier = await _app.Db.TierAsync("earlybird");
        var expected = tier!.PromoPriceEUR ?? tier.RegularPriceEUR;

        var auditFrom = await _app.Db.LastAuditIdAsync();

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var response = await session.PostHandlerAsync("/Payment/earlybird", "SubmitIban");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.True(json.GetProperty("success").GetBoolean());

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.NotNull(after!.IbanTransferSubmittedAt);
        // Declaring a transfer is not a payment: the status stays pending.
        Assert.Equal("Pending", after.PaymentStatus);

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "IBAN Transfer Submitted");

        var mail = await _app.Smtp.WaitForPaymentMailAsync(user.Email!, TimeSpan.FromSeconds(20));
        Assert.NotNull(mail);

        // The amount in the message comes from TicketPricing rather than from the
        // handler's empty value; otherwise the message said "0.00 EUR".
        var amountText = expected!.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(amountText, mail!.Body);
    }

    [Fact]
    public async Task Второто_натискане_не_праща_второ_писмо()
    {
        var user = await NewParticipantAsync();

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        await session.PostHandlerAsync("/Payment/earlybird", "SubmitIban");
        var first = await _app.Db.FindUserAsync(user.Email!);
        Assert.NotNull(first!.IbanTransferSubmittedAt);

        // The first message is waited for, so that it is not mistaken for a second.
        Assert.NotNull(await _app.Smtp.WaitForPaymentMailAsync(user.Email!, TimeSpan.FromSeconds(20)));
        _app.Smtp.Clear();

        var second = await session.PostHandlerAsync("/Payment/earlybird", "SubmitIban");
        Assert.True(JsonDocument.Parse(await second.Content.ReadAsStringAsync())
            .RootElement.GetProperty("success").GetBoolean());

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal(first.IbanTransferSubmittedAt, after!.IbanTransferSubmittedAt);

        Assert.Null(await _app.Smtp.WaitForPaymentMailAsync(user.Email!, TimeSpan.FromSeconds(6)));
    }

    [Fact]
    public async Task Вече_платил_потребител_не_може_да_подаде_превод()
    {
        // [P-14] Without this check a participant who had paid by card received a
        // "payment pending" message after the "confirmed" one.
        var user = await NewParticipantAsync(paymentStatus: "Confirmed");

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        // [T-02] A confirmed participant sees a message rather than a form, so the
        // token is taken from the profile. The request here is the one a forged
        // form would send.
        var response = await session.PostHandlerAsync(
            "/Payment/earlybird", "SubmitIban", tokenFrom: "/Profile");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.False(json.GetProperty("success").GetBoolean());
        Assert.Equal("already_confirmed", json.GetProperty("error").GetString());

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.Null(after!.IbanTransferSubmittedAt);

        // The "payment pending" message must not go out ([P-14]). The sign-in code
        // does not count, which is why only a payment message is asked for.
        Assert.Null(await _app.Smtp.WaitForPaymentMailAsync(user.Email!, TimeSpan.FromSeconds(6)));
    }

    [Fact]
    public async Task Подаване_без_влизане_се_отказва()
    {
        using var client = _app.NewClient(followRedirects: false);
        var response = await client.PostAsync("/Payment/earlybird?handler=SubmitIban",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Администратор_потвърждава_превода_ръчно()
    {
        var user = await NewParticipantAsync();
        var tier = await _app.Db.TierAsync("earlybird");
        var expected = tier!.PromoPriceEUR ?? tier.RegularPriceEUR;

        // First the participant declares that they have made the transfer.
        using (var participant = _app.NewSession())
        {
            await participant.LoginParticipantAsync(user.Email!);
            await participant.PostHandlerAsync("/Payment/earlybird", "SubmitIban");
        }

        _app.Smtp.Clear();
        var auditFrom = await _app.Db.LastAuditIdAsync();

        using var admin = _app.NewSession();
        await admin.LoginAdminAsync(_app.Credentials.AdminEmail, _app.Credentials.AdminPassword);

        var response = await admin.PostHandlerAsync("/Admin", "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id,
            ["method"] = "IBAN"
        });

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(json.GetProperty("success").GetBoolean(),
            json.TryGetProperty("message", out var m) ? m.GetString() : "без съобщение");

        var after = await _app.Db.FindUserAsync(user.Email!);
        Assert.Equal("Confirmed", after!.PaymentStatus);
        Assert.Equal("IBAN", after.PaymentMethod);
        Assert.NotNull(after.PaidAt);
        // [D-01] What the form of participation owes is recorded as the amount paid.
        Assert.Equal(expected, after.PaidAmountEUR);

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "Payment Confirmed — Admin");

        Assert.NotNull(await _app.Smtp.WaitForPaymentMailAsync(user.Email!, TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public async Task Второ_ръчно_потвърждаване_се_отказва()
    {
        var user = await NewParticipantAsync(paymentStatus: "Confirmed");

        using var admin = _app.NewSession();
        await admin.LoginAdminAsync(_app.Credentials.AdminEmail, _app.Credentials.AdminPassword);

        var response = await admin.PostHandlerAsync("/Admin", "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id,
            ["method"] = "IBAN"
        });

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.False(json.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Обикновен_потребител_не_може_да_потвърди_плащане()
    {
        var user  = await NewParticipantAsync();
        var other = await NewParticipantAsync();

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        using var client = session.NoRedirectClient();
        var response = await client.PostAsync("/Admin?handler=ConfirmPayment",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["userId"] = other.Id,
                ["method"] = "IBAN"
            }));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);

        var after = await _app.Db.FindUserAsync(other.Email!);
        Assert.Equal("Pending", after!.PaymentStatus);
    }
}
