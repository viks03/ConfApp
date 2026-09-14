// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Auth;

/// <summary>
/// Part 3, the session: who gets where without being signed in, and what is
/// left behind after signing out.
/// </summary>
[Collection(AppCollection.Name)]
public class SessionTests : IAsyncLifetime
{
    private readonly AppFixture _app;
    private readonly List<string> _created = new();

    public SessionTests(AppFixture app) => _app = app;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var email in _created)
        {
            await _app.Db.DeleteParticipantAsync(email);
            await _app.Db.DeleteOtpCodesAsync(email);
        }
    }

    /// <summary>The path and query of the redirect; Location arrives as an absolute address.</summary>
    private static string Location(HttpResponseMessage response)
    {
        var raw = response.Headers.Location
            ?? throw new InvalidOperationException("Отговорът няма Location.");

        return raw.IsAbsoluteUri ? raw.PathAndQuery : raw.OriginalString;
    }

    private async Task<ConferenceApp.Models.ApplicationUser> NewParticipantAsync(string tag)
    {
        var email = $"ses-{tag}-{Guid.NewGuid():N}@example.test";
        _created.Add(email);
        return await _app.Db.CreateParticipantAsync(email);
    }

    // ════════════════════════════════════════════════════════════════════
    // Not signed in
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("/Profile")]
    [InlineData("/SubmitDocuments")]
    [InlineData("/Admin")]
    public async Task Защитена_страница_без_влизане_праща_към_входа(string path)
    {
        using var client = _app.NewClient(followRedirects: false);
        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var location = Location(response);
        Assert.StartsWith("/Login", location);
        Assert.Contains("ReturnUrl=" + Uri.EscapeDataString(path), location);
    }

    [Fact]
    public async Task След_вход_пренасочването_води_обратно_на_поисканата_страница()
    {
        var user = await NewParticipantAsync("return");

        using var client = _app.NewClient(followRedirects: false);
        var denied = await client.GetAsync("/Profile");
        var loginUrl = Location(denied);

        // The address the application offers has to be one that actually opens.
        using var session = _app.NewSession();
        var page = await session.Client.GetAsync(loginUrl);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        await session.LoginParticipantAsync(user.Email!);
        Assert.True(await session.IsSignedInAsync());
    }

    [Fact]
    public async Task Готово_без_влизане_праща_към_входа()
    {
        using var client = _app.NewClient(followRedirects: false);
        var response = await client.GetAsync("/Done");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login", Location(response));
    }

    [Fact]
    public async Task Влязъл_потребител_не_вижда_входа()
    {
        var user = await NewParticipantAsync("loggedin");

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        using var client = session.NoRedirectClient();
        var response = await client.GetAsync("/Login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Profile", Location(response));
    }

    [Fact]
    public async Task Влязъл_администратор_отива_в_панела_от_входа()
    {
        using var session = _app.NewSession();
        await session.LoginAdminAsync(_app.Credentials.AdminEmail, _app.Credentials.AdminPassword);

        using var client = session.NoRedirectClient();
        var response = await client.GetAsync("/Login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Admin", Location(response));
    }

    // ════════════════════════════════════════════════════════════════════
    // Signing out
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Излизането_прекратява_сесията_и_оставя_следа_в_одита()
    {
        var user = await NewParticipantAsync("logout");
        var auditFrom = await _app.Db.LastAuditIdAsync();

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);
        Assert.True(await session.IsSignedInAsync());

        using (var client = session.NoRedirectClient())
        {
            var response = await client.PostAsync("/Logout", new StringContent(string.Empty));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Login", Location(response));
        }

        Assert.False(await session.IsSignedInAsync());

        var audit = await _app.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "Logout" && a.UserEmail == user.Email);
    }

    [Fact]
    public async Task Бисквитка_отпреди_излизането_вече_не_отваря_профила()
    {
        // [T-05] Signing out rotates the security stamp, and the ticket is checked
        // against it on every request, so a copy of the cookie taken before the
        // sign-out stops working immediately rather than in 30 days.
        var user = await NewParticipantAsync("stolen");

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        // A copy of the cookies from before the sign-out, as someone who took
        // them off the machine would have them.
        using var stolen = session.SnapshotClient();
        Assert.Equal(HttpStatusCode.OK, (await stolen.GetAsync("/Profile")).StatusCode);

        using (var client = session.NoRedirectClient())
            await client.PostAsync("/Logout", new StringContent(string.Empty));

        var response = await stolen.GetAsync("/Profile");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Излизане_без_влизане_не_гърми()
    {
        using var client = _app.NewClient(followRedirects: false);
        var response = await client.PostAsync("/Logout", new StringContent(string.Empty));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login", Location(response));
    }

    [Fact]
    public async Task GET_към_изхода_не_отписва()
    {
        // [T-06] GET /Logout used to sign the user out, so a page elsewhere with
        // <img src="…/Logout"> could throw them out. Signing out in the
        // application goes only through POST forms; the request here is the one
        // another site would make.
        var user = await NewParticipantAsync("getlogout");

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        using (var client = session.NoRedirectClient())
        {
            var response = await client.GetAsync("/Logout");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/Profile", Location(response));
        }

        Assert.True(await session.IsSignedInAsync(), "GET /Logout отписа потребителя.");
    }
}
