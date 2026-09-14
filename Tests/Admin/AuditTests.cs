// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Tests.Admin;

/// <summary>
/// Part 6, the Audit Logs tab.
/// <para>
/// The audit log is not written by hand in every handler: <c>AdminAuditFilter</c>
/// does it automatically for every action that changes something, and the nine
/// handlers that write a detailed record of their own are skipped so that one
/// action does not leave two rows. The test guards both sides — that it is
/// written, and that it is not written twice.
/// </para>
/// </summary>
public class AuditTests : AdminTestBase
{
    public AuditTests(AppFixture app) : base(app, "ad-aud") { }

    private static string AuditTab(string html)
    {
        var start = html.IndexOf("id=\"tab-audit\"", StringComparison.Ordinal);
        Assert.True(start > 0, "Табът „Audit Logs“ липсва.");

        var end = html.IndexOf("id=\"tab-", start + 10, StringComparison.Ordinal);
        return end > start ? html[start..end] : html[start..];
    }

    // ════════════════════════════════════════════════════════════════════
    // The records appear
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Всяко_действие_без_собствен_запис_влиза_в_одита()
    {
        using var admin = await SignedInAdminAsync();
        var lastId = await App.Db.LastAuditIdAsync();

        // An action whose handler does no logging of its own.
        await PostAsync(admin, "SetGlobalMobile", new Dictionary<string, string>
        {
            ["allowed"] = "true"
        });

        var since = await App.Db.AuditSinceAsync(lastId);
        Assert.Contains(since, a => a.Action == "Global Mobile Backgrounds");
    }

    [Fact]
    public async Task Handler_без_собствен_запис_се_описва_от_филтъра()
    {
        using var admin = await SignedInAdminAsync();
        var lastId = await App.Db.LastAuditIdAsync();

        await PostAsync(admin, "ToggleFooterLinkActive", new Dictionary<string, string>
        {
            ["id"] = "999999"
        });

        var since = await App.Db.AuditSinceAsync(lastId);
        var entry = since.FirstOrDefault(a => a.Action == "ToggleFooterLinkActive");

        Assert.NotNull(entry);
        // The audit log has to tell "pressed" apart from "succeeded".
        Assert.StartsWith("REJECTED", entry!.Details);
        Assert.Equal(App.Credentials.AdminEmail, entry.UserEmail);
    }

    [Fact]
    public async Task Успялото_действие_се_отбелязва_като_успяло()
    {
        using var admin = await SignedInAdminAsync();
        var lastId = await App.Db.LastAuditIdAsync();

        await PostAsync(admin, "ResetPageStyle", new Dictionary<string, string>
        {
            ["pageKey"] = "/FAQ"
        });
        TrackStyle("/FAQ");

        var since = await App.Db.AuditSinceAsync(lastId);
        var entry = since.FirstOrDefault(a => a.Action == "ResetPageStyle");

        Assert.NotNull(entry);
        Assert.StartsWith("OK", entry!.Details);
        Assert.Contains("/FAQ", entry.Details);
    }

    /// <summary>
    /// The nine handlers that write a detailed record of their own must not also
    /// get an automatic one; otherwise confirming a single payment leaves two rows
    /// and the counts in the reports go wrong.
    /// </summary>
    [Fact]
    public async Task Един_handler_със_собствен_запис_не_оставя_два_реда()
    {
        var user = await NewParticipantAsync();

        using var admin = await SignedInAdminAsync();
        var lastId = await App.Db.LastAuditIdAsync();

        await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id, ["method"] = "IBAN"
        });

        var since = await App.Db.AuditSinceAsync(lastId);

        Assert.Single(since.Where(a => a.Action == "Payment Confirmed — Admin"));
        Assert.DoesNotContain(since, a => a.Action == "ConfirmPayment");
    }

    [Fact]
    public async Task Четенето_не_оставя_шум()
    {
        using var admin = await SignedInAdminAsync();
        var lastId = await App.Db.LastAuditIdAsync();

        await admin.Client.GetAsync("/Admin?handler=HealthCheck&service=database");
        await admin.Client.GetAsync("/Admin?handler=FetchRejectionReason&userId=x");
        await admin.Client.GetAsync("/Admin?handler=FetchUserAudits&email=x@example.test");
        await admin.Client.GetAsync("/Admin");

        var since = await App.Db.AuditSinceAsync(lastId);

        foreach (var noise in new[] { "HealthCheck", "FetchRejectionReason", "FetchUserAudits" })
            Assert.DoesNotContain(since, a => a.Action == noise);
    }

    // ════════════════════════════════════════════════════════════════════
    // What does NOT go into a record
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The audit log is read by people and exported to CSV. A token or a password
    /// that ends up in it leaks at once to everyone with access to the panel.
    /// </summary>
    [Fact]
    public async Task Токенът_не_влиза_в_записа()
    {
        using var admin = await SignedInAdminAsync();
        var lastId = await App.Db.LastAuditIdAsync();

        await PostAsync(admin, "SetGlobalMobile", new Dictionary<string, string>
        {
            ["allowed"] = "true"
        });

        var since = await App.Db.AuditSinceAsync(lastId);

        foreach (var entry in since)
        {
            Assert.DoesNotContain("__RequestVerificationToken", entry.Details);
            Assert.DoesNotContain(App.Credentials.AdminPassword, entry.Details);
        }
    }

    /// <summary>
    /// Large text fields are summarised rather than recorded in full; otherwise one
    /// row about a change to the privacy policy takes up 900 characters of
    /// base64.
    /// </summary>
    [Fact]
    public async Task Едро_съдържание_се_обобщава()
    {
        var big = new string('я', 400);
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"<p>{big}</p>"));

        using var admin = await SignedInAdminAsync();
        var lastId = await App.Db.LastAuditIdAsync();

        // The policy is a SHARED row, the same one /Privacy reads. It is restored in
        // a finally block: without that, part 7 finds a page with a 400-character
        // word that does not wrap and reports it as overflowing.
        var before = await PrivacyAsync();

        try
        {
            await PostAsync(admin, "SavePrivacyPolicy", new Dictionary<string, string>
            {
                ["contentEn"] = encoded,
                ["contentBg"] = encoded
            });

            var since = await App.Db.AuditSinceAsync(lastId);
            var entry = since.FirstOrDefault(a => a.Action == "SavePrivacyPolicy");

            Assert.NotNull(entry);
            Assert.DoesNotContain(big, entry!.Details);
            Assert.Contains("знака", entry.Details);
        }
        finally
        {
            await RestorePrivacyAsync(before);
        }
    }

    [Fact]
    public async Task Записът_не_надхвърля_тавана()
    {
        var huge = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("<p>" + new string('x', 5000) + "</p>"));

        using var admin = await SignedInAdminAsync();
        var lastId = await App.Db.LastAuditIdAsync();

        // The terms are a SHARED row too; see the note on the policy above.
        var before = await TermsAsync();

        try
        {
            await PostAsync(admin, "SaveTermsOfUse", new Dictionary<string, string>
            {
                ["contentEn"] = huge,
                ["contentBg"] = huge
            });

            var since = await App.Db.AuditSinceAsync(lastId);
            foreach (var entry in since)
                Assert.True(entry.Details.Length <= 901,
                    $"Ред в одита е {entry.Details.Length} знака, а таванът е 900.");
        }
        finally
        {
            await RestoreTermsAsync(before);
        }
    }

    // ── The shared rows behind the two legal pages ──────────────────────

    private Task<(string En, string Bg)?> PrivacyAsync() =>
        App.Db.ReadAsync(async db =>
        {
            var row = await db.PrivacyPolicyContents.AsNoTracking().FirstOrDefaultAsync();
            return row == null ? ((string, string)?)null : (row.ContentEn, row.ContentBg);
        });

    private Task RestorePrivacyAsync((string En, string Bg)? before) =>
        App.Db.WriteAsync(async db =>
        {
            var row = await db.PrivacyPolicyContents.FirstOrDefaultAsync();
            if (row == null) return;

            if (before == null) db.PrivacyPolicyContents.Remove(row);
            else { row.ContentEn = before.Value.En; row.ContentBg = before.Value.Bg; }
        });

    private Task<(string En, string Bg)?> TermsAsync() =>
        App.Db.ReadAsync(async db =>
        {
            var row = await db.TermsOfUseContents.AsNoTracking().FirstOrDefaultAsync();
            return row == null ? ((string, string)?)null : (row.ContentEn, row.ContentBg);
        });

    private Task RestoreTermsAsync((string En, string Bg)? before) =>
        App.Db.WriteAsync(async db =>
        {
            var row = await db.TermsOfUseContents.FirstOrDefaultAsync();
            if (row == null) return;

            if (before == null) db.TermsOfUseContents.Remove(row);
            else { row.ContentEn = before.Value.En; row.ContentBg = before.Value.Bg; }
        });

    // ════════════════════════════════════════════════════════════════════
    // Display and filters
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Последното_действие_се_вижда_в_таба()
    {
        var user = await NewParticipantAsync();

        using var admin = await SignedInAdminAsync();
        await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id, ["method"] = "IBAN"
        });

        var tab = AuditTab(await PanelAsync(admin));

        Assert.Contains(user.Email!, tab);
        Assert.Contains("Payment Confirmed", tab);
    }

    [Fact]
    public async Task Показват_се_най_много_двеста_записа()
    {
        using var admin = await SignedInAdminAsync();
        var tab = AuditTab(await PanelAsync(admin));

        var rows = System.Text.RegularExpressions.Regex
            .Matches(tab, "audit-data-row").Count;

        var total = await App.Db.ReadAsync(db => db.AuditLogs.CountAsync());

        Assert.Equal(Math.Min(200, total), rows);
    }

    /// <summary>
    /// The filter searches by <c>data-search</c> and <c>data-action</c>, so the test
    /// guards the values the script filters on. The hiding itself is in the browser
    /// test.
    /// </summary>
    [Fact]
    public async Task Редовете_носят_стойностите_за_филтрите()
    {
        var user = await NewParticipantAsync();

        using var admin = await SignedInAdminAsync();
        await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id, ["method"] = "IBAN"
        });

        var tab = AuditTab(await PanelAsync(admin));
        var index = tab.IndexOf(user.Email!, StringComparison.OrdinalIgnoreCase);
        Assert.True(index > 0);

        var row = tab[tab.LastIndexOf("<tr", index, StringComparison.Ordinal)..
                      tab.IndexOf("</tr>", index, StringComparison.Ordinal)];

        Assert.Contains("data-search=", row);
        Assert.Contains("data-action=", row);
        Assert.Contains(user.Email!.ToLowerInvariant(), row);
    }

    /// <summary>
    /// The values in the dropdown are a hand-written list, while the actions
    /// themselves are written elsewhere. The test performs one real action for
    /// every value the panel can produce and checks that the filter finds it;
    /// otherwise a choice that always comes back empty looks like an absence of
    /// records.
    /// <para>
    /// "Stripe", "Webhook" and "System" come from the payments and the background
    /// services (parts 2 and 10) rather than from the panel, so they are not
    /// produced here.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Филтърът_намира_действията_които_панелът_произвежда()
    {
        var toEdit   = await NewParticipantAsync();
        var toVerify = await NewParticipantAsync("2");
        var toDelete = await NewParticipantAsync();

        await App.Db.SetVerificationAsync(toVerify.Email!, "Pending",
            "uploads/submitted-documents/students/probe.png");

        using var admin = await SignedInAdminAsync();

        await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = toEdit.Id, ["method"] = "IBAN"
        });

        await PostAsync(admin, "ApproveVerification", new Dictionary<string, string>
        {
            ["userId"] = toVerify.Id
        });

        await PostAsync(admin, "SaveRegistration", new Dictionary<string, string>
        {
            ["id"]             = toEdit.Id,
            ["firstName"]      = "Renamed",
            ["lastName"]       = toEdit.LastName ?? "",
            ["age"]            = toEdit.Age.ToString(),
            ["phone"]          = toEdit.PhoneNumber ?? "",
            ["academicTitle"]  = toEdit.AcademicTitle ?? "",
            ["organization"]   = toEdit.Workplace ?? "",
            ["participation"]  = toEdit.PartForm ?? "1",
            ["isForeigner"]    = "false",
            ["emailConfirmed"] = "true",
            ["paymentStatus"]  = "Confirmed"
        });

        await PostAsync(admin, "DeleteUser", new Dictionary<string, string>
        {
            ["id"] = toDelete.Id
        });
        Forget(toDelete.Email!);

        var tab = AuditTab(await PanelAsync(admin));

        var options = System.Text.RegularExpressions.Regex
            .Matches(tab, "<option value=\"(?<v>[^\"]+)\">")
            .Select(m => m.Groups["v"].Value)
            .ToList();

        // The menu has to offer each of these, and each has to find a row.
        foreach (var wanted in new[] { "Payment", "Verification", "Admin Edit", "User Deleted", "Login" })
        {
            Assert.Contains(wanted, options);

            var found = System.Text.RegularExpressions.Regex
                .Matches(tab, "data-action=\"(?<a>[^\"]*)\"")
                .Select(m => m.Groups["a"].Value)
                .Any(a => a.Contains(wanted, StringComparison.OrdinalIgnoreCase));

            Assert.True(found, $"Филтърът „{wanted}“ не среща нито един ред в одита.");
        }
    }

    [Fact]
    public async Task Адресът_на_клиента_влиза_в_записа()
    {
        var user = await NewParticipantAsync();

        using var admin = await SignedInAdminAsync();
        await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id, ["method"] = "IBAN"
        });

        var entry = (await App.Db.AuditForAsync(user.Email!))
            .First(a => a.Action == "Payment Confirmed — Admin");

        Assert.Equal(admin.ClientIp, entry.IpAddress);
        Assert.NotEqual("127.0.0.1", entry.IpAddress);
    }
}
