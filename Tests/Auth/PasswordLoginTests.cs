// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Auth;

/// <summary>
/// Part 3, signing in with a password. <c>/Login</c> picks its path by whether
/// the account has a password, so every account with one goes down the
/// administrator's path.
/// <para>
/// The lockout tests use an account with a password of their own rather than the
/// real administrator: three wrong attempts would lock that account for 12
/// hours, and every later test that needs the panel would fail because of this
/// one.
/// </para>
/// </summary>
[Collection(AppCollection.Name)]
public class PasswordLoginTests : IAsyncLifetime
{
    private const string Password = "Test-Passw0rd";

    private readonly AppFixture _app;
    private readonly List<string> _created = new();

    public PasswordLoginTests(AppFixture app) => _app = app;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var email in _created)
        {
            await _app.Db.DeleteParticipantAsync(email);
            await _app.Db.DeleteOtpCodesAsync(email);
        }
    }

    private async Task<ConferenceApp.Models.ApplicationUser> NewPasswordUserAsync(string tag)
    {
        var email = $"pwd-{tag}-{Guid.NewGuid():N}@example.test";
        _created.Add(email);
        return await _app.Db.CreatePasswordUserAsync(email, Password);
    }

    private static async Task<string> PostLoginAsync(
        HttpSession session, string email, string? password = null)
    {
        var token = await session.AntiforgeryTokenAsync("/Login");

        var fields = new Dictionary<string, string>
        {
            ["Email"] = email,
            ["__RequestVerificationToken"] = token
        };
        if (password != null) fields["Password"] = password;

        var response = await session.PostFormAsync("/Login", fields);
        return await response.ReadPageAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // The happy path
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Администраторът_влиза_с_парола_и_стига_до_панела()
    {
        var auditFrom = await _app.Db.LastAuditIdAsync();

        using var session = _app.NewSession();
        await session.LoginAdminAsync(_app.Credentials.AdminEmail, _app.Credentials.AdminPassword);

        Assert.True(await session.IsSignedInAsync("/Admin"));

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "Admin Login");

        // An account with a password is sent no code.
        Assert.Empty(await _app.Db.OtpCodesAsync(_app.Credentials.AdminEmail));
    }

    [Fact]
    public async Task Само_с_имейл_страницата_иска_парола_и_казва_колко_опита_остават()
    {
        var user = await NewPasswordUserAsync("askpass");

        using var session = _app.NewSession();
        var html = await PostLoginAsync(session, user.Email!);

        Assert.Contains(Resx.Prefix("Pages.Login", "Error_AdminLoginDetected"), html);
        Assert.Contains("type=\"password\"", html);
        Assert.False(await session.IsSignedInAsync());

        // An account with a password does not go down the code path.
        Assert.Empty(await _app.Db.OtpCodesAsync(user.Email!));
    }

    [Fact]
    public async Task Акаунт_с_парола_влиза_в_профила_а_не_в_панела()
    {
        var user = await NewPasswordUserAsync("nonadmin");

        using var session = _app.NewSession();
        await PostLoginAsync(session, user.Email!);
        await PostLoginAsync(session, user.Email!, Password);

        Assert.True(await session.IsSignedInAsync());
        Assert.False(await session.IsSignedInAsync("/Admin"));
    }

    // ════════════════════════════════════════════════════════════════════
    // A wrong password, and the lockout
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Грешна_парола_не_влиза()
    {
        var user = await NewPasswordUserAsync("wrong");
        var auditFrom = await _app.Db.LastAuditIdAsync();

        using var session = _app.NewSession();
        var html = await PostLoginAsync(session, user.Email!, "Not-The-Passw0rd");

        Assert.Contains(Resx.Value("Pages.Login", "Error_InvalidPassword"), html);
        Assert.False(await session.IsSignedInAsync());

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "Admin Login Failed" && a.UserEmail == user.Email);

        // One wrong attempt does not lock anything.
        Assert.Null(await _app.Db.LockoutEndAsync(user.Email!));
        Assert.True(await session.IsSignedInAsync() == false);

        // The right password still works after a single miss.
        await PostLoginAsync(session, user.Email!, Password);
        Assert.True(await session.IsSignedInAsync());
    }

    [Fact]
    public async Task Три_грешни_опита_заключват_акаунта_и_блокират_адреса()
    {
        var user = await NewPasswordUserAsync("lock");
        var auditFrom = await _app.Db.LastAuditIdAsync();

        using var session = _app.NewSession();
        for (var attempt = 0; attempt < 3; attempt++)
            await PostLoginAsync(session, user.Email!, "Not-The-Passw0rd");

        var lockedUntil = await _app.Db.LockoutEndAsync(user.Email!);
        Assert.NotNull(lockedUntil);
        Assert.True(lockedUntil > DateTimeOffset.UtcNow.AddHours(11),
            $"Заключването е до {lockedUntil}, а трябва да е около 12 часа напред.");

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "IP Blocked" && a.IpAddress == session.ClientIp);

        // The right password from the same address no longer helps: the address
        // is blocked.
        var html = await PostLoginAsync(session, user.Email!, Password);
        Assert.Contains(Resx.Value("Pages.Login", "Error_LoginRestricted"), html);
        Assert.False(await session.IsSignedInAsync());
    }

    [Fact]
    public async Task Блокираният_адрес_не_пуска_и_чужд_акаунт()
    {
        var victim  = await NewPasswordUserAsync("blocked-a");
        var другият = await NewPasswordUserAsync("blocked-b");

        using var session = _app.NewSession();
        for (var attempt = 0; attempt < 3; attempt++)
            await PostLoginAsync(session, victim.Email!, "Not-The-Passw0rd");

        var html = await PostLoginAsync(session, другият.Email!, Password);

        Assert.Contains(Resx.Value("Pages.Login", "Error_LoginRestricted"), html);
        Assert.False(await session.IsSignedInAsync());
    }

    [Fact]
    public async Task Блокирането_е_само_за_този_адрес()
    {
        var user = await NewPasswordUserAsync("otherip");

        using (var blocked = _app.NewSession())
        {
            for (var attempt = 0; attempt < 3; attempt++)
                await PostLoginAsync(blocked, user.Email!, "Not-The-Passw0rd");
        }

        // Another participant from another address must not feel someone else's
        // block.
        var innocent = await NewPasswordUserAsync("innocent");

        using var fresh = _app.NewSession();
        await PostLoginAsync(fresh, innocent.Email!);
        await PostLoginAsync(fresh, innocent.Email!, Password);

        Assert.True(await fresh.IsSignedInAsync());
    }

    [Fact]
    public async Task Заключен_акаунт_не_влиза_и_с_вярната_парола()
    {
        var user = await NewPasswordUserAsync("locked");

        using (var attacker = _app.NewSession())
        {
            for (var attempt = 0; attempt < 3; attempt++)
                await PostLoginAsync(attacker, user.Email!, "Not-The-Passw0rd");
        }

        Assert.NotNull(await _app.Db.LockoutEndAsync(user.Email!));

        // The owner arrives from an address of their own with the right password.
        using var owner = _app.NewSession();
        await PostLoginAsync(owner, user.Email!);
        var html = await PostLoginAsync(owner, user.Email!, Password);

        Assert.False(await owner.IsSignedInAsync());

        // [T-08] It says the account is locked, and for how long, rather than
        // "wrong password". Otherwise the owner goes looking for a problem with
        // their password.
        Assert.Contains(Resx.Prefix("Pages.Login", "Error_AccountLockedFor"), html);
        Assert.DoesNotContain(Resx.Value("Pages.Login", "Error_InvalidPassword"), html);

        // The lockout is 12 hours, so what is left is measured in hours.
        Assert.Contains(Resx.Format("Pages.Login", "Lockout_Hours", 11), html);
    }

    [Fact]
    public async Task Опит_срещу_заключен_акаунт_не_трупа_нови_опити()
    {
        // [T-08] Every attempt against a locked account used to go through
        // PasswordSignInAsync and leave an "Admin Login Failed" behind, so an
        // owner who did not know they were locked out would block their own
        // address as well while trying their correct password.
        var user = await NewPasswordUserAsync("nocount");

        using (var attacker = _app.NewSession())
        {
            for (var attempt = 0; attempt < 3; attempt++)
                await PostLoginAsync(attacker, user.Email!, "Not-The-Passw0rd");
        }

        var lockedAt = await _app.Db.LockoutEndAsync(user.Email!);
        Assert.NotNull(lockedAt);

        var failedBefore = await _app.Db.AccessFailedCountAsync(user.Email!);
        var auditFrom    = await _app.Db.LastAuditIdAsync();

        using var owner = _app.NewSession();
        for (var attempt = 0; attempt < 4; attempt++)
            await PostLoginAsync(owner, user.Email!, Password);

        // Neither Identity's counter nor the end of the lockout moves.
        Assert.Equal(failedBefore, await _app.Db.AccessFailedCountAsync(user.Email!));
        Assert.Equal(lockedAt, await _app.Db.LockoutEndAsync(user.Email!));

        var audit = await _app.Db.AuditSinceAsync(auditFrom);

        // The counter that blocks an address looks for exactly "Admin Login
        // Failed", so there must be no such record; otherwise four attempts would
        // block this address too.
        Assert.DoesNotContain(audit, a => a.Action == "Admin Login Failed"
                                          && a.UserEmail == user.Email);

        // The trace is still left, under a different action, so that it shows up
        // in the audit tab.
        Assert.Contains(audit, a => a.Action == "Admin Login Refused — Account Locked"
                                    && a.UserEmail == user.Email);

        // And the owner's address is not blocked after the four attempts.
        Assert.DoesNotContain(audit, a => a.Action == "IP Blocked"
                                          && a.IpAddress == owner.ClientIp);
    }

    [Fact]
    public async Task Опитът_който_заключва_акаунта_го_казва()
    {
        // The counter that blocks an address is per (address, e-mail) pair, while
        // the lockout of the account is global. Two misses from one address and a
        // third from another lock the account without the second address reaching
        // three, which is exactly where the lockout message is the one shown.
        var user = await NewPasswordUserAsync("saysso");

        using (var first = _app.NewSession())
        {
            await PostLoginAsync(first, user.Email!, "Not-The-Passw0rd");
            await PostLoginAsync(first, user.Email!, "Not-The-Passw0rd");
        }

        Assert.Null(await _app.Db.LockoutEndAsync(user.Email!));

        using var second = _app.NewSession();
        var html = await PostLoginAsync(second, user.Email!, "Not-The-Passw0rd");

        Assert.NotNull(await _app.Db.LockoutEndAsync(user.Email!));
        Assert.Contains(Resx.Prefix("Pages.Login", "Error_AccountLockedFor"), html);
        Assert.DoesNotContain(Resx.Value("Pages.Login", "Error_InvalidPassword"), html);
    }

    // ════════════════════════════════════════════════════════════════════
    // Password recovery
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Възстановяване_на_парола_няма_никъде_в_приложението()
    {
        // Part 3 asks for "password recovery from end to end" and "an expired
        // password reset token". No such flow exists in the application: no page,
        // no link, no token issued anywhere. This test pins that state down, so
        // that if one appears it fails and says the flow now needs coverage.
        using var session = _app.NewSession();

        foreach (var path in new[]
                 {
                     "/ForgotPassword", "/ResetPassword", "/Account/ForgotPassword",
                     "/Identity/Account/ForgotPassword"
                 })
        {
            var response = await session.Client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.NotFound,
                $"{path} върна {(int)response.StatusCode} — има поток за смяна на парола, който не е покрит.");
        }

        var login = await session.Client.GetStringAsync("/Login");
        Assert.DoesNotContain("ForgotPassword", login, StringComparison.OrdinalIgnoreCase);
    }
}
