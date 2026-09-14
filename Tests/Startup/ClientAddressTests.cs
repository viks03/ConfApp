// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Startup;

/// <summary>
/// Part 1, the real client address in the audit log. Sign in, then look at the
/// Audit tab: <c>IpAddress</c> has to be a client's address, not the proxy's.
/// <para>
/// In the deployed application the client sits behind Nginx, so their address
/// arrives in <c>X-Forwarded-For</c>. The check therefore has two halves, and
/// the second is the more important one: the header must be honoured ONLY from
/// a trusted neighbour (<c>ForwardedHeaders:KnownProxies</c>). Otherwise any
/// client can choose which address the log records for them.
/// </para>
/// </summary>
[Collection(AppCollection.Name)]
public class ClientAddressTests
{
    private readonly AppFixture _app;

    public ClientAddressTests(AppFixture app) => _app = app;

    [Fact]
    public async Task Журналът_пази_адреса_на_клиента_а_не_на_проксито()
    {
        var email = $"audit-ip-{Guid.NewGuid():N}@example.test";
        await _app.Db.CreateParticipantAsync(email);

        try
        {
            using var session = _app.NewSession();

            // The session presents an address from 10.0.0.0/8 while the connection
            // comes from loopback, exactly as it does behind a proxy on the same
            // machine.
            await session.LoginParticipantAsync(email);

            var audit = await _app.Db.AuditForAsync(email);
            Assert.NotEmpty(audit);

            var login = audit.First(a => a.Action == "Login");

            Assert.Equal(session.ClientIp, login.IpAddress);
            Assert.NotEqual("127.0.0.1", login.IpAddress);
            Assert.NotEqual("Unknown", login.IpAddress);
        }
        finally
        {
            await _app.Db.DeleteParticipantAsync(email);
        }
    }

    [Fact]
    public async Task Всеки_запис_от_една_сесия_носи_същия_адрес()
    {
        var email = $"audit-ip2-{Guid.NewGuid():N}@example.test";
        await _app.Db.CreateParticipantAsync(email);

        try
        {
            using var session = _app.NewSession();
            await session.LoginParticipantAsync(email);
            await session.PostHandlerAsync("/Payment/earlybird", "SubmitIban");

            var audit = await _app.Db.AuditForAsync(email);
            var withIp = audit.Where(a => !string.IsNullOrEmpty(a.IpAddress)).ToList();

            Assert.True(withIp.Count >= 2, $"Очаквах поне два записа с адрес, намерих {withIp.Count}.");
            Assert.All(withIp, a => Assert.Equal(session.ClientIp, a.IpAddress));
        }
        finally
        {
            await _app.Db.DeleteParticipantAsync(email);
        }
    }

    [Fact]
    public async Task Недоверен_съсед_не_може_да_си_избере_адрес()
    {
        // A probe instance in which loopback is NOT in KnownProxies. The
        // X-Forwarded-For header must then be stripped before anything sees it,
        // and the log must keep the connection's real address.
        var probe = await StartupProbe.StartAsync("untrusted-proxy", settings =>
        {
            settings["ForwardedHeaders:KnownProxies:0"] = "10.99.99.99";
        });

        Assert.Null(probe.StartupError);

        using var db = probe.Db();
        var email = $"spoof-{Guid.NewGuid():N}@example.test";
        await db.CreateParticipantAsync(email);

        using var client = probe.NewClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "8.8.8.8");

        var html = await client.GetStringAsync("/Login");
        var token = HttpSession.ExtractAntiforgeryToken(html, "/Login");

        await client.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["__RequestVerificationToken"] = token
        }));

        var audit = await db.AuditForAsync(email);
        Assert.NotEmpty(audit);

        Assert.All(audit, a =>
        {
            Assert.NotEqual("8.8.8.8", a.IpAddress);
            Assert.Equal("127.0.0.1", a.IpAddress);
        });
    }
}
