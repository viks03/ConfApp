// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text.Json;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Emails;

/// <summary>
/// Part 8: "a failed message is retried".
/// <para>
/// There are three attempts, 3 and 15 seconds apart ([E-02] in
/// <c>MailComposer</c>). Until now only a comment claimed so: the test sink
/// accepted everything, so the retry code never ran at all. Here the sink
/// rejects deliberately, and per address, so that the other tests' mail does not
/// suffer.
/// </para>
/// <para>
/// Failing FOR GOOD is in <see cref="EmailDeliveryFailureTests"/>, against a
/// separately started application: the failure counter is per process, and a
/// message that failed here would leave the Health tab red for every later
/// test.
/// </para>
/// </summary>
public class EmailRetryTests : EmailTestBase
{
    public EmailRetryTests(AppFixture app) : base(app, "mail-rty") { }

    [Fact]
    public async Task Отказано_писмо_се_повтаря_и_минава_на_втория_опит()
    {
        var user = await NewParticipantAsync();
        App.Smtp.FailNextFor(user.Email!, 1);

        using var session = App.NewSession();
        await RequestCodeAsync(session, user.Email!);

        // The recipient gets the message all the same, which is the whole point of
        // retrying.
        var mail = await ExpectAsync(user.Email!, "Email_Otp_Login_Subject");

        var attempts = App.Smtp.AttemptsFor(user.Email!);
        Assert.Equal(2, attempts);

        // And the code inside is still valid rather than spent on the first
        // attempt.
        var otp = (await App.Db.OtpCodesAsync(user.Email!, "Login")).Single(c => !c.IsUsed);
        Assert.Contains(otp.Code, mail.Body);
    }

    /// <summary>
    /// The third attempt is the last one, and here it succeeds. That checks two
    /// things at once: that the 15-second pause does not get the task cancelled
    /// along the way, and that the queue does not block — a message submitted
    /// after the failing one goes out too.
    /// </summary>
    [Fact]
    public async Task Третият_опит_минава_и_опашката_продължава_нататък()
    {
        var slow = await NewParticipantAsync();
        var next = await NewParticipantAsync();

        App.Smtp.FailNextFor(slow.Email!, 2);

        using var first = App.NewSession();
        await RequestCodeAsync(first, slow.Email!);

        using var second = App.NewSession();
        await RequestCodeAsync(second, next.Email!);

        // 3 plus 15 seconds of waiting before the third attempt.
        await ExpectAsync(slow.Email!, "Email_Otp_Login_Subject", seconds: 60);
        Assert.Equal(3, App.Smtp.AttemptsFor(slow.Email!));

        // The single consumer was busy for 18 seconds, but it did not die.
        await ExpectAsync(next.Email!, "Email_Otp_Login_Subject", seconds: 30);
    }

    /// <summary>
    /// An attempt that succeeds after a retry does not count as a failure: Health
    /// has to stay green. The opposite would raise an alarm every time the SMTP
    /// server blinked.
    /// </summary>
    [Fact]
    public async Task Успешното_повторение_не_оцветява_Health_в_червено()
    {
        var user = await NewParticipantAsync();
        App.Smtp.FailNextFor(user.Email!, 1);

        using var session = App.NewSession();
        await RequestCodeAsync(session, user.Email!);
        await ExpectAsync(user.Email!, "Email_Otp_Login_Subject");

        using var admin = await SignedInAdminAsync();
        var queue = await HealthAsync(admin, "emailQueue");

        var status = queue.GetProperty("status").GetString();
        Assert.True(status is "ok" or "warn",
            $"След успешно повторение опашката е „{status}“: {queue.GetProperty("message").GetString()}");
    }

    /// <summary>
    /// A Health tab that counts only the queued tasks says "all is well" precisely
    /// when nothing is going out. [E-02] added the counters; what is checked here
    /// is that they reach the screen, not only the service.
    /// </summary>
    [Fact]
    public async Task Health_показва_изпратени_провалени_и_последен_провал()
    {
        using var admin = await SignedInAdminAsync();
        var queue = await HealthAsync(admin, "emailQueue");

        var labels = queue.GetProperty("details").EnumerateArray()
            .Select(d => d.GetProperty("label").GetString()!)
            .ToList();

        Assert.Contains(labels, l => l.Contains("Изпратени", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(labels, l => l.Contains("Последен провал", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(labels, l => l.Contains("Консуматор", StringComparison.OrdinalIgnoreCase));

        // The consumer has to be running; otherwise everything else in part 8 would
        // have been measuring something else.
        var consumer = queue.GetProperty("details").EnumerateArray()
            .First(d => d.GetProperty("label").GetString()!.Contains("Консуматор", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("работи", consumer.GetProperty("value").GetString());
    }

    /// <summary>
    /// The sent counter goes up with every message that goes out. Without it
    /// "0 / 0" and "running" look the same on a live queue and on a dead one.
    /// </summary>
    [Fact]
    public async Task Броячът_за_изпратени_расте_след_писмо()
    {
        using var admin = await SignedInAdminAsync();

        var before = SucceededFrom(await HealthAsync(admin, "emailQueue"));

        var user = await NewParticipantAsync();
        using var session = App.NewSession();
        await RequestCodeAsync(session, user.Email!);
        await ExpectAsync(user.Email!, "Email_Otp_Login_Subject");

        var after = SucceededFrom(await HealthAsync(admin, "emailQueue"));

        Assert.True(after > before, $"Изпратените не се промениха: {before} → {after}.");
    }

    // ════════════════════════════════════════════════════════════════════

    /// <summary>The first figure on the "sent / failed" row of the Health tab.</summary>
    private static int SucceededFrom(JsonElement queue)
    {
        var value = queue.GetProperty("details").EnumerateArray()
            .First(d => d.GetProperty("label").GetString()!.Contains("Изпратени", StringComparison.OrdinalIgnoreCase))
            .GetProperty("value").GetString()!;

        var match = System.Text.RegularExpressions.Regex.Match(value, @"(?<n>\d+)");
        return match.Success ? int.Parse(match.Groups["n"].Value) : -1;
    }

    private static async Task<JsonElement> HealthAsync(HttpSession admin, string service)
    {
        using var response = await admin.Client.GetAsync($"/Admin?handler=HealthCheck&service={service}");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
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
