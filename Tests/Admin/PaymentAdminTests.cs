// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using ConferenceApp.Services.Payments;
using ConferenceApp.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Tests.Admin;

/// <summary>
/// Part 6, confirming and cancelling a payment by hand from the panel.
/// <para>
/// This is the path for a bank transfer: the administrator sees the transfer in
/// the account and presses the button. So what is checked here is not only the
/// status but that the participant finds out — [D-01] records the amount, and the
/// message goes out exactly once.
/// </para>
/// </summary>
public class PaymentAdminTests : AdminTestBase
{
    public PaymentAdminTests(AppFixture app) : base(app, "ad-pay") { }

    private async Task<decimal?> DueForAsync(ApplicationUser user)
    {
        var tiers = await App.Db.ReadAsync(db => db.TicketTiers.AsNoTracking().ToListAsync());
        return TicketPricing.PriceEUR(TicketPricing.ForUser(tiers, user));
    }

    [Fact]
    public async Task Потвърждаването_вдига_статуса_и_записва_сумата()
    {
        var user = await NewParticipantAsync("1");
        await App.Db.MarkIbanSubmittedAsync(user.Email!);

        using var admin = await SignedInAdminAsync();
        var reply = await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id,
            ["method"] = "IBAN"
        });

        Assert.True(reply.Success, reply.Message);

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Confirmed", after!.PaymentStatus);
        Assert.Equal("IBAN", after.PaymentMethod);
        Assert.NotNull(after.PaidAt);
        Assert.Equal(await DueForAsync(user), after.PaidAmountEUR);
    }

    [Fact]
    public async Task Потвърждаването_праща_писмо_на_участника()
    {
        var user = await NewParticipantAsync("1");
        App.Smtp.Clear();

        using var admin = await SignedInAdminAsync();
        await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id,
            ["method"] = "IBAN"
        });

        var mail = await App.Smtp.WaitForPaymentMailAsync(user.Email!, TimeSpan.FromSeconds(10));
        Assert.NotNull(mail);
        Assert.Contains(user.ReferenceNumber!, mail!.Body);
    }

    [Fact]
    public async Task Потвърждаването_оставя_запис_в_одита()
    {
        var user = await NewParticipantAsync();

        using var admin = await SignedInAdminAsync();
        await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id,
            ["method"] = "IBAN"
        });

        var audit = await App.Db.AuditForAsync(user.Email!);
        var entry = audit.FirstOrDefault(a => a.Action == "Payment Confirmed — Admin");

        Assert.NotNull(entry);
        Assert.Contains("Method: IBAN", entry!.Details);
        Assert.Contains(user.ReferenceNumber!, entry.Details);
    }

    [Fact]
    public async Task Второ_потвърждаване_не_прави_нищо()
    {
        var user = await NewParticipantAsync();

        using var admin = await SignedInAdminAsync();
        Assert.True((await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id, ["method"] = "IBAN"
        })).Success);

        var first = await App.Db.FindUserAsync(user.Email!);

        // The message goes out through the background queue rather than in the
        // request itself, so if the sink is cleared before it arrives the next
        // assertion counts it as a second message.
        Assert.NotNull(await App.Smtp.WaitForPaymentMailAsync(user.Email!, TimeSpan.FromSeconds(10)));
        App.Smtp.Clear();

        var second = await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id, ["method"] = "Card"
        });

        Assert.False(second.Success);
        Assert.Contains("already confirmed", second.Message, StringComparison.OrdinalIgnoreCase);

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("IBAN", after!.PaymentMethod);
        Assert.Equal(first!.PaidAt, after.PaidAt);

        var mail = await App.Smtp.WaitForPaymentMailAsync(user.Email!, TimeSpan.FromSeconds(3));
        Assert.Null(mail);
    }

    [Theory]
    [InlineData("Card")]
    [InlineData("Crypto")]
    [InlineData("IBAN")]
    [InlineData("Manual")]
    public async Task Познатите_методи_се_записват_както_са(string method)
    {
        var user = await NewParticipantAsync();

        using var admin = await SignedInAdminAsync();
        await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id, ["method"] = method
        });

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal(method, after!.PaymentMethod);
    }

    [Theory]
    [InlineData("Bitcoin")]
    [InlineData("")]
    [InlineData("<script>")]
    public async Task Непознат_метод_става_Manual(string method)
    {
        var user = await NewParticipantAsync();

        using var admin = await SignedInAdminAsync();
        var reply = await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id, ["method"] = method
        });

        Assert.True(reply.Success, reply.Message);

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Manual", after!.PaymentMethod);
    }

    /// <summary>
    /// A student and a journalist have no price. Confirming by hand still has to
    /// work; there is simply no amount to record.
    /// </summary>
    [Fact]
    public async Task Ниво_без_цена_се_потвърждава_без_сума()
    {
        var user = await NewParticipantAsync("2");

        using var admin = await SignedInAdminAsync();
        var reply = await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id, ["method"] = "Manual"
        });

        Assert.True(reply.Success, reply.Message);

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Confirmed", after!.PaymentStatus);
        Assert.Null(after.PaidAmountEUR);
    }

    /// <summary>
    /// Someone who paid a promotional price before it was withdrawn keeps THEIR
    /// amount. The recorded value takes precedence over the current tier.
    /// </summary>
    [Fact]
    public async Task Вече_записана_сума_не_се_презаписва()
    {
        var user = await NewParticipantAsync("1");

        await App.Db.WriteAsync(async db =>
        {
            var row = await db.Users.FirstAsync(u => u.Email == user.Email);
            row.PaidAmountEUR = 42.50m;
        });

        using var admin = await SignedInAdminAsync();
        await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id, ["method"] = "IBAN"
        });

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal(42.50m, after!.PaidAmountEUR);
    }

    [Fact]
    public async Task Потвърждаване_на_несъществуващ_не_гърми()
    {
        using var admin = await SignedInAdminAsync();

        var reply = await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = Guid.NewGuid().ToString(), ["method"] = "IBAN"
        });

        Assert.False(reply.Success);
        Assert.Contains("not found", reply.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ════════════════════════════════════════════════════════════════════
    // Cancelling
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Отказването_връща_статуса_и_маха_заявения_превод()
    {
        var user = await NewParticipantAsync();
        await App.Db.MarkIbanSubmittedAsync(user.Email!);

        using var admin = await SignedInAdminAsync();
        var reply = await PostAsync(admin, "CancelPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id
        });

        Assert.True(reply.Success, reply.Message);

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Cancelled", after!.PaymentStatus);
        Assert.Null(after.IbanTransferSubmittedAt);
        Assert.Null(after.PaidAt);

        var audit = await App.Db.AuditForAsync(user.Email!);
        Assert.Contains(audit, a => a.Action == "Payment Cancelled — Admin");
    }

    /// <summary>
    /// Cancelling a payment that is already confirmed is allowed: the administrator
    /// may have confirmed it by mistake. The test guards that decision — if it is
    /// ever forbidden, that is a change of behaviour rather than a detail.
    /// </summary>
    [Fact]
    public async Task Отказването_действа_и_върху_потвърдено()
    {
        var user = await NewParticipantAsync();

        using var admin = await SignedInAdminAsync();
        await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id, ["method"] = "IBAN"
        });

        var reply = await PostAsync(admin, "CancelPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id
        });
        Assert.True(reply.Success, reply.Message);

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Cancelled", after!.PaymentStatus);

        var audit = await App.Db.AuditForAsync(user.Email!);
        var entry = audit.First(a => a.Action == "Payment Cancelled — Admin");
        Assert.Contains("Previous status: Confirmed", entry.Details);
    }

    /// <summary>
    /// After a manual confirmation the participant has to see it in their profile;
    /// otherwise the panel and the profile say different things.
    /// </summary>
    [Fact]
    public async Task Профилът_на_участника_показва_потвърденото()
    {
        var user = await NewParticipantAsync();

        using (var admin = await SignedInAdminAsync())
        {
            await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
            {
                ["userId"] = user.Id, ["method"] = "IBAN"
            });
        }

        using var session = await SignedInAsync(user);
        using var response = await session.Client.GetAsync("/Profile");
        var html = await response.ReadPageAsync();

        Assert.DoesNotContain("/Payment/", html[html.IndexOf("status-panel", StringComparison.Ordinal)..]);
    }

    /// <summary>
    /// The queue of pending transfers is filled by the participants themselves and
    /// cleared on confirmation; otherwise the administrator stares at a queue that
    /// never moves.
    /// </summary>
    [Fact]
    public async Task Опашката_за_банков_превод_се_изчиства_след_потвърждаване()
    {
        var user = await NewParticipantAsync();
        await App.Db.MarkIbanSubmittedAsync(user.Email!);

        using var admin = await SignedInAdminAsync();

        Assert.Contains(user.Email!, await IbanQueueAsync(admin));

        await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id, ["method"] = "IBAN"
        });

        Assert.DoesNotContain(user.Email!, await IbanQueueAsync(admin));
    }

    // ════════════════════════════════════════════════════════════════════
    // The All Payments table
    // ════════════════════════════════════════════════════════════════════

    /// <summary>The participant's row in the All Payments table.</summary>
    private static string PaymentRow(string html, string email)
    {
        var all = html.IndexOf("id=\"paymentsTableBody\"", StringComparison.Ordinal);
        Assert.True(all > 0, "Не намирам таблицата с плащанията.");

        var index = html.IndexOf(email, all, StringComparison.OrdinalIgnoreCase);
        Assert.True(index > 0, $"{email} не се вижда в „All Payments“.");

        var start = html.LastIndexOf("<tr", index, StringComparison.Ordinal);
        var end = html.IndexOf("</tr>", index, StringComparison.Ordinal);
        return html[start..end];
    }

    /// <summary>
    /// The values the application really writes into <c>PaymentMethod</c>. Card and
    /// crypto payments carry a prefix ([T-01]); a manual confirmation from the
    /// panel does not.
    /// </summary>
    public static TheoryData<string, string, string> WrittenMethods => new()
    {
        { "Stripe:Card",           "card",       "Card (Stripe)" },
        { "Crypto:USDC",           "crypto",     "Crypto (Go28)" },
        { "Crypto:USDC:ETH:5001",  "crypto",     "Crypto (Go28)" },
        { "IBAN",                  "iban",       "IBAN Transfer" },
        { "Subsidised",            "subsidised", "Subsidised" }
    };

    /// <summary>The class and the label of the method badge in a given row.</summary>
    private static (string Css, string Label) MethodBadge(string row)
    {
        var match = System.Text.RegularExpressions.Regex.Match(row,
            "<span class=\"method-badge (?<css>[^\"]*)\"[^>]*>(?<label>[^<]*)</span>");

        Assert.True(match.Success, "Редът няма бадж за метод на плащане.");
        return (match.Groups["css"].Value.Trim(), Html.Text(match.Groups["label"].Value).Trim());
    }

    [Theory, MemberData(nameof(WrittenMethods))]
    public async Task Баджът_в_списъка_с_участници_разбира_префиксите(
        string stored, string family, string _)
    {
        var user = await NewParticipantAsync(paymentStatus: "Confirmed");
        await SetMethodAsync(user.Email!, stored);

        using var admin = await SignedInAdminAsync();
        var badge = MethodBadge(RegistrationRow(await PanelAsync(admin), user.Email!));

        Assert.Equal($"method-{family}", badge.Css);

        // The label is checked against the application's own helper rather than a
        // copied string: changing a translation must not break the test.
        Assert.Equal(PaymentMethodDisplay.Label(stored), badge.Label);
    }

    /// <summary>
    /// The same value, the same panel, a different table. The badge in All Payments
    /// still compares the WHOLE value against five literals — exactly what [T-01]
    /// removed from the participants table.
    /// </summary>
    [Theory, MemberData(nameof(WrittenMethods))]
    public async Task Баджът_в_All_Payments_също_разбира_префиксите(
        string stored, string family, string _)
    {
        var user = await NewParticipantAsync(paymentStatus: "Confirmed");
        await SetMethodAsync(user.Email!, stored);

        using var admin = await SignedInAdminAsync();
        var badge = MethodBadge(PaymentRow(await PanelAsync(admin), user.Email!));

        Assert.Equal($"method-{family}", badge.Css);
        Assert.Equal(PaymentMethodDisplay.Label(stored), badge.Label);
    }

    /// <summary>
    /// The method filter compares exactly (<c>rowData === val</c> in adminPanel.js),
    /// so <c>data-method</c> has to be one of the five values in the dropdown;
    /// otherwise choosing "Card (Stripe)" hides the card payments themselves.
    /// </summary>
    [Theory, MemberData(nameof(WrittenMethods))]
    public async Task Филтърът_по_метод_намира_плащането(
        string stored, string _, string optionLabel)
    {
        var user = await NewParticipantAsync(paymentStatus: "Confirmed");
        await SetMethodAsync(user.Email!, stored);

        using var admin = await SignedInAdminAsync();
        var html = await PanelAsync(admin);

        var option = System.Text.RegularExpressions.Regex.Match(html,
            "<option value=\"(?<v>[^\"]+)\">" + System.Text.RegularExpressions.Regex.Escape(optionLabel) + "</option>");
        Assert.True(option.Success, $"В падащото меню няма „{optionLabel}“.");

        var expected = option.Groups["v"].Value;

        var row = PaymentRow(html, user.Email!);
        var actual = System.Text.RegularExpressions.Regex
            .Match(row, "data-method=\"(?<v>[^\"]*)\"").Groups["v"].Value;

        // The script compares EXACTLY, but lower-cases both sides first.
        Assert.Equal(expected.ToLowerInvariant(), actual.ToLowerInvariant());
    }

    private Task SetMethodAsync(string email, string method) =>
        App.Db.WriteAsync(async db =>
        {
            var row = await db.Users.FirstAsync(u => u.Email == email);
            row.PaymentMethod = method;
            row.PaidAt = DateTime.UtcNow;
        });

    /// <summary>The participant's row in the Registrations table.</summary>
    private static string RegistrationRow(string html, string email)
    {
        var table = html.IndexOf("id=\"registrationsTableBody\"", StringComparison.Ordinal);
        var index = html.IndexOf(email, table, StringComparison.OrdinalIgnoreCase);
        Assert.True(index > 0, $"{email} не се вижда в списъка с участници.");

        var start = html.LastIndexOf("<tr", index, StringComparison.Ordinal);
        var end = html.IndexOf("</tr>", index, StringComparison.Ordinal);
        return html[start..end];
    }

    /// <summary>The "IBAN Transfers Awaiting Confirmation" card alone, not the whole tab.</summary>
    private static async Task<string> IbanQueueAsync(HttpSession admin)
    {
        var html = await PanelAsync(admin);

        var start = html.IndexOf("IBAN Transfers Awaiting Confirmation", StringComparison.Ordinal);
        if (start < 0) return string.Empty;          // an empty queue: the card is not rendered at all

        var end = html.IndexOf("All Payments", start, StringComparison.Ordinal);
        return end > start ? html[start..end] : html[start..];
    }

    /// <summary>
    /// A cancellation has to clear the queue as well; otherwise the administrator
    /// sees a row there is nothing left to do about.
    /// </summary>
    [Fact]
    public async Task Опашката_се_изчиства_и_при_отказ()
    {
        var user = await NewParticipantAsync();
        await App.Db.MarkIbanSubmittedAsync(user.Email!);

        using var admin = await SignedInAdminAsync();
        Assert.Contains(user.Email!, await IbanQueueAsync(admin));

        await PostAsync(admin, "CancelPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id
        });

        Assert.DoesNotContain(user.Email!, await IbanQueueAsync(admin));
    }
}
