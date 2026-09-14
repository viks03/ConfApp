// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text.Json;
using ConferenceApp.Services;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Services;

/// <summary>
/// The three background cards against the <b>live</b> application, through the
/// admin panel itself.
/// <para>
/// <see cref="BackgroundHealthTests"/> checks what the probe shows in every
/// possible state. What is checked here is simpler and more important: that
/// against a really running application the state it reads is real — the services
/// are genuinely turning, rather than the card being green by default.
/// </para>
/// </summary>
[Collection(AppCollection.Name)]
public class LiveBackgroundHealthTests
{
    private readonly AppFixture _app;

    public LiveBackgroundHealthTests(AppFixture app) => _app = app;

    private async Task<HttpSession> SignedInAdminAsync()
    {
        var session = _app.NewSession();
        await session.LoginAdminAsync(_app.Credentials.AdminEmail, _app.Credentials.AdminPassword);
        return session;
    }

    private async Task<JsonElement> CheckAsync(string key)
    {
        using var admin = await SignedInAdminAsync();

        using var response = await admin.Client.GetAsync($"/Admin?handler=HealthCheck&service={key}");
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static Dictionary<string, string> DetailsOf(JsonElement el) =>
        el.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
            ? details.EnumerateArray().ToDictionary(
                  d => d.GetProperty("label").GetString()!,
                  d => d.GetProperty("value").GetString()!)
            : new Dictionary<string, string>();

    /// <summary>
    /// The card has to read the queue the application's mail really travels
    /// through, so the test first sends one the real way — by asking for a sign-in
    /// code — and only then queries it.
    /// </summary>
    [Fact]
    public async Task Опашката_наистина_се_върти()
    {
        var email = $"queue-live-{Guid.NewGuid():N}@example.test";

        await _app.Db.CreateParticipantAsync(email);

        try
        {
            using (var session = _app.NewSession())
            {
                var token = await session.AntiforgeryTokenAsync("/Login");
                using var response = await session.PostFormAsync("/Login", new Dictionary<string, string>
                {
                    ["Email"] = email,
                    ["__RequestVerificationToken"] = token
                });
                response.EnsureSuccessStatusCode();
            }

            Assert.NotNull(await _app.Smtp.WaitForAsync(email, TimeSpan.FromSeconds(30)));

            var el = DetailsOf(await CheckAsync("emailQueue"));

            Assert.Equal("работи", el["Консуматор"]);
            Assert.NotEqual("— (няма от старта)", el["Последна активност"]);
            Assert.NotEqual("0 / 0 (от старта на процеса)", el["Изпратени / провалени"]);
        }
        finally
        {
            await _app.Db.DeleteOtpCodesAsync(email);
            await _app.Db.DeleteParticipantAsync(email);
        }
    }

    [Fact]
    public async Task Чистенето_наистина_се_върти()
    {
        var el = await CheckAsync("cleanup");
        var details = DetailsOf(el);

        // The first cycle runs immediately after startup, so by the time this test
        // runs it is long since done.
        Assert.Equal("ok", el.GetProperty("status").GetString());
        Assert.Equal("работи", details["Услуга"]);
        Assert.Equal($"на всеки {CleanupService.Interval.TotalHours:0.#} ч.", details["Интервал"]);
        Assert.NotEqual("— (няма от старта)", details["Последен успешен"]);
    }

    /// <summary>
    /// The backups card looks at the application's folder. Under test the database
    /// is <c>App_Data/test.db</c>, so whatever it shows must not be about the live
    /// database.
    /// </summary>
    [Fact]
    public async Task Картата_за_копията_отговаря_и_не_гледа_живата_база()
    {
        var el = await CheckAsync("backups");

        Assert.Equal("backups", el.GetProperty("key").GetString());
        Assert.Contains(el.GetProperty("status").GetString(), new[] { "ok", "warn", "fail" });
        Assert.NotEqual(string.Empty, el.GetProperty("message").GetString());

        var details = DetailsOf(el);

        if (details.TryGetValue("Последно", out var newest))
            Assert.StartsWith("test_", newest);
    }

    [Fact]
    public async Task Трите_фонови_услуги_са_в_общия_отчет()
    {
        using var admin = await SignedInAdminAsync();

        using var response = await admin.Client.GetAsync("/Admin?handler=HealthCheck");
        response.EnsureSuccessStatusCode();

        var report = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        var keys = report.GetProperty("services").EnumerateArray()
            .Select(s => s.GetProperty("key").GetString())
            .ToList();

        Assert.Contains("emailQueue", keys);
        Assert.Contains("cleanup", keys);
        Assert.Contains("backups", keys);
    }
}
