// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Emails;

/// <summary>
/// Part 8: "the message is in the user's language".
/// <para>
/// The language is decided in one place, <c>MailContext.CultureFor</c>, which
/// today returns the culture of the CURRENT REQUEST. For the messages a
/// participant triggers themselves that matches their own language. For the ones
/// sent from the admin panel and from the webhooks it does not; see [T-29] and
/// the last section here.
/// </para>
/// <para>
/// Both the subject and the body are checked: a subject in one language with a
/// body in another is just as broken as an entirely wrong language, and the
/// subject alone does not catch it.
/// </para>
/// </summary>
public class EmailLanguageTests : EmailTestBase
{
    public EmailLanguageTests(AppFixture app) : base(app, "mail-lng") { }

    /// <summary>
    /// A string that appears in EVERY message — the bottom line of the frame —
    /// and differs between the two languages; the body's language is read off
    /// from it.
    /// </summary>
    private static string FooterFor(string culture) =>
        Subject("Email_Common_FooterRights", culture);

    private void AssertLanguage(CapturedMail mail, MailKind kind, string culture)
    {
        var other = culture == "bg" ? "en" : "bg";

        Assert.Equal(Subject(MailKinds.SubjectKey(kind), culture).Trim(), mail.Subject.Trim());

        Assert.Contains(FooterFor(culture), mail.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(FooterFor(other), mail.Body, StringComparison.Ordinal);
    }

    // ════════════════════════════════════════════════════════════════════
    // What the participant triggers themselves
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("bg")]
    [InlineData("en")]
    public async Task Кодът_при_регистрация_идва_на_езика_на_формата(string culture)
    {
        var mail = await SampleAsync(MailKind.OtpRegistration, culture);
        AssertLanguage(mail, MailKind.OtpRegistration, culture);
    }

    [Theory]
    [InlineData("bg")]
    [InlineData("en")]
    public async Task Кодът_за_вход_идва_на_езика_на_страницата(string culture)
    {
        var mail = await SampleAsync(MailKind.OtpLogin, culture);
        AssertLanguage(mail, MailKind.OtpLogin, culture);

        // The caption above the code as well, not only the frame; otherwise the
        // test would pass on a body half-assembled in the other language.
        Assert.Contains(Subject("Email_Otp_CodeLabel", culture), mail.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bg")]
    [InlineData("en")]
    public async Task Писмото_за_банков_превод_е_на_езика_на_участника(string culture)
    {
        var mail = await SampleAsync(MailKind.PaymentPending, culture);
        AssertLanguage(mail, MailKind.PaymentPending, culture);

        Assert.Contains(Subject("Email_Common_AmountLabel", culture), mail.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The message is composed on a background task, AFTER the request has ended
    /// and the thread's culture has been reset to the default language. The
    /// translations are therefore read while the request is still running and
    /// handed over ready. This test guards exactly that: switching the language to
    /// English has to survive the queue.
    /// </summary>
    [Fact]
    public async Task Езикът_преживява_фоновата_опашка()
    {
        var mail = await SampleAsync(MailKind.PaymentPending, "en");

        Assert.DoesNotContain(FooterFor("bg"), mail.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(Subject("Email_PayPending_Status", "bg"), mail.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Switching the language midway applies to the NEXT message. This is worth
    /// asserting because the language cookie is read per request rather than
    /// remembered in the profile, so two messages to the same person can be in
    /// different languages — and that is the normal behaviour today.
    /// </summary>
    [Fact]
    public async Task Смяната_на_езика_важи_за_следващото_писмо()
    {
        var user = await NewParticipantAsync();

        using var session = App.NewSession();
        await session.SetLanguageAsync("bg");
        await RequestCodeAsync(session, user.Email!);
        var first = await ExpectAsync(user.Email!, "Email_Otp_Login_Subject", "bg");
        Assert.Contains(FooterFor("bg"), first.Body, StringComparison.Ordinal);

        await session.SetLanguageAsync("en");
        using var resend = await session.PostHandlerAsync("/Verification", "Resend");
        resend.EnsureSuccessStatusCode();

        var second = await ExpectAsync(user.Email!, "Email_Otp_Login_Subject", "en");
        Assert.Contains(FooterFor("en"), second.Body, StringComparison.Ordinal);
    }

    // ════════════════════════════════════════════════════════════════════
    // What someone else triggers — [T-29]
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// [T-29] The four messages sent from the admin panel go out in the
    /// RECIPIENT's language, not in that of the administrator pressing the button.
    /// The administrator here deliberately works in the other language; otherwise
    /// the test cannot tell the two apart.
    /// </summary>
    [Theory]
    [InlineData(MailKind.PaymentConfirmed,     "en")]
    [InlineData(MailKind.PaymentConfirmed,     "bg")]
    [InlineData(MailKind.VerificationApproved, "en")]
    [InlineData(MailKind.VerificationApproved, "bg")]
    [InlineData(MailKind.VerificationRejected, "en")]
    [InlineData(MailKind.VerificationRejected, "bg")]
    [InlineData(MailKind.StatusChanged,        "en")]
    [InlineData(MailKind.StatusChanged,        "bg")]
    public async Task Писмата_от_панела_излизат_на_езика_на_получателя(MailKind kind, string culture)
    {
        Assert.True(MailKinds.TriggeredByAdmin(kind));

        // SampleAsync records the language on the participant and signs the
        // administrator in under the other one; see EmailTestBase.
        var mail = await SampleAsync(kind, culture);

        AssertLanguage(mail, kind, culture);
    }

    /// <summary>
    /// The most costly of the four: the rejection reason is the sentence that
    /// makes someone upload a document again. In a language they cannot read it is
    /// of no use.
    /// </summary>
    [Fact]
    public async Task Причината_за_отхвърляне_стига_на_езика_на_участника()
    {
        var user = await SpeakerOfAsync("en");
        await App.Db.SetVerificationAsync(user.Email!, "Pending", null);

        const string reason = "The photo of the document is unreadable.";

        using var admin = await SignedInAdminAsync("bg");
        var reply = await AdminPostAsync(admin, "RejectVerification", new Dictionary<string, string>
        {
            ["userId"] = user.Id,
            ["reason"] = reason
        });
        Assert.True(reply.Success, reply.Message);

        var mail = await ExpectAsync(user.Email!, "Email_VerifRejected_Subject", "en");

        Assert.Contains(reason, mail.Body, StringComparison.Ordinal);
        Assert.Contains(Subject("Email_VerifRejected_ReasonLabel", "en"), mail.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(FooterFor("bg"), mail.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A webhook has neither a user request nor a language cookie. That used to
    /// mean a message in Bulgarian for everyone; the recipient's recorded language
    /// now works from there too.
    /// </summary>
    [Theory]
    [InlineData("bg")]
    [InlineData("en")]
    public async Task Писмото_от_webhook_е_на_езика_на_получателя(string culture)
    {
        var user = await SpeakerOfAsync(culture);

        var payload = StripeWebhook.CheckoutSessionCompleted(
            $"cs_test_{Guid.NewGuid():N}"[..30], user.Id, 6000, user.Email);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/stripe/webhook")
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature",
            StripeWebhook.Sign(payload, AppFixture.StripeWebhookSecret));

        using var client = App.NewClient();
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var mail = await ExpectAsync(user.Email!, "Email_PayConfirmed_Subject", culture);
        Assert.Contains(FooterFor(culture), mail.Body, StringComparison.Ordinal);
    }

    // ════════════════════════════════════════════════════════════════════
    // Where the recorded language comes from
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The language of the form is recorded at registration: it is the only thing
    /// known about a person before they have ever signed in.
    /// </summary>
    [Theory]
    [InlineData("bg")]
    [InlineData("en")]
    public async Task Регистрацията_запомня_езика_на_формата(string culture)
    {
        var email = NewEmail();

        using var session = App.NewSession();
        await session.SetLanguageAsync(culture);
        await Auth.RegistrationFlow.RunAsync(session, new Auth.RegistrationForm { Email = email });

        var user = await App.Db.FindUserAsync(email);
        Assert.NotNull(user);
        Assert.Equal(culture, user!.PreferredLanguage);
    }

    /// <summary>
    /// Switching from the top bar updates the record; otherwise someone who
    /// registered in Bulgarian and moved to English would go on receiving
    /// Bulgarian mail for ever.
    /// </summary>
    [Fact]
    public async Task Смяната_от_лентата_обновява_записания_език()
    {
        var user = await SpeakerOfAsync("bg");

        using var session = await SignedInAsync(user);
        await session.SetLanguageAsync("en");

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("en", after!.PreferredLanguage);

        // And the next message from the admin panel is already in the new
        // language.
        using var admin = await SignedInAdminAsync("bg");
        var reply = await AdminPostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id,
            ["method"] = "IBAN"
        });
        Assert.True(reply.Success, reply.Message);

        var mail = await ExpectAsync(user.Email!, "Email_PayConfirmed_Subject", "en");
        Assert.Contains(FooterFor("en"), mail.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A language switch by a visitor who is not signed in must not blow up:
    /// there is nobody to record it against, and the cookie works regardless.
    /// </summary>
    [Fact]
    public async Task Смяната_без_влизане_не_чупи_нищо()
    {
        using var session = App.NewSession();
        await session.SetLanguageAsync("en");

        using var response = await session.Client.GetAsync("/");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Rows that predate the field hold <c>null</c>. For them the old behaviour
    /// stands — the culture of the request — rather than a message in no language
    /// at all, or an exception on the background queue where nobody would see
    /// it.
    /// </summary>
    [Fact]
    public async Task Участник_без_записан_език_пада_на_езика_на_заявката()
    {
        var user = await NewParticipantAsync();
        await App.Db.SetPreferredLanguageAsync(user.Email!, null);

        using var admin = await SignedInAdminAsync("en");
        var reply = await AdminPostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id,
            ["method"] = "IBAN"
        });
        Assert.True(reply.Success, reply.Message);

        var mail = await ExpectAsync(user.Email!, "Email_PayConfirmed_Subject", "en");
        Assert.Contains(FooterFor("en"), mail.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The field is a string in the database. A row touched from outside — by an
    /// import, a botched migration, or by hand — must not throw a
    /// <c>CultureNotFoundException</c> on the background task and quietly swallow
    /// the message.
    /// </summary>
    [Theory]
    [InlineData("klingon")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Непознат_записан_език_не_спира_писмото(string stored)
    {
        var user = await NewParticipantAsync();
        await App.Db.SetPreferredLanguageAsync(user.Email!, stored);

        using var admin = await SignedInAdminAsync("bg");
        var reply = await AdminPostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id,
            ["method"] = "IBAN"
        });
        Assert.True(reply.Success, reply.Message);

        // It falls back to the language of the request, but the MESSAGE GOES OUT,
        // which is what matters here.
        await ExpectAsync(user.Email!, "Email_PayConfirmed_Subject", "bg");
    }

    /// <summary>
    /// A browser sending a full culture code gives "bg-BG" rather than "bg". What
    /// is recorded has to be the short form; otherwise matching against the
    /// supported languages catches nothing.
    /// </summary>
    [Theory]
    [InlineData("bg-BG", "bg")]
    [InlineData("en-US", "en")]
    [InlineData("EN",    "en")]
    [InlineData("fr",    null)]
    [InlineData(null,    null)]
    public void Езикът_се_свежда_до_един_от_поддържаните(string? input, string? expected)
    {
        Assert.Equal(expected, ConferenceApp.Services.Email.MailContext.Normalize(input));
    }

    // ════════════════════════════════════════════════════════════════════
    // The translations themselves
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A key missing from one of the files means a message that goes out half in
    /// the other language: .NET falls back to the neutral resource silently, with
    /// no error anywhere.
    /// </summary>
    [Theory, MemberData(nameof(EmailLinkTests.Kinds), MemberType = typeof(EmailLinkTests))]
    public void Темата_на_всеки_вид_я_има_и_на_двата_езика(MailKind kind)
    {
        var key = MailKinds.SubjectKey(kind);

        var bg = Subject(key, "bg");
        var en = Subject(key, "en");

        Assert.False(string.IsNullOrWhiteSpace(bg), $"{key}: празна тема на български.");
        Assert.False(string.IsNullOrWhiteSpace(en), $"{key}: празна тема на английски.");

        // Identical subjects would mean an untranslated key; for these six that is
        // not the case.
        Assert.NotEqual(bg, en);
    }

    private static async Task RequestCodeAsync(HttpSession session, string email)
    {
        var token = await session.AntiforgeryTokenAsync("/Login");
        var response = await session.PostFormAsync("/Login", new Dictionary<string, string>
        {
            ["Email"] = email,
            ["__RequestVerificationToken"] = token
        });
        response.EnsureSuccessStatusCode();
    }
}
