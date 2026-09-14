// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Auth;

/// <summary>
/// Part 3, signing in with a one-time code. Participants have no password:
/// everything goes through the code in <c>OtpCodes</c>, the same way a person
/// goes through it.
/// </summary>
[Collection(AppCollection.Name)]
public class OtpLoginTests : IAsyncLifetime
{
    private readonly AppFixture _app;
    private readonly List<string> _created = new();

    public OtpLoginTests(AppFixture app) => _app = app;

    public Task InitializeAsync()
    {
        _app.Smtp.Clear();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var email in _created)
        {
            await _app.Db.DeleteParticipantAsync(email);
            await _app.Db.DeleteOtpCodesAsync(email);
        }
    }

    private async Task<ConferenceApp.Models.ApplicationUser> NewParticipantAsync(string tag)
    {
        var email = $"otp-{tag}-{Guid.NewGuid():N}@example.test";
        _created.Add(email);
        return await _app.Db.CreateParticipantAsync(email);
    }

    /// <summary>Asks for a code through /Login and returns the page it lands on.</summary>
    private static async Task<string> RequestCodeAsync(HttpSession session, string email)
    {
        var token = await session.AntiforgeryTokenAsync("/Login");
        var response = await session.PostFormAsync("/Login", new Dictionary<string, string>
        {
            ["Email"] = email,
            ["__RequestVerificationToken"] = token
        });
        return await response.ReadPageAsync();
    }

    /// <summary>Enters a code on /Verification and returns what comes back.</summary>
    private static async Task<HttpResponseMessage> SubmitCodeAsync(HttpSession session, string code)
    {
        var token = await session.AntiforgeryTokenAsync("/Verification");
        return await session.PostFormAsync("/Verification", new Dictionary<string, string>
        {
            ["VerificationCode"] = code,
            ["__RequestVerificationToken"] = token
        });
    }

    // ════════════════════════════════════════════════════════════════════
    // The happy path
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Кодът_от_писмото_съвпада_с_кода_в_базата_и_пуска_вътре()
    {
        var user = await NewParticipantAsync("happy");
        var auditFrom = await _app.Db.LastAuditIdAsync();

        using var session = _app.NewSession();
        await RequestCodeAsync(session, user.Email!);

        var otp = await _app.Db.WaitForOtpAsync(user.Email!, "Login", TimeSpan.FromSeconds(15));
        Assert.NotNull(otp);
        Assert.True(otp!.ExpirationTime > DateTime.UtcNow, "Кодът се ражда изтекъл.");

        // The second source is the message. The code in it must be the same one.
        var mail = await _app.Smtp.WaitForAsync(user.Email!, TimeSpan.FromSeconds(20));
        Assert.NotNull(mail);
        Assert.Contains(otp.Code, mail!.Body);

        await SubmitCodeAsync(session, otp.Code);

        Assert.True(await session.IsSignedInAsync());

        var used = await _app.Db.OtpCodesAsync(user.Email!, "Login");
        Assert.True(used.Single().IsUsed, "Използваният код не е отбелязан като използван.");

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "Login OTP Sent" && a.UserEmail == user.Email);
        Assert.Contains(audit, a => a.Action == "Login" && a.UserEmail == user.Email);
    }

    [Fact]
    public async Task Влизането_с_код_води_в_профила_а_не_в_готово()
    {
        var user = await NewParticipantAsync("dest");

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        using var client = session.NoRedirectClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Profile")).StatusCode);

        // /Done is a step of registration only; someone who signed in with a code
        // never sees it.
        var done = await client.GetAsync("/Done");
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        Assert.Equal("/Profile", done.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Подслушан_код_не_върши_работа_в_чужда_сесия()
    {
        var user = await NewParticipantAsync("reuse");

        using var owner = _app.NewSession();
        await RequestCodeAsync(owner, user.Email!);
        var otp = await _app.Db.WaitForOtpAsync(user.Email!, "Login", TimeSpan.FromSeconds(15));
        await SubmitCodeAsync(owner, otp!.Code);
        Assert.True(await owner.IsSignedInAsync());
        Assert.True((await _app.Db.OtpCodesAsync(user.Email!, "Login")).Single().IsUsed);

        // Someone else has seen the code and tries to sign in with it. To reach
        // the code screen at all they have to ask for a code themselves, and that
        // puts out the old one.
        using var stranger = _app.NewSession();
        await RequestCodeAsync(stranger, user.Email!);

        var response = await SubmitCodeAsync(stranger, otp.Code);

        Assert.False(await stranger.IsSignedInAsync());
        Assert.Contains(Resx.Prefix("Pages.Verification", "Error_InvalidCodeWithAttempts"),
            await response.ReadPageAsync());

        // The first code stays used; it has not come back to life.
        var codes = await _app.Db.OtpCodesAsync(user.Email!, "Login");
        Assert.True(codes.First(c => c.Code == otp.Code).IsUsed);
    }

    // ════════════════════════════════════════════════════════════════════
    // An expired code and a wrong one
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Изтекъл_код_не_влиза()
    {
        var user = await NewParticipantAsync("expired");

        using var session = _app.NewSession();
        await RequestCodeAsync(session, user.Email!);

        var otp = await _app.Db.WaitForOtpAsync(user.Email!, "Login", TimeSpan.FromSeconds(15));
        Assert.NotNull(otp);

        await _app.Db.ExpireLastOtpAsync(user.Email!, "Login");

        var response = await SubmitCodeAsync(session, otp!.Code);

        Assert.False(await session.IsSignedInAsync());
        Assert.Contains(Resx.Value("Pages.Verification", "Error_CodeExpired"),
            await response.ReadPageAsync());

        // The code stays unused, because it was not accepted.
        Assert.False((await _app.Db.OtpCodesAsync(user.Email!, "Login")).Single().IsUsed);
    }

    [Fact]
    public async Task Изтекъл_код_не_отнема_опит_от_трите()
    {
        // A user who was distracted for more than 15 minutes has not guessed
        // wrong, so they should not collect strikes towards the lockout.
        var user = await NewParticipantAsync("expired-strike");

        using var session = _app.NewSession();
        await RequestCodeAsync(session, user.Email!);
        var otp = await _app.Db.WaitForOtpAsync(user.Email!, "Login", TimeSpan.FromSeconds(15));
        await _app.Db.ExpireLastOtpAsync(user.Email!, "Login");

        for (var attempt = 0; attempt < 3; attempt++)
            await SubmitCodeAsync(session, otp!.Code);

        Assert.Null(await _app.Db.LockoutEndAsync(user.Email!));
    }

    [Fact]
    public async Task Грешен_код_не_влиза_и_казва_колко_опита_остават()
    {
        var user = await NewParticipantAsync("wrong");
        var auditFrom = await _app.Db.LastAuditIdAsync();

        using var session = _app.NewSession();
        await RequestCodeAsync(session, user.Email!);
        var otp = await _app.Db.WaitForOtpAsync(user.Email!, "Login", TimeSpan.FromSeconds(15));

        var response = await SubmitCodeAsync(session, WrongCode(otp!.Code));
        var html = await response.ReadPageAsync();

        Assert.False(await session.IsSignedInAsync());
        Assert.Contains(Resx.Prefix("Pages.Verification", "Error_InvalidCodeWithAttempts"), html);
        Assert.Contains("2", html);

        // The correct code still works: a wrong attempt does not burn it.
        await SubmitCodeAsync(session, otp.Code);
        Assert.True(await session.IsSignedInAsync());

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "Verification Failed" && a.UserEmail == user.Email);
    }

    // ════════════════════════════════════════════════════════════════════
    // Lockout
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Три_грешни_кода_заключват_акаунта()
    {
        var user = await NewParticipantAsync("lock");

        using var session = _app.NewSession();
        await RequestCodeAsync(session, user.Email!);
        var otp = await _app.Db.WaitForOtpAsync(user.Email!, "Login", TimeSpan.FromSeconds(15));
        var wrong = WrongCode(otp!.Code);

        for (var attempt = 0; attempt < 3; attempt++)
            await SubmitCodeAsync(session, wrong);

        var lockedUntil = await _app.Db.LockoutEndAsync(user.Email!);
        Assert.NotNull(lockedUntil);
        Assert.True(lockedUntil > DateTimeOffset.UtcNow.AddHours(11),
            $"Заключването е до {lockedUntil}, а трябва да е около 12 часа напред.");

        // Even the correct code no longer helps.
        var response = await SubmitCodeAsync(session, otp.Code);
        Assert.False(await session.IsSignedInAsync());
        Assert.Contains(Resx.Value("Pages.Verification", "Error_AccountLocked"),
            await response.ReadPageAsync());
    }

    [Fact]
    public async Task Заключен_акаунт_не_получава_нов_код_от_входа()
    {
        var user = await NewParticipantAsync("locked-login");

        using var session = _app.NewSession();
        await RequestCodeAsync(session, user.Email!);
        var otp = await _app.Db.WaitForOtpAsync(user.Email!, "Login", TimeSpan.FromSeconds(15));
        var wrong = WrongCode(otp!.Code);

        for (var attempt = 0; attempt < 3; attempt++)
            await SubmitCodeAsync(session, wrong);

        Assert.NotNull(await _app.Db.LockoutEndAsync(user.Email!));

        var codesBefore = (await _app.Db.OtpCodesAsync(user.Email!)).Count;
        _app.Smtp.Clear();

        using var fresh = _app.NewSession();
        var html = await RequestCodeAsync(fresh, user.Email!);

        Assert.Contains(Resx.Value("Pages.Login", "Error_AccountLocked"), html);
        Assert.Equal(codesBefore, (await _app.Db.OtpCodesAsync(user.Email!)).Count);
        Assert.Null(await _app.Smtp.WaitForAsync(user.Email!, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Заключен_акаунт_не_получава_нов_код_и_от_бутона_за_повторно_пращане()
    {
        var user = await NewParticipantAsync("locked-resend");

        using var session = _app.NewSession();
        await RequestCodeAsync(session, user.Email!);
        var otp = await _app.Db.WaitForOtpAsync(user.Email!, "Login", TimeSpan.FromSeconds(15));
        var wrong = WrongCode(otp!.Code);

        for (var attempt = 0; attempt < 3; attempt++)
            await SubmitCodeAsync(session, wrong);

        var codesBefore = (await _app.Db.OtpCodesAsync(user.Email!)).Count;
        _app.Smtp.Clear();

        var response = await session.PostHandlerAsync("/Verification", "Resend");
        var html = await response.ReadPageAsync();

        Assert.Contains(Resx.Value("Pages.Verification", "Error_AccountLocked"), html);
        Assert.Equal(codesBefore, (await _app.Db.OtpCodesAsync(user.Email!)).Count);
        Assert.Null(await _app.Smtp.WaitForAsync(user.Email!, TimeSpan.FromSeconds(5)));
    }

    // ════════════════════════════════════════════════════════════════════
    // Asking for a code over and over
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Четвъртото_искане_на_код_за_половин_час_се_отказва()
    {
        var user = await NewParticipantAsync("throttle");

        using var session = _app.NewSession();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var page = await RequestCodeAsync(session, user.Email!);
            Assert.DoesNotContain(Resx.Value("Pages.Login", "Error_TooManyEmails"), page);
        }

        Assert.Equal(3, (await _app.Db.OtpCodesAsync(user.Email!, "Login")).Count);

        var blocked = await RequestCodeAsync(session, user.Email!);
        Assert.Contains(Resx.Value("Pages.Login", "Error_TooManyEmails"), blocked);
        Assert.Equal(3, (await _app.Db.OtpCodesAsync(user.Email!, "Login")).Count);

        // The window slides: once the older codes fall outside the 30 minutes, a
        // new code is issued again.
        await _app.Db.AgeOtpCodesAsync(user.Email!, TimeSpan.FromMinutes(31));

        var again = await RequestCodeAsync(session, user.Email!);
        Assert.DoesNotContain(Resx.Value("Pages.Login", "Error_TooManyEmails"), again);
        Assert.Equal(4, (await _app.Db.OtpCodesAsync(user.Email!, "Login")).Count);
    }

    [Fact]
    public async Task Шейсетте_секунди_между_два_кода_ги_спазва_само_браузърът()
    {
        // [T-10] /Verification shows a countdown and keeps the button disabled for
        // 60 seconds. The server, however, asks nothing about those 60 seconds; it
        // sees only the ceiling of three codes per 30 minutes. This test pins down
        // the gap between what the page promises and what the server enforces.
        var user = await NewParticipantAsync("cooldown");

        using var session = _app.NewSession();
        var page = await RequestCodeAsync(session, user.Email!);

        var first = await _app.Db.WaitForOtpAsync(user.Email!, "Login", TimeSpan.FromSeconds(15));
        Assert.NotNull(first);

        // The page tells the browser to wait.
        Assert.True(CooldownOnPage(page) > 0,
            "Страницата не показа обратно броене — предпоставката на теста отпада.");

        // The server issues a new code straight away.
        var resend = await session.PostHandlerAsync("/Verification", "Resend");
        Assert.Contains(Resx.Value("Pages.Verification", "Success_CodeResent"),
            await resend.ReadPageAsync());

        var second = await _app.Db.WaitForOtpAsync(user.Email!, "Login", TimeSpan.FromSeconds(15));
        Assert.NotNull(second);
        Assert.NotEqual(first!.Id, second!.Id);
        Assert.True((second.CreatedAt - first.CreatedAt).TotalSeconds < 60,
            "Двата кода са на повече от 60 секунди — тестът не е измерил каквото твърди.");
    }

    /// <summary>The seconds of the countdown, as rendered into the button.</summary>
    private static int CooldownOnPage(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html, "id=\"resendTimer\">(?<n>\\d+)<");
        return match.Success ? int.Parse(match.Groups["n"].Value) : 0;
    }

    [Fact]
    public async Task Нов_код_обезсилва_стария()
    {
        var user = await NewParticipantAsync("supersede");

        using var session = _app.NewSession();
        await RequestCodeAsync(session, user.Email!);
        var first = await _app.Db.WaitForOtpAsync(user.Email!, "Login", TimeSpan.FromSeconds(15));

        var resend = await session.PostHandlerAsync("/Verification", "Resend");
        Assert.Contains(Resx.Value("Pages.Verification", "Success_CodeResent"),
            await resend.ReadPageAsync());

        var second = await _app.Db.WaitForOtpAsync(user.Email!, "Login", TimeSpan.FromSeconds(15));
        Assert.NotEqual(first!.Id, second!.Id);

        // The old one is marked used the moment the new one is issued.
        Assert.True((await _app.Db.OtpCodesAsync(user.Email!, "Login"))
            .First(c => c.Id == first.Id).IsUsed);

        var response = await SubmitCodeAsync(session, first.Code);
        Assert.False(await session.IsSignedInAsync());
        Assert.Contains(Resx.Prefix("Pages.Verification", "Error_InvalidCodeWithAttempts"),
            await response.ReadPageAsync());

        await SubmitCodeAsync(session, second.Code);
        Assert.True(await session.IsSignedInAsync());
    }

    // ════════════════════════════════════════════════════════════════════
    // An unknown address
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Несъществуващ_имейл_не_ражда_код()
    {
        var email = $"nobody-{Guid.NewGuid():N}@example.test";
        var auditFrom = await _app.Db.LastAuditIdAsync();

        using var session = _app.NewSession();
        var html = await RequestCodeAsync(session, email);

        Assert.Contains(Resx.Value("Pages.Login", "Error_UserNotFound"), html);
        Assert.Empty(await _app.Db.OtpCodesAsync(email));
        Assert.Null(await _app.Smtp.WaitForAsync(email, TimeSpan.FromSeconds(5)));

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "Login Failed");
    }

    [Fact]
    public async Task Празен_имейл_се_отказва()
    {
        using var session = _app.NewSession();
        var html = await RequestCodeAsync(session, "   ");

        Assert.Contains(Resx.Value("Pages.Login", "Error_EmailRequired"), html);
    }

    [Fact]
    public async Task Верификация_без_започнат_вход_праща_обратно_към_входа()
    {
        using var session = _app.NewSession();
        using var client = session.NoRedirectClient();

        var response = await client.GetAsync("/Verification");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Код_с_грешна_дължина_не_отнема_опит()
    {
        var user = await NewParticipantAsync("short");

        using var session = _app.NewSession();
        await RequestCodeAsync(session, user.Email!);

        var response = await SubmitCodeAsync(session, "123");
        Assert.Contains(Resx.Value("Pages.Verification", "Error_CodeRequired"),
            await response.ReadPageAsync());
        Assert.Null(await _app.Db.LockoutEndAsync(user.Email!));
    }

    /// <summary>A code of the same length that is certain to be different.</summary>
    private static string WrongCode(string code) =>
        code == "000000" ? "111111" : "000000";
}
