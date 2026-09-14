// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using System.Text.Json;
using ConferenceApp.Models;
using ConferenceApp.Tests.Auth;
using ConferenceApp.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Tests.Emails;

/// <summary>
/// Part 8: "every message goes out on the right event".
/// <para>
/// The six automatic kinds have eight entry points: the sign-in and registration
/// code (three places), a confirmed payment (five places), a declared bank
/// transfer, an approved and a rejected verification, and a status change. Each
/// is triggered from where a person triggers it, not by calling the composer.
/// </para>
/// <para>
/// The negative cases live here rather than separately, because "goes out on the
/// right event" also means "does not go out on anything else": a repeated
/// confirmation, a refused action and an edit that changes nothing must not fill
/// the participant's mailbox.
/// </para>
/// </summary>
public class EmailTriggerTests : EmailTestBase
{
    public EmailTriggerTests(AppFixture app) : base(app, "mail-trg") { }

    // ════════════════════════════════════════════════════════════════════
    // The confirmation and sign-in codes
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Регистрацията_праща_код_за_потвърждение()
    {
        var email = NewEmail();

        using var session = App.NewSession();
        var done = await RegistrationFlow.RunAsync(session, new RegistrationForm { Email = email });
        Assert.True(done.Response.IsSuccessStatusCode);

        var mail = await ExpectAsync(email, "Email_Otp_Registration_Subject");

        // The code in the message has to be the one in the database; otherwise the
        // user types in something that opens nothing.
        var otp = await App.Db.WaitForOtpAsync(email, "Registration", TimeSpan.FromSeconds(15));
        Assert.NotNull(otp);
        Assert.Contains(otp!.Code, mail.Body);
    }

    [Fact]
    public async Task Влизането_праща_код_за_вход_а_не_за_регистрация()
    {
        var user = await NewParticipantAsync();

        using var session = App.NewSession();
        await RequestLoginCodeAsync(session, user.Email!);

        var mail = await ExpectAsync(user.Email!, "Email_Otp_Login_Subject");

        var otp = await App.Db.WaitForOtpAsync(user.Email!, "Login", TimeSpan.FromSeconds(15));
        Assert.NotNull(otp);
        Assert.Contains(otp!.Code, mail.Body);

        // The two kinds of code differ only in their wording, so if they were
        // swapped the message would invite someone to "confirm their registration"
        // during an ordinary sign-in.
        await ExpectNoneAsync(user.Email!, "Email_Otp_Registration_Subject", seconds: 3);
    }

    [Fact]
    public async Task Нов_код_от_Verification_праща_второ_писмо()
    {
        var user = await NewParticipantAsync();

        using var session = App.NewSession();
        await RequestLoginCodeAsync(session, user.Email!);
        await ExpectAsync(user.Email!, "Email_Otp_Login_Subject");

        var resend = await session.PostHandlerAsync("/Verification", "Resend");
        Assert.Contains(Resx.Value("Pages.Verification", "Success_CodeResent"),
            await resend.ReadPageAsync());

        var count = await WaitForCountAsync(user.Email!, "Email_Otp_Login_Subject", 2);
        Assert.True(count >= 2,
            $"След „Изпрати нов код“ до {user.Email} има {count} писма с код вместо поне 2.");

        // And the second message carries the VALID code rather than the old one:
        // issuing a new code invalidates the previous one in the database.
        var codes = await App.Db.OtpCodesAsync(user.Email!, "Login");
        var live = codes.Single(c => !c.IsUsed);

        var mails = App.Smtp.All
            .Where(m => m.To.Contains(user.Email!, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.ReceivedAt)
            .ToList();

        Assert.Contains(live.Code, mails[^1].Body);
    }

    /// <summary>
    /// An unknown address must trigger nothing: otherwise the arrival of a message
    /// tells an attacker who is registered.
    /// </summary>
    [Fact]
    public async Task Непознат_адрес_не_получава_код()
    {
        var email = NewEmail();   // no participant was ever created

        using var session = App.NewSession();
        await RequestLoginCodeAsync(session, email);

        await ExpectNoneAsync(email, "Email_Otp_Login_Subject");
        Assert.Empty(await App.Db.OtpCodesAsync(email));
    }

    // ════════════════════════════════════════════════════════════════════
    // Payment
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Заявката_за_банков_превод_праща_писмо_в_обработка()
    {
        var user = await NewParticipantAsync();

        using var session = await SignedInAsync(user);
        var response = await session.PostHandlerAsync("/Payment/earlybird", "SubmitIban");
        Assert.True(Json(await response.Content.ReadAsStringAsync()).GetProperty("success").GetBoolean());

        var mail = await ExpectAsync(user.Email!, "Email_PayPending_Subject");
        Assert.Contains(user.ReferenceNumber!, mail.Body);

        // Declaring a transfer is not a payment, so the "confirmed" message must
        // not go out.
        await ExpectNoneAsync(user.Email!, "Email_PayConfirmed_Subject", seconds: 3);
    }

    [Fact]
    public async Task Ръчното_потвърждаване_от_панела_праща_писмо_потвърдено()
    {
        var user = await NewParticipantAsync();

        using var admin = await SignedInAdminAsync();
        var reply = await ConfirmPaymentAsync(admin, user, "IBAN");
        Assert.True(reply.Success, reply.Message);

        var mail = await ExpectAsync(user.Email!, "Email_PayConfirmed_Subject");
        Assert.Contains(user.ReferenceNumber!, mail.Body);
    }

    [Fact]
    public async Task Webhook_от_Stripe_праща_писмо_потвърдено()
    {
        var user = await NewParticipantAsync();

        var payload = StripeWebhook.CheckoutSessionCompleted(
            $"cs_test_{Tag}_{Guid.NewGuid():N}"[..40], user.Id, 6000, user.Email);

        var response = await SendStripeWebhookAsync(payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var mail = await ExpectAsync(user.Email!, "Email_PayConfirmed_Subject");
        Assert.Contains(user.ReferenceNumber!, mail.Body);

        // The webhook arrives with no request from our site, so the amount in the
        // message is the one Stripe took rather than an empty field.
        Assert.Contains("60.00", mail.Body);
    }

    [Fact]
    public async Task Webhook_от_Go28_праща_писмо_потвърдено()
    {
        var user = await NewParticipantAsync();

        var orderId = await CreateCryptoOrderAsync(user.Email!);
        App.Go28.MarkConfirmed(orderId);

        var response = await PostCryptoWebhookAsync(new
        {
            id = orderId,
            externalId = App.Go28.Order(orderId).ExternalId,
            status = "Confirmed"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var mail = await ExpectAsync(user.Email!, "Email_PayConfirmed_Subject");
        Assert.Contains(user.ReferenceNumber!, mail.Body);

        await CleanCryptoOrdersAsync(user.Id);
    }

    /// <summary>
    /// A redelivered webhook, a second press in the admin panel, a browser
    /// returning after the webhook has already been through: all three reach a
    /// participant who is confirmed already. The message is sent AFTER the
    /// idempotency guard, so there is no second one.
    /// </summary>
    [Fact]
    public async Task Второ_потвърждаване_не_праща_второ_писмо()
    {
        var user = await NewParticipantAsync();

        using var admin = await SignedInAdminAsync();
        Assert.True((await ConfirmPaymentAsync(admin, user, "IBAN")).Success);
        await ExpectAsync(user.Email!, "Email_PayConfirmed_Subject");

        var subject = Subject("Email_PayConfirmed_Subject");
        Assert.Equal(1, Count(user.Email!, subject));

        var second = await ConfirmPaymentAsync(admin, user, "IBAN");
        Assert.False(second.Success, "Второто потвърждаване трябва да бъде отказано.");

        var seen = await WaitForCountAsync(user.Email!, "Email_PayConfirmed_Subject", 2, seconds: 8);
        Assert.Equal(1, seen);
    }

    // ════════════════════════════════════════════════════════════════════
    // Verification
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Одобрената_верификация_праща_писмо_одобрено()
    {
        var user = await NewParticipantAsync();
        await App.Db.SetVerificationAsync(user.Email!, "Pending", null);

        using var admin = await SignedInAdminAsync();
        var reply = await AdminPostAsync(admin, "ApproveVerification",
            new Dictionary<string, string> { ["userId"] = user.Id });

        Assert.True(reply.Success, reply.Message);

        await ExpectAsync(user.Email!, "Email_VerifApproved_Subject");
        await ExpectNoneAsync(user.Email!, "Email_VerifRejected_Subject", seconds: 3);
    }

    [Fact]
    public async Task Отхвърлената_верификация_праща_писмо_с_причината()
    {
        var user = await NewParticipantAsync();
        await App.Db.SetVerificationAsync(user.Email!, "Pending", null);

        const string reason = "Снимката на документа е нечетима.";

        using var admin = await SignedInAdminAsync();
        var reply = await AdminPostAsync(admin, "RejectVerification", new Dictionary<string, string>
        {
            ["userId"] = user.Id,
            ["reason"] = reason
        });

        Assert.True(reply.Success, reply.Message);

        var mail = await ExpectAsync(user.Email!, "Email_VerifRejected_Subject");

        // A "rejected" message without the reason in it sends the person back to
        // ask why.
        Assert.Contains(reason, mail.Body);
    }

    /// <summary>
    /// A reason shorter than five characters is refused before the status is
    /// touched at all. A message about something that did not happen is worse than
    /// no message.
    /// </summary>
    [Fact]
    public async Task Отказаното_отхвърляне_не_праща_писмо()
    {
        var user = await NewParticipantAsync();
        await App.Db.SetVerificationAsync(user.Email!, "Pending", null);

        using var admin = await SignedInAdminAsync();
        var reply = await AdminPostAsync(admin, "RejectVerification", new Dictionary<string, string>
        {
            ["userId"] = user.Id,
            ["reason"] = "не"
        });

        Assert.False(reply.Success, "Твърде кратка причина трябва да се откаже.");

        await ExpectNoneAsync(user.Email!, "Email_VerifRejected_Subject");

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Pending", after!.VerificationStatus);
    }

    [Fact]
    public async Task Второ_одобряване_не_праща_второ_писмо()
    {
        var user = await NewParticipantAsync();
        await App.Db.SetVerificationAsync(user.Email!, "Pending", null);

        using var admin = await SignedInAdminAsync();
        var form = new Dictionary<string, string> { ["userId"] = user.Id };

        Assert.True((await AdminPostAsync(admin, "ApproveVerification", form)).Success);
        await ExpectAsync(user.Email!, "Email_VerifApproved_Subject");

        await AdminPostAsync(admin, "ApproveVerification", form);

        var seen = await WaitForCountAsync(user.Email!, "Email_VerifApproved_Subject", 2, seconds: 8);
        Assert.Equal(1, seen);
    }

    // ════════════════════════════════════════════════════════════════════
    // A change of status
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Смяната_на_статус_от_панела_праща_писмо()
    {
        var user = await NewParticipantAsync();

        using var admin = await SignedInAdminAsync();
        var form = EditForm(user);
        form["paymentStatus"] = "Confirmed";

        var reply = await AdminPostAsync(admin, "SaveRegistration", form);
        Assert.True(reply.Success, reply.Message);

        var mail = await ExpectAsync(user.Email!, "Email_StatusChanged_Subject");

        Assert.Contains("Pending", mail.Body);
        Assert.Contains("Confirmed", mail.Body);
    }

    /// <summary>
    /// An edit that changes no status at all — a name, a telephone, an
    /// organisation — is no reason for a message. Otherwise every small correction
    /// in the admin panel reaches the participant's mailbox as "your status has
    /// been changed".
    /// </summary>
    [Fact]
    public async Task Редакция_без_смяна_на_статус_не_праща_писмо()
    {
        var user = await NewParticipantAsync();

        using var admin = await SignedInAdminAsync();
        var form = EditForm(user);
        form["organization"] = "Друга организация";
        form["phone"]        = "+359 888 999000";

        var reply = await AdminPostAsync(admin, "SaveRegistration", form);
        Assert.True(reply.Success, reply.Message);

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Друга организация", after!.Workplace);

        await ExpectNoneAsync(user.Email!, "Email_StatusChanged_Subject");
    }

    /// <summary>
    /// Changing the form of participation is a change of status too, and the
    /// message has to show the human-readable name rather than the digit.
    /// </summary>
    [Fact]
    public async Task Смяната_на_формата_на_участие_излиза_с_име_а_не_с_цифра()
    {
        var user = await NewParticipantAsync(partForm: "1");

        using var admin = await SignedInAdminAsync();
        var form = EditForm(user);
        form["participation"] = "3";

        Assert.True((await AdminPostAsync(admin, "SaveRegistration", form)).Success);

        var mail = await ExpectAsync(user.Email!, "Email_StatusChanged_Subject");

        Assert.Contains("Lector / Academic", mail.Body);
        Assert.Contains("Online Participant", mail.Body);
    }

    // ════════════════════════════════════════════════════════════════════
    // A report from the widget: the only message outside the composer
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The bug-report notification does not go through <c>IMailComposer</c> but
    /// straight through <c>EmailSender</c>, to an address hard-coded in
    /// <c>BugReportController</c>. The test does not copy that address but reads
    /// it from the controller itself, so that it cannot start lying when the
    /// address changes.
    /// </summary>
    [Fact]
    public async Task Подаден_сигнал_праща_известие_до_адреса_за_известия()
    {
        var title = $"{Tag}-{Guid.NewGuid():N}"[..24];

        // The widget is visible only to an administrator and the endpoint demands
        // both the role and a token, so the report is submitted from where it is
        // really submitted.
        using var admin = await SignedInAdminAsync();
        var token = await admin.AntiforgeryTokenAsync("/Admin");

        using var response = await admin.Client.PostAsync("/api/bug-reports/submit",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["title"]       = title,
                ["description"] = "Подаден от тест на част 8.",
                ["category"]    = "Bug",
                ["severity"]    = "Low",
                ["pageUrl"]     = "/",
                ["userAgent"]   = "confapp-tests",
                ["__RequestVerificationToken"] = token
            }));

        Assert.True(response.IsSuccessStatusCode, $"Сигналът не беше приет: {response.StatusCode}");
        var id = Json(await response.Content.ReadAsStringAsync()).GetProperty("id").GetInt32();

        var mail = await App.Smtp.WaitForAsync(BugReportNotifyAddress, TimeSpan.FromSeconds(25),
            m => m.Subject.Contains(title, StringComparison.Ordinal));

        Assert.True(mail != null,
            $"До {BugReportNotifyAddress} не дойде известие за сигнал „{title}“.");

        Assert.StartsWith("[Bug Report]", mail!.Subject);
        Assert.Contains("Подаден от тест на част 8.", mail.Body);

        await App.Db.WriteAsync(async db =>
        {
            var row = await db.BugReports.FindAsync(id);
            if (row != null) db.BugReports.Remove(row);
        });
    }

    /// <summary>
    /// The notification goes through the background queue rather than through the
    /// request itself.
    /// <para>
    /// <c>EmailSender.SendAsync</c> used to be awaited inside the handler, with a
    /// 15-second SMTP timeout: an administrator filing a report while the mail
    /// server was slow watched a spinner because of a message that had nothing to
    /// do with their report.
    /// </para>
    /// <para>
    /// The sink deliberately slows its answer down. An instant answer cannot tell
    /// the two apart, which is why the test needs a delay longer than the
    /// handler's patience.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Подаването_на_сигнал_не_чака_пощата()
    {
        var title = $"{Tag}-slow-{Guid.NewGuid():N}"[..24];
        var notify = BugReportNotifyAddress;

        App.Smtp.DelayFor(notify, TimeSpan.FromSeconds(8));

        try
        {
            using var admin = await SignedInAdminAsync();
            var token = await admin.AntiforgeryTokenAsync("/Admin");

            var clock = System.Diagnostics.Stopwatch.StartNew();

            using var response = await admin.Client.PostAsync("/api/bug-reports/submit",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["title"]       = title,
                    ["description"] = "Проверка, че заявката не чака SMTP.",
                    ["category"]    = "Bug",
                    ["severity"]    = "Low",
                    ["pageUrl"]     = "/",
                    ["userAgent"]   = "confapp-tests",
                    ["__RequestVerificationToken"] = token
                }));

            clock.Stop();

            Assert.True(response.IsSuccessStatusCode, $"Сигналът не беше приет: {response.StatusCode}");

            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4),
                $"Заявката трая {clock.Elapsed.TotalSeconds:F1}s — чакала е пощата.");

            var id = Json(await response.Content.ReadAsStringAsync()).GetProperty("id").GetInt32();

            // And the message does go out, only later.
            var mail = await App.Smtp.WaitForAsync(notify, TimeSpan.FromSeconds(40),
                m => m.Subject.Contains(title, StringComparison.Ordinal));

            Assert.True(mail != null, $"Известието за „{title}“ така и не тръгна.");

            await App.Db.WriteAsync(async db =>
            {
                var row = await db.BugReports.FindAsync(id);
                if (row != null) db.BugReports.Remove(row);
            });
        }
        finally
        {
            App.Smtp.StopDelayingFor(notify);
        }
    }

    /// <summary>
    /// The notification address is a <c>private const</c> in the controller.
    /// Reading it by reflection is ugly, but more honest than copying the string:
    /// the test fails when the field disappears instead of going on checking a
    /// dead address.
    /// </summary>
    private static string BugReportNotifyAddress =>
        (string)typeof(ConferenceApp.Controllers.BugReportController)
            .GetField("NotifyEmail", System.Reflection.BindingFlags.NonPublic
                                   | System.Reflection.BindingFlags.Static)!
            .GetRawConstantValue()!;

    // ════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private static async Task RequestLoginCodeAsync(HttpSession session, string email)
    {
        var token = await session.AntiforgeryTokenAsync("/Login");
        var response = await session.PostFormAsync("/Login", new Dictionary<string, string>
        {
            ["Email"] = email,
            ["__RequestVerificationToken"] = token
        });
        response.EnsureSuccessStatusCode();
    }

    private static Task<(bool Success, string Message)> ConfirmPaymentAsync(
        HttpSession admin, ApplicationUser user, string method) =>
        AdminPostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id,
            ["method"] = method
        });

    private async Task<HttpResponseMessage> SendStripeWebhookAsync(string payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/stripe/webhook")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature",
            StripeWebhook.Sign(payload, AppFixture.StripeWebhookSecret));

        using var client = App.NewClient();
        return await client.SendAsync(request);
    }

    private async Task<int> CreateCryptoOrderAsync(string email)
    {
        using var session = App.NewSession();
        await session.LoginParticipantAsync(email);

        var response = await session.PostJsonAsync("/api/crypto/create-order",
            """{"currency":"USDC","network":"ETH","slug":"earlybird"}""");

        response.EnsureSuccessStatusCode();

        return Json(await response.Content.ReadAsStringAsync())
            .GetProperty("orderId").GetInt32();
    }

    private async Task<HttpResponseMessage> PostCryptoWebhookAsync(object body)
    {
        using var client = App.NewClient();
        return await client.PostAsync("/api/crypto/webhook",
            new StringContent(JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8, "application/json"));
    }

    /// <summary>Orders outlive the participant ([D-06]), so the test clears them itself.</summary>
    private Task CleanCryptoOrdersAsync(string userId) =>
        App.Db.WriteAsync(async db =>
        {
            var orders = await db.CryptoOrders.Where(o => o.UserId == userId).ToListAsync();
            db.CryptoOrders.RemoveRange(orders);
        });
}
