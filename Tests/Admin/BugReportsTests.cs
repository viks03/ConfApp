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
/// Part 6, the bug reports: the floating widget an administrator sees on every
/// page, and the review of them at <c>/Admin/BugReports</c>.
/// <para>
/// The widget is shown only to an administrator, but that is a decision made by
/// the view. So the endpoint itself — <c>POST /api/bug-reports/submit</c> — is
/// also tried as a participant and from outside.
/// </para>
/// </summary>
public class BugReportsTests : AdminTestBase
{
    public BugReportsTests(AppFixture app) : base(app, "ad-bug") { }

    private async Task<AdminReply> SubmitAsync(
        HttpSession session, string title, string description,
        string category = "Bug", string severity = "High",
        string? pageUrl = "/Schedule", string? tokenFrom = "/Admin")
    {
        var token = await session.AntiforgeryTokenAsync(tokenFrom!);

        // Redirects are not followed: a refusal is a 302 to /AccessDenied, which
        // answers 200, so otherwise the refusal would look like a success.
        using var client = session.NoRedirectClient();
        using var response = await client.PostAsync("/api/bug-reports/submit",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["title"]       = title,
                ["description"] = description,
                ["category"]    = category,
                ["severity"]    = severity,
                ["pageUrl"]     = pageUrl ?? "",
                ["userAgent"]   = "Mozilla/5.0 (Macintosh) Chrome/140.0 Safari/537.36",
                ["__RequestVerificationToken"] = token
            }));

        var reply = await AdminReply.ReadAsync(response);

        if (reply.Success)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(reply.Raw);
            TrackBugReport(doc.RootElement.GetProperty("id").GetInt32());
        }

        return reply;
    }

    private Task<BugReport?> ReportAsync(string title) =>
        App.Db.ReadAsync(db => db.BugReports.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Title == title));

    // ════════════════════════════════════════════════════════════════════
    // The widget
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Виджетът_се_вижда_от_администратора_на_всяка_страница()
    {
        using var admin = await SignedInAdminAsync();

        foreach (var path in new[] { "/", "/Schedule", "/Admin" })
        {
            using var response = await admin.Client.GetAsync(path);
            response.EnsureSuccessStatusCode();

            var html = await response.ReadPageAsync();
            Assert.Contains("brwFab", html);
        }
    }

    [Fact]
    public async Task Виджетът_не_се_вижда_от_участник_и_отвън()
    {
        var user = await NewParticipantAsync();
        using var session = await SignedInAsync(user);

        var mine = await (await session.Client.GetAsync("/Schedule")).ReadPageAsync();
        Assert.DoesNotContain("brwFab", mine);

        using var client = App.NewClient();
        var anonymous = await (await client.GetAsync("/Schedule")).ReadPageAsync();
        Assert.DoesNotContain("brwFab", anonymous);
    }

    // ════════════════════════════════════════════════════════════════════
    // Submitting
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Подаденият_сигнал_влиза_в_базата_отворен()
    {
        var title = $"Сигнал {Guid.NewGuid():N}";

        using var admin = await SignedInAdminAsync();
        var reply = await SubmitAsync(admin, title, "Бутонът не реагира на клик.");

        Assert.True(reply.Success, reply.Message);

        var saved = await ReportAsync(title);
        Assert.NotNull(saved);
        Assert.Equal("Open", saved!.Status);
        Assert.Equal("Bug", saved.Category);
        Assert.Equal("High", saved.Severity);
        Assert.Equal("/Schedule", saved.PageUrl);
        Assert.Equal(App.Credentials.AdminEmail, saved.ReportedByEmail);
        Assert.Equal(admin.ClientIp, saved.IpAddress);
    }

    [Fact]
    public async Task Подаденият_сигнал_праща_известие_по_пощата()
    {
        var title = $"Сигнал {Guid.NewGuid():N}";

        using var admin = await SignedInAdminAsync();
        App.Smtp.Clear();

        await SubmitAsync(admin, title, "Описание на проблема.");

        var mail = await App.Smtp.WaitForAsync("viktor.georgiev@icbi.bg", TimeSpan.FromSeconds(10));
        Assert.NotNull(mail);
        Assert.Contains(title, Html.Text(mail!.Subject) + Html.Text(mail.Body));
    }

    [Theory]
    [InlineData("", "Описание", "Title")]
    [InlineData("Заглавие", "", "Description")]
    [InlineData("   ", "Описание", "Title")]
    public async Task Сигнал_без_заглавие_или_описание_се_отказва(
        string title, string description, string expected)
    {
        var before = await App.Db.ReadAsync(db => db.BugReports.CountAsync());

        using var admin = await SignedInAdminAsync();
        var reply = await SubmitAsync(admin, title, description);

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Contains(expected, reply.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await App.Db.ReadAsync(db => db.BugReports.CountAsync()));
    }

    [Fact]
    public async Task Непозната_категория_и_тежест_падат_на_безопасна_стойност()
    {
        var title = $"Сигнал {Guid.NewGuid():N}";

        using var admin = await SignedInAdminAsync();
        var reply = await SubmitAsync(admin, title, "Описание.",
            category: "Whatever", severity: "Apocalyptic");

        Assert.True(reply.Success, reply.Message);

        var saved = await ReportAsync(title);
        Assert.Equal("Other", saved!.Category);
        Assert.Equal("Medium", saved.Severity);
    }

    [Fact]
    public async Task Участник_не_може_да_подаде_сигнал()
    {
        var user = await NewParticipantAsync();
        using var session = await SignedInAsync(user);

        var before = await App.Db.ReadAsync(db => db.BugReports.CountAsync());
        var reply = await SubmitAsync(session, "Чужд сигнал", "Описание.", tokenFrom: "/Profile");

        Assert.NotEqual(HttpStatusCode.OK, reply.Status);
        Assert.Equal(before, await App.Db.ReadAsync(db => db.BugReports.CountAsync()));
    }

    [Fact]
    public async Task Външен_не_може_да_подаде_сигнал()
    {
        var before = await App.Db.ReadAsync(db => db.BugReports.CountAsync());

        using var client = App.NewClient(followRedirects: false);
        using var response = await client.PostAsync("/api/bug-reports/submit",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["title"]       = "Анонимен сигнал",
                ["description"] = "Описание."
            }));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, await App.Db.ReadAsync(db => db.BugReports.CountAsync()));
    }

    // ════════════════════════════════════════════════════════════════════
    // Reviewing
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Сигналът_се_вижда_в_прегледа()
    {
        var title = $"Сигнал {Guid.NewGuid():N}";

        using var admin = await SignedInAdminAsync();
        await SubmitAsync(admin, title, "Търсеното описание.");

        using var response = await admin.Client.GetAsync("/Admin/BugReports");
        response.EnsureSuccessStatusCode();

        var html = await response.ReadPageAsync();
        Assert.Contains(title, html);
        Assert.Contains("Търсеното описание.", html);
    }

    [Fact]
    public async Task Броячът_в_лентата_брои_отворените()
    {
        using var admin = await SignedInAdminAsync();

        var open = await App.Db.ReadAsync(db => db.BugReports.CountAsync(b => b.Status == "Open"));

        var title = $"Сигнал {Guid.NewGuid():N}";
        await SubmitAsync(admin, title, "Описание.");

        var html = await PanelAsync(admin);
        Assert.Contains($">{open + 1}</span>", html);
    }

    // ════════════════════════════════════════════════════════════════════
    // Changing the status
    // ════════════════════════════════════════════════════════════════════

    private static async Task<AdminReply> SetStatusAsync(
        HttpSession admin, int id, string status, string? notes = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["id"]     = id.ToString(),
            ["status"] = status
        };
        if (notes != null) fields["resolutionNotes"] = notes;

        using var response = await admin.PostHandlerAsync("/Admin/BugReports", "UpdateStatus", fields);
        return await AdminReply.ReadAsync(response);
    }

    [Theory]
    [InlineData("InProgress")]
    [InlineData("Resolved")]
    [InlineData("WontFix")]
    [InlineData("Open")]
    public async Task Познатият_статус_се_записва(string status)
    {
        var title = $"Сигнал {Guid.NewGuid():N}";

        using var admin = await SignedInAdminAsync();
        await SubmitAsync(admin, title, "Описание.");

        var saved = await ReportAsync(title);
        var reply = await SetStatusAsync(admin, saved!.Id, status);

        Assert.True(reply.Success, reply.Message);
        Assert.Equal(status, (await ReportAsync(title))!.Status);
    }

    [Fact]
    public async Task Затварянето_записва_кой_кога_и_защо()
    {
        var title = $"Сигнал {Guid.NewGuid():N}";
        const string notes = "Оправено в 1.1.0 — беше грешен селектор.";

        using var admin = await SignedInAdminAsync();
        await SubmitAsync(admin, title, "Описание.");

        var saved = await ReportAsync(title);
        Assert.True((await SetStatusAsync(admin, saved!.Id, "Resolved", notes)).Success);

        var after = await ReportAsync(title);
        Assert.Equal("Resolved", after!.Status);
        Assert.Equal(notes, after.ResolutionNotes);
        Assert.Equal(App.Credentials.AdminEmail, after.ResolvedByEmail);
        Assert.NotNull(after.ResolvedAt);
    }

    /// <summary>
    /// Reopening has to clear the note and the date; otherwise a report closed a
    /// month ago comes back carrying an explanation that no longer applies.
    /// </summary>
    [Fact]
    public async Task Преотварянето_изчиства_данните_от_затварянето()
    {
        var title = $"Сигнал {Guid.NewGuid():N}";

        using var admin = await SignedInAdminAsync();
        await SubmitAsync(admin, title, "Описание.");

        var saved = await ReportAsync(title);
        await SetStatusAsync(admin, saved!.Id, "Resolved", "Оправено.");
        await SetStatusAsync(admin, saved.Id, "Open");

        var after = await ReportAsync(title);
        Assert.Equal("Open", after!.Status);
        Assert.Null(after.ResolutionNotes);
        Assert.Null(after.ResolvedAt);
        Assert.Null(after.ResolvedByEmail);
    }

    [Fact]
    public async Task Непознат_статус_се_отказва()
    {
        var title = $"Сигнал {Guid.NewGuid():N}";

        using var admin = await SignedInAdminAsync();
        await SubmitAsync(admin, title, "Описание.");

        var saved = await ReportAsync(title);
        var reply = await SetStatusAsync(admin, saved!.Id, "Maybe");

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Equal("Open", (await ReportAsync(title))!.Status);
    }

    [Fact]
    public async Task Смяна_на_статус_на_несъществуващ_е_404()
    {
        using var admin = await SignedInAdminAsync();
        var reply = await SetStatusAsync(admin, 999999, "Resolved");

        Assert.Equal(HttpStatusCode.NotFound, reply.Status);
    }

    [Fact]
    public async Task Участник_не_мени_статуса()
    {
        var title = $"Сигнал {Guid.NewGuid():N}";

        using (var admin = await SignedInAdminAsync())
            await SubmitAsync(admin, title, "Описание.");

        var saved = await ReportAsync(title);

        var user = await NewParticipantAsync();
        using var session = await SignedInAsync(user);
        var token = await session.AntiforgeryTokenAsync("/Profile");

        using var client = session.NoRedirectClient();
        using var response = await client.PostAsync("/Admin/BugReports?handler=UpdateStatus",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["id"]     = saved!.Id.ToString(),
                ["status"] = "WontFix",
                ["__RequestVerificationToken"] = token
            }));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Open", (await ReportAsync(title))!.Status);
    }

    // ════════════════════════════════════════════════════════════════════
    // Deleting
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Сигналът_се_изтрива_трайно()
    {
        var title = $"Сигнал {Guid.NewGuid():N}";

        using var admin = await SignedInAdminAsync();
        await SubmitAsync(admin, title, "Описание.");

        var saved = await ReportAsync(title);

        using var response = await admin.PostHandlerAsync("/Admin/BugReports", "Delete",
            new Dictionary<string, string> { ["id"] = saved!.Id.ToString() });

        var reply = await AdminReply.ReadAsync(response);
        Assert.True(reply.Success, reply.Message);
        Assert.Null(await ReportAsync(title));
    }
}
