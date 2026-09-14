// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using ConferenceApp.Models;
using ConferenceApp.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Tests.Admin;

/// <summary>
/// Part 6, the Registrations tab: the list, the search, the filters, the export,
/// editing and deleting.
/// <para>
/// The search and the filters live on the client: the server renders every row
/// and puts <c>data-search</c>, <c>data-payment</c>, <c>data-type</c> and
/// <c>data-account</c> on each of them, and the script hides rows by those. What
/// is checked here is that the values are right; the hiding itself is in the
/// browser test.
/// </para>
/// </summary>
public class ParticipantsTests : AdminTestBase
{
    public ParticipantsTests(AppFixture app) : base(app, "ad-par") { }

    private const string ServiceAccount = "sys.auth_7x9b@conference.unwe.bg";

    private static string RowOf(string html, string email)
    {
        var index = html.IndexOf(email, StringComparison.OrdinalIgnoreCase);
        Assert.True(index > 0, $"{email} не се вижда в панела.");

        var start = html.LastIndexOf("<tr", index, StringComparison.Ordinal);
        var end = html.IndexOf("</tr>", index, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Не намирам реда на участника.");

        return html[start..end];
    }

    // ════════════════════════════════════════════════════════════════════
    // The list
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Списъкът_показва_новорегистрирания()
    {
        var user = await NewParticipantAsync("2");
        using var admin = await SignedInAdminAsync();

        var html = await PanelAsync(admin);
        var row = RowOf(html, user.Email!);

        Assert.Contains(user.FirstName!, row);
        Assert.Contains(user.LastName!, row);
        Assert.Contains(user.ReferenceNumber!, row);
        Assert.Contains("Student / PhD Candidate", row);
    }

    [Fact]
    public async Task Редът_носи_стойностите_за_търсене_и_филтри()
    {
        var user = await NewParticipantAsync("4", paymentStatus: "Confirmed");
        using var admin = await SignedInAdminAsync();

        var row = RowOf(await PanelAsync(admin), user.Email!);

        Assert.Contains("data-payment=\"Confirmed\"", row);
        Assert.Contains("data-type=\"4\"", row);
        Assert.Contains("data-account=\"Verified\"", row);

        // data-search is lower-cased, because the script compares it against what
        // was typed, lower-cased as well.
        Assert.Contains($"data-search=\"", row);
        Assert.Contains(user.Email!.ToLowerInvariant(), row);
        Assert.Contains(user.ReferenceNumber!.ToLowerInvariant(), row);
    }

    /// <summary>
    /// The system account is the administrator themselves: their name belongs in
    /// the panel's header but has no business in the list of participants. The
    /// assertion is therefore made against the table rather than the whole page.
    /// </summary>
    [Fact]
    public async Task Служебният_акаунт_не_е_в_списъка_с_участници()
    {
        using var admin = await SignedInAdminAsync();
        var html = await PanelAsync(admin);

        var start = html.IndexOf("id=\"registrationsTableBody\"", StringComparison.Ordinal);
        Assert.True(start > 0, "Не намирам таблицата с регистрациите.");

        var end = html.IndexOf("</table>", start, StringComparison.Ordinal);
        var table = html[start..end];

        Assert.DoesNotContain(ServiceAccount, table);
    }

    [Fact]
    public async Task Броячите_отгоре_съвпадат_с_базата()
    {
        await NewParticipantAsync(paymentStatus: "Confirmed");
        await NewParticipantAsync(paymentStatus: "Pending");

        using var admin = await SignedInAdminAsync();
        var html = await PanelAsync(admin);

        var total = await App.Db.ReadAsync(db =>
            db.Users.CountAsync(u => u.Email != ServiceAccount));
        var confirmed = await App.Db.ReadAsync(db =>
            db.Users.CountAsync(u => u.Email != ServiceAccount && u.PaymentStatus == "Confirmed"));

        Assert.Contains($">{total}</strong>", html);
        Assert.Contains($">{confirmed}</strong>", html);
    }

    // ════════════════════════════════════════════════════════════════════
    // Export
    // ════════════════════════════════════════════════════════════════════

    private static async Task<string> CsvAsync(HttpSession admin, string query)
    {
        using var response = await admin.Client.GetAsync($"/Admin?handler={query}");
        response.EnsureSuccessStatusCode();

        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Износът_на_всички_съдържа_участника()
    {
        var user = await NewParticipantAsync("3");
        using var admin = await SignedInAdminAsync();

        var csv = await CsvAsync(admin, "ExportRegistrations&type=all");

        Assert.StartsWith("﻿", csv);           // a BOM, or Excel reads the Cyrillic wrongly
        Assert.Contains("First Name,Last Name,Age", csv);
        Assert.Contains(user.Email!, csv);
        Assert.Contains(user.ReferenceNumber!, csv);
        Assert.Contains("Online Participant", csv);
        Assert.DoesNotContain(ServiceAccount, csv);
    }

    [Fact]
    public async Task Износът_по_статус_подбира()
    {
        var paid    = await NewParticipantAsync(paymentStatus: "Confirmed");
        var unpaid  = await NewParticipantAsync(paymentStatus: "Pending");

        using var admin = await SignedInAdminAsync();

        var confirmed = await CsvAsync(admin, "ExportRegistrations&type=confirmed");
        Assert.Contains(paid.Email!, confirmed);
        Assert.DoesNotContain(unpaid.Email!, confirmed);

        var pending = await CsvAsync(admin, "ExportRegistrations&type=pending");
        Assert.Contains(unpaid.Email!, pending);
        Assert.DoesNotContain(paid.Email!, pending);
    }

    [Fact]
    public async Task Износът_на_отказаните_не_вади_останалите()
    {
        var cancelled = await NewParticipantAsync(paymentStatus: "Cancelled");
        var pending   = await NewParticipantAsync(paymentStatus: "Pending");

        using var admin = await SignedInAdminAsync();
        var csv = await CsvAsync(admin, "ExportRegistrations&type=cancelled");

        Assert.Contains(cancelled.Email!, csv);
        Assert.DoesNotContain(pending.Email!, csv);
    }

    [Fact]
    public async Task Износът_на_одита_съдържа_последното_действие()
    {
        var user = await NewParticipantAsync();
        using var admin = await SignedInAdminAsync();

        var reply = await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id,
            ["method"] = "IBAN"
        });
        Assert.True(reply.Success, reply.Message);

        var csv = await CsvAsync(admin, "ExportAuditLogs");

        Assert.StartsWith("﻿", csv);
        Assert.Contains("Date & Time (BG),User Email,Action,IP Address,Details", csv);
        Assert.Contains("Payment Confirmed — Admin", csv);
        Assert.Contains(user.Email!, csv);
    }

    [Fact]
    public async Task Износът_не_изнася_паролата_на_никого()
    {
        using var admin = await SignedInAdminAsync();

        var registrations = await CsvAsync(admin, "ExportRegistrations&type=all");
        var audit = await CsvAsync(admin, "ExportAuditLogs");

        foreach (var csv in new[] { registrations, audit })
        {
            Assert.DoesNotContain(App.Credentials.AdminPassword, csv);
            Assert.DoesNotContain("PasswordHash", csv);
            Assert.DoesNotContain("AQAAAA", csv);     // the start of an Identity hash
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Editing
    // ════════════════════════════════════════════════════════════════════

    private static Dictionary<string, string> EditForm(ApplicationUser user) => new()
    {
        ["id"]                 = user.Id,
        ["firstName"]          = user.FirstName ?? "",
        ["lastName"]           = user.LastName ?? "",
        ["age"]                = (user.Age).ToString(),
        ["phone"]              = user.PhoneNumber ?? "",
        ["academicTitle"]      = user.AcademicTitle ?? "",
        ["organization"]       = user.Workplace ?? "",
        ["participation"]      = user.PartForm ?? "1",
        ["isForeigner"]        = user.IsForeigner ? "true" : "false",
        ["emailConfirmed"]     = user.EmailConfirmed ? "true" : "false",
        ["paymentStatus"]      = user.PaymentStatus ?? "Pending",
        ["verificationStatus"] = user.VerificationStatus ?? "None"
    };

    /// <summary>
    /// The handler's early return has to undo the changes, not merely leave them
    /// unsaved by itself.
    /// <para>
    /// <c>user</c> is tracked by EF. The handler assigns the fields BEFORE
    /// <c>UserManager.UpdateAsync</c> and returns early if it fails, so the
    /// mutations stay in the change tracker. Whether they reach the disk depends on
    /// whether anything calls <c>SaveChangesAsync</c> on the same scoped
    /// <c>DbContext</c> after the handler. For <c>EditTicket</c> something did
    /// (<c>AdminAuditFilter</c>), and that was [T-20]; these five handlers are in
    /// <c>SelfLogging</c> and the filter stays quiet for them. The test checks the
    /// conclusion rather than the reasoning.
    /// </para>
    /// <para>
    /// <c>UpdateAsync</c> is made to fail by force: the e-mail in the row is
    /// corrupted from outside and Identity runs with <c>RequireUniqueEmail</c>, so
    /// its validator refuses. The handler does not touch the e-mail, which makes
    /// this the only way to reach that branch without changing the code.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Провалена_редакция_не_записва_нищо()
    {
        var user = await NewParticipantAsync();
        using var admin = await SignedInAdminAsync();

        var email = user.Email!;
        var originalFirstName = user.FirstName;

        // The e-mail is corrupted from OUTSIDE, so that Identity's validator
        // refuses.
        await App.Db.WriteAsync(async db =>
        {
            var row = await db.Users.FirstAsync(u => u.Id == user.Id);
            row.Email = "not-an-email";
        });

        var form = EditForm(user);
        form["firstName"]    = "Не-трябва-да-остане";
        form["organization"] = "Не-трябва-да-остане";

        var reply = await PostAsync(admin, "SaveRegistration", form);
        Assert.False(reply.Success, "Очаква се отказ — валидаторът на Identity трябва да падне.");

        // A test that passes for the wrong reason is worth nothing: the refusal has
        // to come from UpdateAsync rather than from one of the early checks before
        // the assignments — an empty name, the age, a missing participant.
        Assert.Contains("mail", reply.Message, StringComparison.OrdinalIgnoreCase);

        var after = await App.Db.ReadAsync(db =>
            db.Users.AsNoTracking().FirstAsync(u => u.Id == user.Id));

        Assert.Equal(originalFirstName, after.FirstName);
        Assert.NotEqual("Не-трябва-да-остане", after.Workplace);

        // The e-mail is restored so that the cleanup can find the participant.
        await App.Db.WriteAsync(async db =>
        {
            var row = await db.Users.FirstAsync(u => u.Id == user.Id);
            row.Email = email;
        });
    }

    [Fact]
    public async Task Редакцията_записва_и_оставя_следа()
    {
        var user = await NewParticipantAsync();
        using var admin = await SignedInAdminAsync();

        var form = EditForm(user);
        form["firstName"]    = "Renamed";
        form["organization"] = "New Institute";
        form["age"]          = "45";

        var reply = await PostAsync(admin, "SaveRegistration", form);
        Assert.True(reply.Success, reply.Message);

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Renamed", after!.FirstName);
        Assert.Equal("New Institute", after.Workplace);
        Assert.Equal(45, after.Age);

        var audit = await App.Db.AuditForAsync(user.Email!);
        var edit = audit.FirstOrDefault(a => a.Action == "Admin Edit");
        Assert.NotNull(edit);
        Assert.Contains("First Name", edit!.Details);
    }

    [Theory]
    [InlineData("age", "15")]
    [InlineData("age", "101")]
    [InlineData("firstName", "")]
    [InlineData("lastName", "")]
    public async Task Редакцията_отказва_невалидна_стойност(string field, string value)
    {
        var user = await NewParticipantAsync();
        using var admin = await SignedInAdminAsync();

        var form = EditForm(user);
        form[field] = value;

        var reply = await PostAsync(admin, "SaveRegistration", form);
        Assert.False(reply.Success);
        Assert.NotEqual(string.Empty, reply.Message);

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal(user.FirstName, after!.FirstName);
        Assert.Equal(user.Age, after.Age);
    }

    [Fact]
    public async Task Непознат_статус_на_верификация_не_влиза()
    {
        var user = await NewParticipantAsync("2");
        await App.Db.SetVerificationAsync(user.Email!, "Pending");

        using var admin = await SignedInAdminAsync();

        var form = EditForm(user);
        form["verificationStatus"] = "Approved-ish";

        var reply = await PostAsync(admin, "SaveRegistration", form);
        Assert.True(reply.Success, reply.Message);

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Pending", after!.VerificationStatus);
    }

    [Fact]
    public async Task Редакция_на_несъществуващ_участник_казва_какво_има()
    {
        using var admin = await SignedInAdminAsync();

        var reply = await PostAsync(admin, "SaveRegistration", new Dictionary<string, string>
        {
            ["id"]            = Guid.NewGuid().ToString(),
            ["firstName"]     = "Nobody",
            ["lastName"]      = "Here",
            ["age"]           = "40",
            ["phone"]         = "+359 888 000111",
            ["academicTitle"] = "-",
            ["organization"]  = "-",
            ["participation"] = "1",
            ["isForeigner"]   = "false",
            ["emailConfirmed"] = "true",
            ["paymentStatus"] = "Pending"
        });

        Assert.False(reply.Success);
        Assert.Contains("not found", reply.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ════════════════════════════════════════════════════════════════════
    // Deleting
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Изтриването_маха_участника_кода_и_файла()
    {
        var user = await NewParticipantAsync();

        // A row in OtpCodes: signing in leaves one behind.
        using (var session = await SignedInAsync(user)) { }

        var relative = $"uploads/papers26/{Guid.NewGuid():N}.pdf";
        var physical = PrivatePhysicalPath(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        await File.WriteAllBytesAsync(physical, UploadFile.Pdf("x").Content);
        await App.Db.SetPaperPathAsync(user.Email!, relative);

        using var admin = await SignedInAdminAsync();
        var reply = await PostAsync(admin, "DeleteUser", new Dictionary<string, string>
        {
            ["id"] = user.Id
        });
        Assert.True(reply.Success, reply.Message);

        Forget(user.Email!);

        Assert.Null(await App.Db.FindUserAsync(user.Email!));
        Assert.Empty(await App.Db.OtpCodesAsync(user.Email!));
        Assert.False(File.Exists(physical), "Докладът на изтрит участник остана на диска.");
    }

    /// <summary>
    /// [D-06], an explicitly requested check: deleting a participant must NOT take
    /// their crypto history with them. It is the only evidence on our side in a
    /// dispute over "I paid and I am not there".
    /// </summary>
    [Fact]
    public async Task Изтрит_участник_оставя_крипто_поръчката_с_празен_собственик()
    {
        var user = await NewParticipantAsync();
        var external = $"{Tag}-{Guid.NewGuid():N}";

        await App.Db.WriteAsync(db => db.CryptoOrders.AddAsync(new CryptoOrder
        {
            UserId        = user.Id,
            Go28OrderId   = Random.Shared.Next(100000, 999999),
            ExternalId    = external,
            Currency      = "USDC",
            Network       = "ETH",
            AmountEUR     = 60m,
            CryptoAmount  = "65.10",
            WalletAddress = "0xTESTWALLET",
            Status        = "Confirmed",
            CompletedAt   = DateTime.UtcNow
        }).AsTask());

        using var admin = await SignedInAdminAsync();
        var reply = await PostAsync(admin, "DeleteUser", new Dictionary<string, string>
        {
            ["id"] = user.Id
        });
        Assert.True(reply.Success, reply.Message);

        Forget(user.Email!);
        Assert.Null(await App.Db.FindUserAsync(user.Email!));

        var order = await App.Db.ReadAsync(db => db.CryptoOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.ExternalId == external));

        Assert.NotNull(order);
        Assert.Null(order!.UserId);
        Assert.Equal("Confirmed", order.Status);
        Assert.Equal(60m, order.AmountEUR);
        Assert.Equal("0xTESTWALLET", order.WalletAddress);
    }

    [Fact]
    public async Task Изтриването_оставя_запис_в_одита()
    {
        var user = await NewParticipantAsync();
        var email = user.Email!;

        using var admin = await SignedInAdminAsync();
        await PostAsync(admin, "DeleteUser", new Dictionary<string, string> { ["id"] = user.Id });
        Forget(email);

        var audit = await App.Db.AuditForAsync(email);
        var entry = audit.FirstOrDefault(a => a.Action == "User Deleted");

        Assert.NotNull(entry);
        Assert.Contains(user.ReferenceNumber!, entry!.Details);
    }

    [Fact]
    public async Task Изтриване_на_несъществуващ_не_гърми()
    {
        using var admin = await SignedInAdminAsync();

        var reply = await PostAsync(admin, "DeleteUser", new Dictionary<string, string>
        {
            ["id"] = Guid.NewGuid().ToString()
        });

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.False(reply.Success);
        Assert.Contains("not found", reply.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ════════════════════════════════════════════════════════════════════
    // The lookups behind the dialogs
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Справката_за_действията_на_участник_връща_неговите()
    {
        var mine  = await NewParticipantAsync();
        var other = await NewParticipantAsync();

        using (var session = await SignedInAsync(mine)) { }
        using (var session = await SignedInAsync(other)) { }

        using var admin = await SignedInAdminAsync();

        using var response = await admin.Client.GetAsync(
            $"/Admin?handler=FetchUserAudits&email={Uri.EscapeDataString(mine.Email!)}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("action", json);
        Assert.DoesNotContain(other.Email!, json);
    }
}
