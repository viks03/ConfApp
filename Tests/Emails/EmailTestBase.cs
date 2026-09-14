// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Emails;

/// <summary>
/// The common ground of part 8. The application sends six kinds of automatic
/// message through <c>IMailComposer</c> and two more straight through
/// <c>EmailSender</c> — an invitation from the admin panel and a bug-report
/// notification. Part 8 works from the event that produces a message rather than
/// from the method that composes it.
/// <para>
/// The subjects are read from <c>Resources/EmailMessages.*.resx</c> rather than
/// copied in here: a copied string checks only that both copies were made the
/// same way, while a key checks that the message really is the one that was
/// supposed to go out.
/// </para>
/// </summary>
[Collection(AppCollection.Name)]
public abstract class EmailTestBase : IAsyncLifetime
{
    protected readonly AppFixture App;
    private readonly string _tag;
    private readonly List<string> _users = new();

    protected EmailTestBase(AppFixture app, string tag)
    {
        App  = app;
        _tag = tag;
    }

    public virtual Task InitializeAsync()
    {
        App.Smtp.Clear();
        return Task.CompletedTask;
    }

    public virtual async Task DisposeAsync()
    {
        foreach (var email in _users)
        {
            // A rejection left in place would fail the mail of any later test that
            // happened to use the same address.
            App.Smtp.StopFailingFor(email);

            await App.Db.DeleteParticipantAsync(email);
            await App.Db.DeleteOtpCodesAsync(email);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Participants
    // ════════════════════════════════════════════════════════════════════

    /// <summary>An address the cleanup after the test will find and clear away.</summary>
    protected string NewEmail()
    {
        var email = $"{_tag}-{Guid.NewGuid():N}@example.test";
        _users.Add(email);
        return email;
    }

    protected async Task<ApplicationUser> NewParticipantAsync(
        string partForm = "1", string paymentStatus = "Pending") =>
        await App.Db.CreateParticipantAsync(NewEmail(), partForm, paymentStatus);

    protected async Task<HttpSession> SignedInAsync(ApplicationUser user)
    {
        var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);
        return session;
    }

    /// <summary>A participant who uses the site in this language — [T-29].</summary>
    protected async Task<ApplicationUser> SpeakerOfAsync(
        string culture, string partForm = "1", string paymentStatus = "Pending")
    {
        var user = await NewParticipantAsync(partForm, paymentStatus);
        await App.Db.SetPreferredLanguageAsync(user.Email!, culture);
        return user;
    }

    /// <summary>Whichever of the two languages this one is not.</summary>
    protected static string Other(string culture) => culture == "bg" ? "en" : "bg";

    protected async Task<HttpSession> SignedInAdminAsync(string culture = "bg")
    {
        var session = App.NewSession();
        await session.LoginAdminAsync(App.Credentials.AdminEmail, App.Credentials.AdminPassword);
        await session.SetLanguageAsync(culture);
        return session;
    }

    // ════════════════════════════════════════════════════════════════════
    // The messages
    // ════════════════════════════════════════════════════════════════════

    /// <summary>The subject of one kind of message, as it stands in the resx.</summary>
    protected static string Subject(string key, string culture = "bg") =>
        Resx.Value("EmailMessages", key, culture);

    /// <summary>
    /// Waits for a message with EXACTLY this subject. "Any message at all to this
    /// address" does not do: every participant is also sent a sign-in code, so
    /// such a test would pass even when the message in question never went out.
    /// </summary>
    protected async Task<CapturedMail> ExpectAsync(
        string email, string subjectKey, string culture = "bg", int seconds = 30)
    {
        var subject = Subject(subjectKey, culture);

        var mail = await App.Smtp.WaitForAsync(email, TimeSpan.FromSeconds(seconds),
            m => m.Subject.Trim() == subject.Trim());

        Assert.True(mail != null,
            $"До {email} не дойде писмо с тема „{subject}“ за {seconds}s. " +
            $"Дошло е: {Describe(email)}");

        return mail!;
    }

    /// <summary>
    /// Asserts that a message with this subject does NOT go out. The wait is
    /// essential: sending goes through a background queue, so a check made
    /// immediately after the action passes even when the message is merely still
    /// on its way.
    /// </summary>
    protected async Task ExpectNoneAsync(
        string email, string subjectKey, string culture = "bg", int seconds = 8)
    {
        var subject = Subject(subjectKey, culture);

        var mail = await App.Smtp.WaitForAsync(email, TimeSpan.FromSeconds(seconds),
            m => m.Subject.Trim() == subject.Trim());

        Assert.True(mail == null,
            $"До {email} тръгна писмо „{subject}“, а не е трябвало. Дошло е: {Describe(email)}");
    }

    /// <summary>
    /// Waits for AT LEAST that many messages with this subject. It is needed where
    /// the second message is the one that matters — a new code, a second
    /// confirmation — because <see cref="ExpectAsync"/> would return the first and
    /// the test would pass without the second ever going out.
    /// </summary>
    protected async Task<int> WaitForCountAsync(
        string email, string subjectKey, int atLeast, string culture = "bg", int seconds = 30)
    {
        var subject = Subject(subjectKey, culture).Trim();
        var deadline = DateTime.UtcNow.AddSeconds(seconds);

        while (DateTime.UtcNow < deadline)
        {
            var seen = Count(email, subject);
            if (seen >= atLeast) return seen;
            await Task.Delay(200);
        }

        return Count(email, subject);
    }

    protected int Count(string email, string subject) =>
        App.Smtp.All.Count(m =>
            m.To.Contains(email, StringComparison.OrdinalIgnoreCase)
            && m.Subject.Trim() == subject.Trim());

    /// <summary>What this address received at all, for the failure message.</summary>
    protected string Describe(string email)
    {
        var subjects = App.Smtp.All
            .Where(m => m.To.Contains(email, StringComparison.OrdinalIgnoreCase))
            .Select(m => $"„{m.Subject}“")
            .ToList();

        return subjects.Count == 0 ? "нищо" : string.Join(", ", subjects);
    }

    protected string Tag => _tag;

    // ════════════════════════════════════════════════════════════════════
    // A sample of each kind
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Triggers one message of a given kind and returns it. The event is triggered
    /// from where a person triggers it: through the same pages and handlers that
    /// are exercised in <see cref="EmailTriggerTests"/>.
    /// <para>
    /// <paramref name="culture"/> is the RECIPIENT's language and is set where
    /// that language lives: for the messages a participant triggers themselves, as
    /// the language of their session; for those from the admin panel, as the
    /// participant's recorded language, with an administrator working in the OTHER
    /// language. Every sample therefore goes through [T-29] rather than around it.
    /// </para>
    /// </summary>
    protected async Task<CapturedMail> SampleAsync(MailKind kind, string culture = "bg")
    {
        var subjectKey = MailKinds.SubjectKey(kind);

        switch (kind)
        {
            case MailKind.OtpRegistration:
            {
                var email = NewEmail();
                using var session = App.NewSession();
                await session.SetLanguageAsync(culture);
                await Auth.RegistrationFlow.RunAsync(session,
                    new Auth.RegistrationForm { Email = email });

                return await ExpectAsync(email, subjectKey, culture);
            }

            case MailKind.OtpLogin:
            {
                var user = await NewParticipantAsync();
                using var session = App.NewSession();
                await session.SetLanguageAsync(culture);

                var token = await session.AntiforgeryTokenAsync("/Login");
                var response = await session.PostFormAsync("/Login", new Dictionary<string, string>
                {
                    ["Email"] = user.Email!,
                    ["__RequestVerificationToken"] = token
                });
                response.EnsureSuccessStatusCode();

                return await ExpectAsync(user.Email!, subjectKey, culture);
            }

            case MailKind.PaymentPending:
            {
                var user = await NewParticipantAsync();
                using var session = await SignedInAsync(user);
                await session.SetLanguageAsync(culture);

                using var response = await session.PostHandlerAsync("/Payment/earlybird", "SubmitIban");
                response.EnsureSuccessStatusCode();

                return await ExpectAsync(user.Email!, subjectKey, culture);
            }

            case MailKind.PaymentConfirmed:
            {
                var user = await SpeakerOfAsync(culture);
                using var admin = await SignedInAdminAsync(Other(culture));

                var reply = await AdminPostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
                {
                    ["userId"] = user.Id,
                    ["method"] = "IBAN"
                });
                Assert.True(reply.Success, reply.Message);

                return await ExpectAsync(user.Email!, subjectKey, culture);
            }

            case MailKind.VerificationApproved:
            {
                var user = await SpeakerOfAsync(culture);
                await App.Db.SetVerificationAsync(user.Email!, "Pending", null);

                using var admin = await SignedInAdminAsync(Other(culture));
                var reply = await AdminPostAsync(admin, "ApproveVerification",
                    new Dictionary<string, string> { ["userId"] = user.Id });
                Assert.True(reply.Success, reply.Message);

                return await ExpectAsync(user.Email!, subjectKey, culture);
            }

            case MailKind.VerificationRejected:
            {
                var user = await SpeakerOfAsync(culture);
                await App.Db.SetVerificationAsync(user.Email!, "Pending", null);

                using var admin = await SignedInAdminAsync(Other(culture));
                var reply = await AdminPostAsync(admin, "RejectVerification", new Dictionary<string, string>
                {
                    ["userId"] = user.Id,
                    ["reason"] = "Документът не се чете."
                });
                Assert.True(reply.Success, reply.Message);

                return await ExpectAsync(user.Email!, subjectKey, culture);
            }

            case MailKind.StatusChanged:
            {
                var user = await SpeakerOfAsync(culture);
                using var admin = await SignedInAdminAsync(Other(culture));

                var form = EditForm(user);
                form["paymentStatus"] = "Confirmed";

                var reply = await AdminPostAsync(admin, "SaveRegistration", form);
                Assert.True(reply.Success, reply.Message);

                return await ExpectAsync(user.Email!, subjectKey, culture);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Няма как да се предизвика.");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // The admin panel
    // ════════════════════════════════════════════════════════════════════

    protected static async Task<(bool Success, string Message)> AdminPostAsync(
        HttpSession session, string handler, Dictionary<string, string> fields)
    {
        using var response = await session.PostHandlerAsync("/Admin", handler, fields);
        var raw = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrWhiteSpace(raw))
            return (response.IsSuccessStatusCode, string.Empty);

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var success = root.TryGetProperty("success", out var s)
                ? s.ValueKind == System.Text.Json.JsonValueKind.True
                : response.IsSuccessStatusCode;

            var message = root.TryGetProperty("message", out var m)
                          && m.ValueKind == System.Text.Json.JsonValueKind.String
                ? m.GetString()!
                : string.Empty;

            return (success, message);
        }
        catch (System.Text.Json.JsonException)
        {
            // A page instead of JSON means a redirect to the sign-in page, which is
            // not a success.
            return (false, "Отговорът не е JSON — най-често значи пренасочване към входа.");
        }
    }

    /// <summary>The form on the Participants tab: every field, as the panel sends them.</summary>
    protected static Dictionary<string, string> EditForm(ApplicationUser user) => new()
    {
        ["id"]                 = user.Id,
        ["firstName"]          = user.FirstName ?? string.Empty,
        ["lastName"]           = user.LastName ?? string.Empty,
        ["age"]                = user.Age.ToString(),
        ["phone"]              = user.PhoneNumber ?? string.Empty,
        ["academicTitle"]      = user.AcademicTitle ?? string.Empty,
        ["organization"]       = user.Workplace ?? string.Empty,
        ["participation"]      = user.PartForm ?? "1",
        ["isForeigner"]        = user.IsForeigner ? "true" : "false",
        ["emailConfirmed"]     = user.EmailConfirmed ? "true" : "false",
        ["paymentStatus"]      = user.PaymentStatus ?? "Pending",
        ["verificationStatus"] = user.VerificationStatus ?? "None"
    };
}
