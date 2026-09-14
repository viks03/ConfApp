// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using System.Text.Json;
using ConferenceApp.Models;
using ConferenceApp.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Tests.Admin;

/// <summary>
/// The answer from a handler in the panel. The two kinds do not look alike over
/// HTTP: <c>Err()</c> and most successes return JSON with <c>success</c>, while
/// some handlers return a bare <c>Ok()</c> — a 200 with an empty body. Both are
/// 200, so a test cannot judge by the status code.
/// </summary>
public sealed record AdminReply(HttpStatusCode Status, bool Success, string Message, string Raw)
{
    public static async Task<AdminReply> ReadAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();

        if (string.IsNullOrWhiteSpace(raw))
            return new AdminReply(response.StatusCode, response.IsSuccessStatusCode, string.Empty, raw);

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
            var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()!
                : string.Empty;

            // A handler returning JSON without "success" — Health, for instance —
            // counts as a success only if the HTTP status is one.
            if (!root.TryGetProperty("success", out _))
                success = response.IsSuccessStatusCode;

            return new AdminReply(response.StatusCode, success, Html.Text(message), raw);
        }
        catch (JsonException)
        {
            // A page instead of JSON is what an unauthorised request gets, or a
            // handler that redirects. It is not a successful action.
            return new AdminReply(response.StatusCode, false, string.Empty, raw);
        }
    }
}

/// <summary>
/// The common ground of part 6. Every test creates whatever it uses and clears it
/// away itself: participants, crypto orders, lecturers, sessions, themes, styles
/// and bug reports.
/// </summary>
[Collection(AppCollection.Name)]
public abstract class AdminTestBase : IAsyncLifetime
{
    protected readonly AppFixture App;
    private readonly string _tag;

    private readonly List<string> _users     = new();
    private readonly List<int>    _lecturers = new();
    private readonly List<int>    _sessions  = new();
    private readonly List<int>    _bugs      = new();
    private readonly List<string> _themes    = new();
    private readonly List<string> _styleKeys = new();
    private readonly List<string> _files     = new();

    protected AdminTestBase(AppFixture app, string tag)
    {
        App  = app;
        _tag = tag;
    }

    public virtual Task InitializeAsync()
    {
        App.Smtp.Clear();
        return Task.CompletedTask;
    }

    public virtual async Task DisposeAsync()
    {
        foreach (var email in _users)
        {
            var user = await App.Db.FindUserAsync(email);
            if (user != null)
            {
                DeletePrivateUpload(user.PaperFilePath);
                DeletePrivateUpload(user.VerificationDocumentPath);

                // Orders outlive the participant by design ([D-06]), so the test
                // clears them itself; otherwise they stay for ever.
                await App.Db.WriteAsync(async db =>
                {
                    var orders = await db.CryptoOrders.Where(o => o.UserId == user.Id).ToListAsync();
                    db.CryptoOrders.RemoveRange(orders);
                });
            }

            await App.Db.DeleteParticipantAsync(email);
            await App.Db.DeleteOtpCodesAsync(email);
        }

        // The orders these tests left without an owner, found by the ExternalId,
        // which carries the test's tag.
        await App.Db.WriteAsync(async db =>
        {
            var orphans = await db.CryptoOrders
                .Where(o => o.ExternalId.StartsWith(_tag)).ToListAsync();
            db.CryptoOrders.RemoveRange(orphans);
        });

        await App.Db.WriteAsync(async db =>
        {
            foreach (var id in _lecturers)
            {
                var row = await db.Lecturers.FindAsync(id);
                if (row != null)
                {
                    DeletePublicUpload(row.AvatarImagePath);
                    db.Lecturers.Remove(row);
                }
            }

            foreach (var id in _sessions)
            {
                var row = await db.Schedule.FindAsync(id);
                if (row != null) db.Schedule.Remove(row);
            }

            foreach (var id in _bugs)
            {
                var row = await db.BugReports.FindAsync(id);
                if (row != null) db.BugReports.Remove(row);
            }

            foreach (var key in _themes)
            {
                var row = await db.SiteThemes.FirstOrDefaultAsync(t => t.ThemeKey == key);
                if (row != null && !row.IsBuiltIn) db.SiteThemes.Remove(row);
            }

            foreach (var key in _styleKeys)
            {
                var row = await db.PageStyleSettings.FirstOrDefaultAsync(p => p.PageKey == key);
                if (row != null) db.PageStyleSettings.Remove(row);

                var revisions = await db.PageStyleRevisions.Where(r => r.PageKey == key).ToListAsync();
                db.PageStyleRevisions.RemoveRange(revisions);
            }
        });

        foreach (var relative in _files) DeletePublicUpload(relative);
    }

    // ════════════════════════════════════════════════════════════════════
    // Participants
    // ════════════════════════════════════════════════════════════════════

    protected async Task<ApplicationUser> NewParticipantAsync(
        string partForm = "1", string paymentStatus = "Pending")
    {
        var email = $"{_tag}-{Guid.NewGuid():N}@example.test";
        _users.Add(email);
        return await App.Db.CreateParticipantAsync(email, partForm, paymentStatus);
    }

    /// <summary>A participant the base will no longer clear away, because the test deletes them itself.</summary>
    protected void Forget(string email) => _users.Remove(email);

    protected async Task<HttpSession> SignedInAsync(ApplicationUser user)
    {
        var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);
        return session;
    }

    protected async Task<HttpSession> SignedInAdminAsync()
    {
        var session = App.NewSession();
        await session.LoginAdminAsync(App.Credentials.AdminEmail, App.Credentials.AdminPassword);
        return session;
    }

    // ════════════════════════════════════════════════════════════════════
    // Requests to the panel
    // ════════════════════════════════════════════════════════════════════

    /// <summary>A POST to a handler in the panel, with a token taken from the page itself.</summary>
    protected static async Task<AdminReply> PostAsync(
        HttpSession session, string handler, Dictionary<string, string>? fields = null)
    {
        using var response = await session.PostHandlerAsync("/Admin", handler, fields);
        return await AdminReply.ReadAsync(response);
    }

    protected static async Task<AdminReply> PostMultipartAsync(
        HttpSession session, string handler,
        Dictionary<string, string> fields, IEnumerable<UploadFile> files)
    {
        using var response = await session.PostMultipartAsync(
            $"/Admin?handler={handler}", fields, files, tokenFrom: "/Admin");
        return await AdminReply.ReadAsync(response);
    }

    /// <summary>The panel, ready to be searched for visible text.</summary>
    protected static async Task<string> PanelAsync(HttpSession session)
    {
        using var response = await session.Client.GetAsync("/Admin");
        response.EnsureSuccessStatusCode();
        return await response.ReadPageAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // Registering things to be cleared away
    // ════════════════════════════════════════════════════════════════════

    protected void TrackLecturer(int id)   { if (id > 0) _lecturers.Add(id); }
    protected void TrackSession(int id)    { if (id > 0) _sessions.Add(id); }
    protected void TrackBugReport(int id)  { if (id > 0) _bugs.Add(id); }
    protected void TrackTheme(string key)  { if (key.Length > 0) _themes.Add(key); }
    protected void TrackStyle(string key)  { if (key.Length > 0) _styleKeys.Add(key); }
    protected void TrackPublicFile(string? relative) { if (!string.IsNullOrEmpty(relative)) _files.Add(relative); }

    /// <summary>The test's tag; it goes into the names so that it can be recognised after a failure.</summary>
    protected string Tag => _tag;

    protected string Unique(string prefix) => $"{_tag}-{prefix}-{Guid.NewGuid():N}"[..40];

    // ════════════════════════════════════════════════════════════════════
    // Files
    // ════════════════════════════════════════════════════════════════════

    protected static string PrivatePhysicalPath(string relative) =>
        Path.Combine(AppFixture.PrivateUploadsRoot,
            relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    protected static string PublicPhysicalPath(string relative) =>
        Path.Combine(TestPaths.RepoRoot, "wwwroot",
            relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    protected static void DeletePrivateUpload(string? relative)
    {
        if (string.IsNullOrEmpty(relative)) return;

        var full = Path.GetFullPath(PrivatePhysicalPath(relative));
        var root = Path.GetFullPath(AppFixture.PrivateUploadsRoot) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(full)) File.Delete(full);
    }

    /// <summary>
    /// Images uploaded from the panel — lecturers, partners — stay under
    /// <c>wwwroot</c>, because they are public. The cleanup refuses any path
    /// outside <c>wwwroot/uploads</c>.
    /// </summary>
    protected static void DeletePublicUpload(string? relative)
    {
        if (string.IsNullOrEmpty(relative)) return;

        var full = Path.GetFullPath(PublicPhysicalPath(relative));
        var root = Path.GetFullPath(Path.Combine(TestPaths.RepoRoot, "wwwroot", "uploads"))
                   + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(full)) File.Delete(full);
    }
}
