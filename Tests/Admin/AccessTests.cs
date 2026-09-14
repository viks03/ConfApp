// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Admin;

/// <summary>
/// Part 6: "an ordinary user cannot open /Admin".
/// <para>
/// The page is protected by <c>[Authorize(Roles = "Admin")]</c>, but the handlers
/// are entry points of their own: a request to <c>?handler=ConfirmPayment</c>
/// passes through nothing but that same attribute. The test is therefore not
/// content with the page itself and tries every action that changes something.
/// </para>
/// </summary>
public class AccessTests : AdminTestBase
{
    public AccessTests(AppFixture app) : base(app, "ad-acl") { }

    /// <summary>The pages that belong to the panel even though they do not live under /Areas.</summary>
    public static TheoryData<string> AdminPages => new()
    {
        "/Admin",
        "/Admin/BugReports",
        "/Admin/SendInvitations"
    };

    [Theory, MemberData(nameof(AdminPages))]
    public async Task Външен_не_вижда_панела(string path)
    {
        using var client = App.NewClient(followRedirects: false);
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("/Login", location, StringComparison.OrdinalIgnoreCase);
    }

    [Theory, MemberData(nameof(AdminPages))]
    public async Task Обикновен_потребител_не_вижда_панела(string path)
    {
        var user = await NewParticipantAsync();
        using var session = await SignedInAsync(user);

        using var client = session.NoRedirectClient();
        using var response = await client.GetAsync(path);

        // Signed in but without the role: Identity sends them to AccessDenied
        // rather than to the sign-in page.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var location = response.Headers.Location?.ToString() ?? "";
        Assert.Contains("AccessDenied", location, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Admin?", location, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Администраторът_вижда_панела()
    {
        using var admin = await SignedInAdminAsync();

        using var response = await admin.Client.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.ReadPageAsync();
        Assert.Contains("Submitted Registrations", html);
        Assert.Contains("data-target=\"tab-health\"", html);
    }

    /// <summary>
    /// All twenty tabs live in one page, so "every tab opens" means "every tab is
    /// rendered". A missing container is a tab that does not exist.
    /// </summary>
    [Fact]
    public async Task Всеки_таб_от_лентата_има_съдържание()
    {
        using var admin = await SignedInAdminAsync();
        var html = await PanelAsync(admin);

        var tabs = System.Text.RegularExpressions.Regex
            .Matches(html, "data-target=\"(?<id>tab-[a-z]+)\"")
            .Select(m => m.Groups["id"].Value)
            .Distinct()
            .ToList();

        Assert.True(tabs.Count >= 20, $"Очаквах поне двайсет таба, намерих {tabs.Count}.");

        foreach (var id in tabs)
            Assert.Contains($"id=\"{id}\"", html);
    }

    // ════════════════════════════════════════════════════════════════════
    // The handlers one by one
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Actions that change something. Each is attempted as a participant.</summary>
    public static TheoryData<string> MutatingHandlers => new()
    {
        "SaveRegistration", "DeleteUser", "ConfirmPayment", "CancelPayment",
        "ApproveVerification", "RejectVerification", "EditTicket",
        "SaveLecturer", "DeleteLecturer", "SaveSession", "DeleteSession",
        "ActivateTheme", "DeleteTheme", "SavePageStyle", "SetGlobalMobile",
        "TogglePageMobile", "ResetPageStyle", "DisableAllCustomCss",
        "RemoveDownload", "ToggleEmailNotification", "TogglePaymentGate",
        "ClearInactiveCryptoOrders", "SaveFaq", "DeleteFaq", "SaveHotel",
        "SavePromo", "DeletePromo", "SaveSocialLinks", "SaveLiveLink",
        // [T-36]: the button in Health Check copies the whole database — names,
        // addresses, telephone numbers, password hashes.
        "CreateBackup"
    };

    [Theory, MemberData(nameof(MutatingHandlers))]
    public async Task Обикновен_потребител_не_стига_до_действие_в_панела(string handler)
    {
        var user = await NewParticipantAsync();
        using var session = await SignedInAsync(user);

        // The token belongs to the session rather than to the page, so it is taken
        // from a page the participant is allowed to open.
        var token = await session.AntiforgeryTokenAsync("/Profile");

        using var client = session.NoRedirectClient();
        using var response = await client.PostAsync(
            $"/Admin?handler={handler}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }));

        Assert.True(
            response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Forbidden,
            $"handler={handler} отговори {(int)response.StatusCode}, а трябваше да откаже.");

        if (response.StatusCode == HttpStatusCode.Found)
            Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? "",
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reading is access too: an export carries everyone's personal data out.</summary>
    public static TheoryData<string> ReadingHandlers => new()
    {
        "ExportRegistrations&type=all",
        "ExportAuditLogs",
        "FetchUserAudits&email=someone@example.test",
        "FetchRejectionReason&userId=1",
        "ThemeTemplate",
        "HealthCheck"
    };

    [Theory, MemberData(nameof(ReadingHandlers))]
    public async Task Обикновен_потребител_не_чете_от_панела(string query)
    {
        var user = await NewParticipantAsync();
        using var session = await SignedInAsync(user);

        using var client = session.NoRedirectClient();
        using var response = await client.GetAsync($"/Admin?handler={query}");

        Assert.True(
            response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Forbidden,
            $"{query} отговори {(int)response.StatusCode}, а трябваше да откаже.");
    }

    [Fact]
    public async Task Външен_не_изнася_регистрациите()
    {
        using var client = App.NewClient(followRedirects: false);
        using var response = await client.GetAsync("/Admin?handler=ExportRegistrations&type=all");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains("/Login", response.Headers.Location?.ToString() ?? "");
    }

    /// <summary>
    /// Health is a GET without a token, deliberately. The handler therefore checks
    /// the role itself rather than relying on the attribute on the page alone.
    /// </summary>
    [Fact]
    public async Task Health_проверява_ролята_сам()
    {
        using var admin = await SignedInAdminAsync();

        using var response = await admin.Client.GetAsync("/Admin?handler=HealthCheck&service=database");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"key\"", body);
        Assert.DoesNotContain("forbidden", body);
    }

    [Fact]
    public async Task Изтриване_на_участник_от_чужди_ръце_не_става()
    {
        var victim = await NewParticipantAsync();
        var attacker = await NewParticipantAsync();

        using var session = await SignedInAsync(attacker);
        var token = await session.AntiforgeryTokenAsync("/Profile");

        using var client = session.NoRedirectClient();
        using var response = await client.PostAsync("/Admin?handler=DeleteUser",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["id"] = victim.Id,
                ["__RequestVerificationToken"] = token
            }));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await App.Db.FindUserAsync(victim.Email!));
    }
}
