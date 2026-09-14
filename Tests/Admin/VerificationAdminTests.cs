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
/// Part 6, the Verifications tab: approving and rejecting the documents a student
/// or a journalist submits through <c>/SubmitDocuments</c> (part 4).
/// <para>
/// An approval is not merely a change of status: for these two forms of
/// participation it also confirms the payment as <c>Subsidised</c>. The test
/// therefore looks at both sides, and at the message.
/// </para>
/// </summary>
public class VerificationAdminTests : AdminTestBase
{
    public VerificationAdminTests(AppFixture app) : base(app, "ad-ver") { }

    /// <summary>A participant with documents submitted and an image on disk.</summary>
    private async Task<(ApplicationUser User, string Relative, string Physical)>
        PendingVerificationAsync(string partForm = "2")
    {
        var user = await NewParticipantAsync(partForm);

        var folder = partForm == "2" ? "students" : "journalists";
        var relative = $"uploads/submitted-documents/{folder}/{Guid.NewGuid():N}.png";
        var physical = PrivatePhysicalPath(relative);

        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        await File.WriteAllBytesAsync(physical, UploadFile.Png("x").Content);

        await App.Db.SetVerificationAsync(user.Email!, "Pending", relative);
        return (user, relative, physical);
    }

    private static async Task<CapturedMail?> WaitForVerificationMailAsync(
        AppFixture app, string email, TimeSpan timeout) =>
        await app.Smtp.WaitForAsync(email, timeout,
            m => !m.Subject.Contains("код", StringComparison.OrdinalIgnoreCase)
              && !m.Subject.Contains("code", StringComparison.OrdinalIgnoreCase));

    // ════════════════════════════════════════════════════════════════════
    // The early return
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The four participant handlers assign the fields BEFORE
    /// <c>UserManager.UpdateAsync</c> and return early if it fails. <c>user</c> is
    /// tracked by EF, so the mutations stay in the change tracker; whether they
    /// reach the disk depends on whether anything calls <c>SaveChangesAsync</c> on
    /// the same scoped <c>DbContext</c> after the handler. For <c>EditTicket</c>
    /// something did (<c>AdminAuditFilter</c>), and that was [T-20]. These four are
    /// in <c>SelfLogging</c> and the filter stays quiet for them — the test checks
    /// the conclusion rather than the reasoning.
    /// </summary>
    [Theory]
    [InlineData("ConfirmPayment")]
    [InlineData("CancelPayment")]
    [InlineData("ApproveVerification")]
    [InlineData("RejectVerification")]
    public async Task Провалено_действие_не_записва_нищо(string handler)
    {
        var (user, _, _) = await PendingVerificationAsync();
        var email = user.Email!;

        // UpdateAsync is made to fail by force: the e-mail is corrupted from
        // outside and Identity runs with RequireUniqueEmail, so its validator
        // refuses. None of the four handlers touches the e-mail, which makes this
        // the only way to reach that branch without changing the code.
        await App.Db.WriteAsync(async db =>
        {
            var row = await db.Users.FirstAsync(u => u.Id == user.Id);
            row.Email = "not-an-email";
        });

        var form = new Dictionary<string, string> { ["userId"] = user.Id };
        if (handler == "ConfirmPayment")      form["method"] = "IBAN";
        if (handler == "RejectVerification")  form["reason"] = "Документът е нечетим.";

        using var admin = await SignedInAdminAsync();
        var reply = await PostAsync(admin, handler, form);

        Assert.False(reply.Success, $"{handler}: очаква се отказ от валидатора на Identity.");

        // A test that passes for the wrong reason is worth nothing: the refusal has
        // to come from UpdateAsync rather than from an early check before the
        // assignments.
        Assert.Contains("mail", reply.Message, StringComparison.OrdinalIgnoreCase);

        var after = await App.Db.ReadAsync(db =>
            db.Users.AsNoTracking().FirstAsync(u => u.Id == user.Id));

        Assert.Equal("Pending", after.VerificationStatus);
        Assert.Null(after.VerificationRejectionReason);
        Assert.Equal("Pending", after.PaymentStatus);
        Assert.Null(after.PaidAt);
        Assert.Null(after.PaidAmountEUR);

        await App.Db.WriteAsync(async db =>
        {
            var row = await db.Users.FirstAsync(u => u.Id == user.Id);
            row.Email = email;
        });
    }

    // ════════════════════════════════════════════════════════════════════
    // The list
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Подалият_документи_излиза_в_чакащите()
    {
        var (user, _, _) = await PendingVerificationAsync();

        using var admin = await SignedInAdminAsync();
        var html = await PanelAsync(admin);

        var tab = html[html.IndexOf("id=\"tab-verifications\"", StringComparison.Ordinal)..];
        Assert.Contains(user.Email!, tab);
    }

    [Fact]
    public async Task Лектор_не_излиза_в_чакащите_дори_с_подадени_документи()
    {
        var user = await NewParticipantAsync("1");
        await App.Db.SetVerificationAsync(user.Email!, "Pending", "uploads/submitted-documents/students/x.png");

        using var admin = await SignedInAdminAsync();
        var html = await PanelAsync(admin);

        var tab = html[html.IndexOf("id=\"tab-verifications\"", StringComparison.Ordinal)..];
        tab = tab[..tab.IndexOf("id=\"tab-", 10, StringComparison.Ordinal)];

        Assert.DoesNotContain(user.Email!, tab);
    }

    // ════════════════════════════════════════════════════════════════════
    // Approving
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("2")]
    [InlineData("4")]
    public async Task Одобряването_сменя_статуса_и_потвърждава_плащането(string partForm)
    {
        var (user, _, _) = await PendingVerificationAsync(partForm);

        using var admin = await SignedInAdminAsync();
        var reply = await PostAsync(admin, "ApproveVerification", new Dictionary<string, string>
        {
            ["userId"] = user.Id
        });

        Assert.True(reply.Success, reply.Message);
        Assert.Contains("paymentAutoConfirmed", reply.Raw);

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Approved", after!.VerificationStatus);
        Assert.Null(after.VerificationRejectionReason);
        Assert.Equal("Confirmed", after.PaymentStatus);
        Assert.Equal("Subsidised", after.PaymentMethod);
        Assert.NotNull(after.PaidAt);
    }

    [Fact]
    public async Task Одобряването_не_пипа_вече_платено()
    {
        var (user, _, _) = await PendingVerificationAsync();

        await App.Db.WriteAsync(async db =>
        {
            var row = await db.Users.FirstAsync(u => u.Email == user.Email);
            row.PaymentStatus = "Confirmed";
            row.PaymentMethod = "Stripe:Card";
            row.PaidAmountEUR = 60m;
            row.PaidAt = DateTime.UtcNow.AddDays(-2);
        });

        using var admin = await SignedInAdminAsync();
        await PostAsync(admin, "ApproveVerification", new Dictionary<string, string>
        {
            ["userId"] = user.Id
        });

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Approved", after!.VerificationStatus);
        Assert.Equal("Stripe:Card", after.PaymentMethod);
        Assert.Equal(60m, after.PaidAmountEUR);
    }

    [Fact]
    public async Task Одобряването_праща_писмо_и_оставя_запис()
    {
        var (user, _, _) = await PendingVerificationAsync();
        App.Smtp.Clear();

        using var admin = await SignedInAdminAsync();
        await PostAsync(admin, "ApproveVerification", new Dictionary<string, string>
        {
            ["userId"] = user.Id
        });

        var mail = await WaitForVerificationMailAsync(App, user.Email!, TimeSpan.FromSeconds(10));
        Assert.NotNull(mail);

        var audit = await App.Db.AuditForAsync(user.Email!);
        var entry = audit.FirstOrDefault(a => a.Action == "Verification Approved");
        Assert.NotNull(entry);
        Assert.Contains("Payment auto-confirmed: Yes", entry!.Details);
    }

    [Fact]
    public async Task Второ_одобряване_не_прави_нищо()
    {
        var (user, _, _) = await PendingVerificationAsync();

        using var admin = await SignedInAdminAsync();
        Assert.True((await PostAsync(admin, "ApproveVerification",
            new Dictionary<string, string> { ["userId"] = user.Id })).Success);

        Assert.NotNull(await WaitForVerificationMailAsync(App, user.Email!, TimeSpan.FromSeconds(10)));
        App.Smtp.Clear();

        var second = await PostAsync(admin, "ApproveVerification",
            new Dictionary<string, string> { ["userId"] = user.Id });

        Assert.False(second.Success);
        Assert.Contains("already approved", second.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await WaitForVerificationMailAsync(App, user.Email!, TimeSpan.FromSeconds(3)));
    }

    // ════════════════════════════════════════════════════════════════════
    // Rejecting
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Отхвърлянето_записва_причината()
    {
        var (user, _, _) = await PendingVerificationAsync();
        const string reason = "Снимката е нечетлива — не се вижда срокът на валидност.";

        using var admin = await SignedInAdminAsync();
        var reply = await PostAsync(admin, "RejectVerification", new Dictionary<string, string>
        {
            ["userId"] = user.Id,
            ["reason"] = reason
        });

        Assert.True(reply.Success, reply.Message);

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Rejected", after!.VerificationStatus);
        Assert.Equal(reason, after.VerificationRejectionReason);
        Assert.NotEqual("Confirmed", after.PaymentStatus);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("не")]
    [InlineData("abcd")]
    public async Task Отхвърляне_без_смислена_причина_се_отказва(string reason)
    {
        var (user, _, _) = await PendingVerificationAsync();

        using var admin = await SignedInAdminAsync();
        var reply = await PostAsync(admin, "RejectVerification", new Dictionary<string, string>
        {
            ["userId"] = user.Id,
            ["reason"] = reason
        });

        Assert.False(reply.Success);
        Assert.Contains("reason", reply.Message, StringComparison.OrdinalIgnoreCase);

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Pending", after!.VerificationStatus);
        Assert.Null(after.VerificationRejectionReason);
    }

    [Fact]
    public async Task Отхвърлянето_праща_писмо_с_причината()
    {
        var (user, _, _) = await PendingVerificationAsync();
        App.Smtp.Clear();

        const string reason = "Документът е изтекъл през 2024 г.";

        using var admin = await SignedInAdminAsync();
        await PostAsync(admin, "RejectVerification", new Dictionary<string, string>
        {
            ["userId"] = user.Id,
            ["reason"] = reason
        });

        var mail = await WaitForVerificationMailAsync(App, user.Email!, TimeSpan.FromSeconds(10));
        Assert.NotNull(mail);
        Assert.Contains(reason, Html.Text(mail!.Body));
    }

    /// <summary>
    /// The reason is free text written by an administrator. Unescaped in the
    /// message, a "&lt;" breaks the HTML — and in a worse case puts someone else's
    /// markup into someone else's mailbox.
    /// </summary>
    [Fact]
    public async Task Причината_се_екранира_в_писмото()
    {
        var (user, _, _) = await PendingVerificationAsync();
        App.Smtp.Clear();

        const string reason = "Липсва <b>печат</b> и <script>alert(1)</script>.";

        using var admin = await SignedInAdminAsync();
        await PostAsync(admin, "RejectVerification", new Dictionary<string, string>
        {
            ["userId"] = user.Id,
            ["reason"] = reason
        });

        var mail = await WaitForVerificationMailAsync(App, user.Email!, TimeSpan.FromSeconds(10));
        Assert.NotNull(mail);

        // The HTML part only: the plain-text alternative carries the reason exactly
        // as written, and rightly so — there is nothing there that can execute.
        var html = mail!.Body[mail.Body.IndexOf("<html", StringComparison.OrdinalIgnoreCase)..];

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public async Task Причината_се_чете_обратно_от_панела()
    {
        var (user, _, _) = await PendingVerificationAsync();
        const string reason = "Нужна е и служебна бележка от факултета.";

        using var admin = await SignedInAdminAsync();
        await PostAsync(admin, "RejectVerification", new Dictionary<string, string>
        {
            ["userId"] = user.Id,
            ["reason"] = reason
        });

        using var response = await admin.Client.GetAsync(
            $"/Admin?handler=FetchRejectionReason&userId={user.Id}");
        response.EnsureSuccessStatusCode();

        // The answer is JSON, so Cyrillic arrives as \uXXXX rather than as an HTML
        // entity.
        var json = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(reason, json.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Отхвърленият_може_да_бъде_одобрен_после()
    {
        var (user, _, _) = await PendingVerificationAsync();

        using var admin = await SignedInAdminAsync();
        await PostAsync(admin, "RejectVerification", new Dictionary<string, string>
        {
            ["userId"] = user.Id, ["reason"] = "Нечетлива снимка на документа."
        });

        var reply = await PostAsync(admin, "ApproveVerification", new Dictionary<string, string>
        {
            ["userId"] = user.Id
        });
        Assert.True(reply.Success, reply.Message);

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Approved", after!.VerificationStatus);
        Assert.Null(after.VerificationRejectionReason);
    }

    // ════════════════════════════════════════════════════════════════════
    // The document
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Администраторът_сваля_подадения_документ()
    {
        var (user, relative, physical) = await PendingVerificationAsync();

        using var admin = await SignedInAdminAsync();
        using var response = await admin.Client.GetAsync(
            $"/Admin?handler=DownloadVerifDoc&userId={user.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(await File.ReadAllBytesAsync(physical), bytes);
        Assert.Equal(Path.GetFileName(relative),
            response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
    }

    [Fact]
    public async Task Без_подаден_документ_свалянето_е_404()
    {
        var user = await NewParticipantAsync("2");

        using var admin = await SignedInAdminAsync();
        using var response = await admin.Client.GetAsync(
            $"/Admin?handler=DownloadVerifDoc&userId={user.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════
    // The other side: what the participant sees
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Участникът_вижда_одобрението_в_профила_си()
    {
        var (user, _, _) = await PendingVerificationAsync();

        using (var admin = await SignedInAdminAsync())
        {
            await PostAsync(admin, "ApproveVerification",
                new Dictionary<string, string> { ["userId"] = user.Id });
        }

        using var session = await SignedInAsync(user);
        using var response = await session.Client.GetAsync("/Profile");
        var html = await response.ReadPageAsync();

        // The profile's status panel carries a class per state: the same assertion
        // as in part 4, so that no text has to be copied.
        Assert.Contains("status-panel status-confirmed", html);
    }

    [Fact]
    public async Task Участникът_вижда_причината_за_отказа()
    {
        var (user, _, _) = await PendingVerificationAsync();
        const string reason = "Качената снимка е на друг документ.";

        using (var admin = await SignedInAdminAsync())
        {
            await PostAsync(admin, "RejectVerification", new Dictionary<string, string>
            {
                ["userId"] = user.Id, ["reason"] = reason
            });
        }

        using var session = await SignedInAsync(user);
        using var response = await session.Client.GetAsync("/Profile");
        var html = await response.ReadPageAsync();

        Assert.Contains(reason, html);
    }
}
