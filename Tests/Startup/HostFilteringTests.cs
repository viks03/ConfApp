// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Startup;

/// <summary>
/// Part 1, <c>AllowedHosts</c>: a request whose <c>Host</c> is not on the list
/// answers 400 on every page. It is checked the way an outside attacker would:
/// one header, nothing else.
/// <para>
/// The allowed names are read from <c>appsettings.json</c> rather than repeated
/// here. Comparing the setting to a literal only said that nobody had touched
/// the line, and it failed on every legitimate change of domain while the
/// filter still worked. What matters is the behaviour: who gets through.
/// </para>
/// </summary>
[Collection(AppCollection.Name)]
public class HostFilteringTests
{
    private readonly AppFixture _app;

    public HostFilteringTests(AppFixture app) => _app = app;

    /// <summary>Pages from different parts of the application, not only the home page.</summary>
    public static TheoryData<string> Pages() => new()
    {
        "/", "/Conference", "/Attend", "/Login", "/Register",
        "/Schedule", "/Lecturers", "/FAQ", "/Travel", "/Terms", "/Privacy"
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Чужд_Host_връща_400_на_всяка_страница(string path)
    {
        using var client = _app.NewClient(followRedirects: false);
        client.DefaultRequestHeaders.Host = "evil.example.com";

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The entries of <c>AllowedHosts</c>, trimmed, in the order they are written.
    /// </summary>
    private static string[] AllowedHosts()
    {
        var appsettings = Path.Combine(TestPaths.RepoRoot, "appsettings.json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(appsettings));

        var value = doc.RootElement.GetProperty("AllowedHosts").GetString();

        Assert.False(string.IsNullOrWhiteSpace(value),
            "AllowedHosts е празен — без списък филтърът пуска всичко.");

        return value!.Split(';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// One concrete <c>Host</c> header per entry. A wildcard is a pattern rather
    /// than a name, so a real subdomain is built from it — a request never
    /// carries the star itself.
    /// </summary>
    public static TheoryData<string> РазрешениИмена()
    {
        var data = new TheoryData<string>();

        foreach (var entry in AllowedHosts())
            data.Add(entry.StartsWith("*.", StringComparison.Ordinal)
                ? "poddomain" + entry[1..]
                : entry);

        return data;
    }

    [Theory]
    [MemberData(nameof(РазрешениИмена))]
    public async Task Разрешен_Host_минава(string host)
    {
        using var client = _app.NewClient(followRedirects: false);
        client.DefaultRequestHeaders.Host = host;

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Чужд_Host_не_минава_и_към_API_то()
    {
        // The webhooks are anonymous, so the host filter has to cover them too.
        using var client = _app.NewClient();
        client.DefaultRequestHeaders.Host = "evil.example.com";

        var response = await client.PostAsync("/api/crypto/webhook",
            new StringContent("{\"id\":1}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("www.blockchainedu2026.unwe.bg")]   // the subdomain is NOT on the list
    [InlineData("[::1]")]                           // nor is the IPv6 loopback
    [InlineData("blockchainedu2026.unwe.bg.evil.com")]
    public async Task Съседни_имена_също_се_отказват(string host)
    {
        // Recorded deliberately: this is the current behaviour and follows from the
        // exact list. If DNS ever points www.<domain> at the server, every request
        // from there will answer 400 — see the note in TESTS.md.
        using var client = _app.NewClient(followRedirects: false);
        client.DefaultRequestHeaders.Host = host;

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Шаблонът_не_пуска_чуждо_име_със_същата_опашка()
    {
        // A wildcard matches the tail of the name, so "*.icbi.bg" must cover
        // "www.icbi.bg" and reject "icbi.bg.evil.com" — an attacker registers
        // the second one precisely because it ends the same way.
        using var client = _app.NewClient(followRedirects: false);

        foreach (var pattern in AllowedHosts()
                     .Where(e => e.StartsWith("*.", StringComparison.Ordinal)))
        {
            var impostor = pattern[2..] + ".evil.com";
            client.DefaultRequestHeaders.Host = impostor;

            var response = await client.GetAsync("/");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public void Настройките_не_разрешават_всички_хостове()
    {
        // The one value that switches the filter off entirely. A pattern such as
        // "*.icbi.bg" is fine; a bare "*" is not, and the difference is easy to
        // lose while editing the line.
        Assert.DoesNotContain("*", AllowedHosts());
    }
}
