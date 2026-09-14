// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Payments;

/// <summary>
/// Checks the scaffolding itself: whether the application starts, whether the
/// database really is a separate one, and whether Stripe and Go28 point at the
/// local stand-ins.
/// </summary>
[Collection(AppCollection.Name)]
public class SmokeTests
{
    private readonly AppFixture _app;

    public SmokeTests(AppFixture app) => _app = app;

    [Fact]
    public async Task Приложението_отговаря_и_ползва_тестовата_база()
    {
        using var session = _app.NewSession();
        var response = await session.Client.GetAsync("/");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);

        // The tiers come from the seed, which means the migrations ran against
        // test.db.
        var tier = await _app.Db.TierAsync("earlybird");
        Assert.NotNull(tier);
        Assert.True(File.Exists(TestPaths.TestDbFile));
    }

    [Fact]
    public async Task Администраторът_влиза_с_парола()
    {
        using var session = _app.NewSession();
        await session.LoginAdminAsync(_app.Credentials.AdminEmail, _app.Credentials.AdminPassword);

        using var client = session.NoRedirectClient();
        var admin = await client.GetAsync("/Admin");
        Assert.Equal(System.Net.HttpStatusCode.OK, admin.StatusCode);
    }

    [Fact]
    public async Task Участникът_влиза_с_код_от_базата()
    {
        var email = $"smoke-login-{Guid.NewGuid():N}@example.test";
        await _app.Db.CreateParticipantAsync(email);

        try
        {
            using var session = _app.NewSession();
            await session.LoginParticipantAsync(email);
            Assert.True(await session.IsSignedInAsync());
        }
        finally
        {
            await _app.Db.DeleteParticipantAsync(email);
        }
    }
}
