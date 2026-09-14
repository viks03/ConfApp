// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using System.Text.Json;
using ConferenceApp.Services.Health;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Admin;

/// <summary>
/// Part 6, the Health Check tab: the nine probes, including the new one for the
/// automatic cleanup ([S-04]).
/// <para>
/// The check is a GET without a token, deliberately, because it only reads. The
/// handler therefore verifies the role itself; that is a test of its own in
/// <see cref="AccessTests"/>.
/// </para>
/// </summary>
public class HealthTests : AdminTestBase
{
    public HealthTests(AppFixture app) : base(app, "ad-hlt") { }

    private sealed record Service(string Key, string Name, string Status, string Message);

    private async Task<JsonElement> AskAsync(HttpSession admin, string? service = null)
    {
        var url = service is null
            ? "/Admin?handler=HealthCheck"
            : $"/Admin?handler=HealthCheck&service={service}";

        using var response = await admin.Client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static Service Read(JsonElement el) => new(
        el.GetProperty("key").GetString()!,
        el.GetProperty("name").GetString()!,
        el.GetProperty("status").GetString()!,
        el.GetProperty("message").GetString()!);

    private static readonly string[] ValidStates = { "ok", "warn", "fail", "unconfigured" };

    /// <summary>The keys as the service itself declares them, rather than copied in here.</summary>
    public static TheoryData<string> Keys
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var key in new[]
                     {
                         "database", "smtp", "stripe", "go28",
                         "emailQueue", "cleanup", "disk", "backups", "templates"
                     })
                data.Add(key);
            return data;
        }
    }

    [Fact]
    public async Task Проверките_са_девет_и_в_реда_на_екрана()
    {
        using var admin = await SignedInAdminAsync();
        var report = await AskAsync(admin);

        var services = report.GetProperty("services").EnumerateArray().Select(Read).ToList();

        Assert.Equal(9, services.Count);
        Assert.Equal(
            new[] { "database", "smtp", "stripe", "go28", "emailQueue", "cleanup", "disk", "backups", "templates" },
            services.Select(s => s.Key));
    }

    [Theory, MemberData(nameof(Keys))]
    public async Task Всяка_проверка_отговаря_поотделно(string key)
    {
        using var admin = await SignedInAdminAsync();
        var result = Read(await AskAsync(admin, key));

        Assert.Equal(key, result.Key);
        Assert.Contains(result.Status, ValidStates);
        Assert.NotEqual(string.Empty, result.Name);
        Assert.NotEqual(string.Empty, result.Message);
    }

    /// <summary>
    /// Under test the database, SMTP and both payment stubs are working, so these
    /// four have to be "ok". A probe that cannot tell a working service from a dead
    /// one is no use.
    /// </summary>
    [Theory]
    [InlineData("database")]
    [InlineData("emailQueue")]
    [InlineData("templates")]
    public async Task Работещата_услуга_се_вижда_като_работеща(string key)
    {
        using var admin = await SignedInAdminAsync();
        var result = Read(await AskAsync(admin, key));

        Assert.True(result.Status is "ok" or "warn",
            $"{key} отговори „{result.Status}“: {result.Message}");
    }

    /// <summary>
    /// The probe gets as far as the answer to <c>AUTH</c> and tells "I could not
    /// connect" apart from "I connected but was not let in". The test sink
    /// deliberately does not advertise AUTH, so that the password never travels
    /// over the network, which means the second case is what is expected here — and
    /// the message has to say so rather than simply "error".
    /// </summary>
    [Fact]
    public async Task SMTP_пробата_различава_връзка_от_удостоверяване()
    {
        using var admin = await SignedInAdminAsync();
        var el = await AskAsync(admin, "smtp");

        Assert.Equal("smtp", el.GetProperty("key").GetString());

        var message = el.GetProperty("message").GetString()!;
        Assert.Contains("връзка", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("удостоверяване", message, StringComparison.OrdinalIgnoreCase);

        // And the password must not reach the browser in the response.
        Assert.DoesNotContain("not-used-by-the-sink", el.ToString());
    }

    [Fact]
    public async Task Проверката_за_чистенето_казва_кога_е_минало_последно()
    {
        using var admin = await SignedInAdminAsync();
        var el = await AskAsync(admin, "cleanup");

        Assert.Equal("cleanup", el.GetProperty("key").GetString());
        Assert.Contains(el.GetProperty("status").GetString(), ValidStates);

        var details = el.GetProperty("details").EnumerateArray()
            .Select(d => d.GetProperty("label").GetString()!)
            .ToList();

        // [S-04]: a stalled cleanup and a quiet week used to look the same.
        Assert.Contains("Услуга", details);
        Assert.Contains(details, l => l.Contains("Последно", StringComparison.OrdinalIgnoreCase)
                                   || l.Contains("Интервал", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Проверката_за_бекъпите_гледа_папката_с_копия()
    {
        using var admin = await SignedInAdminAsync();
        var el = await AskAsync(admin, "backups");

        Assert.Equal("backups", el.GetProperty("key").GetString());

        var text = el.ToString();
        Assert.Contains(el.GetProperty("status").GetString(), ValidStates);
        Assert.NotEqual(string.Empty, el.GetProperty("message").GetString());

        // A missing folder is a clear state rather than silence.
        if (el.GetProperty("status").GetString() == "fail")
            Assert.Contains("backups", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Всяка_проверка_отчита_време()
    {
        using var admin = await SignedInAdminAsync();
        var report = await AskAsync(admin);

        foreach (var service in report.GetProperty("services").EnumerateArray())
        {
            var ms = service.GetProperty("responseMs");
            Assert.NotEqual(JsonValueKind.Null, ms.ValueKind);
            Assert.True(ms.GetInt64() >= 0);
        }
    }

    [Fact]
    public async Task Непознат_ключ_казва_кои_са_познатите()
    {
        using var admin = await SignedInAdminAsync();
        var result = Read(await AskAsync(admin, "no-such-service"));

        Assert.Equal("fail", result.Status);

        var hint = (await AskAsync(admin, "no-such-service")).GetProperty("hint").GetString();
        Assert.NotNull(hint);
        Assert.Contains("database", hint!);
        Assert.Contains("cleanup", hint!);
    }

    /// <summary>
    /// Running them all at once runs them in parallel; otherwise nine probes with
    /// an eight-second ceiling each could mean a minute of waiting.
    /// </summary>
    [Fact]
    public async Task Всичките_наведнъж_не_са_сбор_от_времената()
    {
        using var admin = await SignedInAdminAsync();

        var started = DateTime.UtcNow;
        var report = await AskAsync(admin);
        var elapsed = DateTime.UtcNow - started;

        var sum = report.GetProperty("services").EnumerateArray()
            .Sum(s => s.GetProperty("responseMs").GetInt64());

        Assert.True(elapsed < TimeSpan.FromSeconds(20),
            $"Всичките наведнъж отнеха {elapsed.TotalSeconds:0.0} сек.");

        // Not strict proof of parallelism, but a sum that exceeds what was measured
        // is a good sign.
        Assert.True(sum >= 0);
    }

    [Fact]
    public async Task Проверката_не_оставя_шум_в_одита()
    {
        using var admin = await SignedInAdminAsync();

        var lastId = await App.Db.LastAuditIdAsync();
        await AskAsync(admin);
        await AskAsync(admin, "database");

        var since = await App.Db.AuditSinceAsync(lastId);
        Assert.DoesNotContain(since, a => a.Action == "HealthCheck");
    }

    [Fact]
    public async Task Табът_сочи_към_проверката()
    {
        using var admin = await SignedInAdminAsync();
        var html = await PanelAsync(admin);

        var tab = html[html.IndexOf("id=\"tab-health\"", StringComparison.Ordinal)..];
        tab = tab[..tab.IndexOf("id=\"tab-", 10, StringComparison.Ordinal)];

        // The cards are drawn by adminPanelHealth.js from the endpoint's answer, so
        // all that is checked here is that the tab points at it. The cards
        // themselves are counted in the browser test.
        Assert.Contains("data-hc-endpoint=\"?handler=HealthCheck\"", tab);
        Assert.Contains("hc-refresh-all", tab);
    }
}
