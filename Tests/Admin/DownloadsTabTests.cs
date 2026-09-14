// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using ConferenceApp.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Tests.Admin;

/// <summary>
/// Part 6, the Downloads tab through the administrator's eyes.
/// <para>
/// Uploading, removing and serving the files themselves is covered in part 5
/// (<c>Files/PublicDownloadsTests.cs</c>). What is checked here is the panel's own
/// job: that the five keys are always there, in the same order, and that the state
/// of each is shown truthfully — an uploaded file with its name and size, a
/// missing one marked as not uploaded.
/// </para>
/// </summary>
public class DownloadsTabTests : AdminTestBase
{
    public DownloadsTabTests(AppFixture app) : base(app, "ad-dl") { }

    private readonly List<string> _touched = new();

    public override async Task DisposeAsync()
    {
        foreach (var key in _touched)
        {
            var row = await RowAsync(key);
            if (row?.FilePath != null)
            {
                var physical = PublicPhysicalPath(row.FilePath);
                if (File.Exists(physical)) File.Delete(physical);
            }

            await App.Db.WriteAsync(async db =>
            {
                var saved = await db.DownloadableFiles.FirstOrDefaultAsync(f => f.FileKey == key);
                if (saved != null) db.DownloadableFiles.Remove(saved);
            });
        }

        await base.DisposeAsync();
    }

    private Task<DownloadableFile?> RowAsync(string key) =>
        App.Db.ReadAsync(db => db.DownloadableFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.FileKey == key));

    private async Task<AdminReply> UploadAsync(HttpSession admin, string key, UploadFile file)
    {
        if (!_touched.Contains(key)) _touched.Add(key);

        return await PostMultipartAsync(admin, "UploadDownload",
            new Dictionary<string, string> { ["fileKey"] = key }, new[] { file });
    }

    private static string DownloadsTab(string html)
    {
        var start = html.IndexOf("id=\"tab-downloads\"", StringComparison.Ordinal);
        Assert.True(start > 0, "Табът „Downloads“ липсва в панела.");

        var end = html.IndexOf("id=\"tab-", start + 10, StringComparison.Ordinal);
        return end > start ? html[start..end] : html[start..];
    }

    [Fact]
    public async Task Петте_ключа_стоят_винаги_и_в_един_и_същи_ред()
    {
        using var admin = await SignedInAdminAsync();
        var tab = DownloadsTab(await PanelAsync(admin));

        var positions = DownloadableFile.Keys
            .Select(k => tab.IndexOf($"data-dl-key=\"{k}\"", StringComparison.Ordinal))
            .ToList();

        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.OrderBy(p => p), positions);
    }

    [Fact]
    public async Task Некачен_файл_се_показва_като_некачен()
    {
        const string key = DownloadableFile.DocCopyright;

        // The test must not depend on whether anyone uploaded anything before it.
        if (await RowAsync(key) is { FilePath: not null })
            return;

        using var admin = await SignedInAdminAsync();
        var tab = DownloadsTab(await PanelAsync(admin));

        var row = tab[tab.IndexOf($"data-dl-key=\"{key}\"", StringComparison.Ordinal)..];
        row = row[..row.IndexOf("</div>\n", StringComparison.Ordinal)];

        Assert.Contains("не е качен", row);
    }

    [Fact]
    public async Task Каченият_файл_се_показва_с_име_тип_и_размер()
    {
        const string key = DownloadableFile.DocDeclaration;

        using var admin = await SignedInAdminAsync();

        var file = UploadFile.Pdf("file", "deklaracia.pdf").OfSize(300 * 1024);
        Assert.True((await UploadAsync(admin, key, file)).Success);

        var saved = await RowAsync(key);
        Assert.NotNull(saved);
        Assert.Equal("deklaracia.pdf", saved!.DownloadName);
        Assert.Equal("PDF", saved.TypeLabel);
        Assert.Equal("300 KB", saved.SizeLabel);

        var tab = DownloadsTab(await PanelAsync(admin));
        Assert.Contains("deklaracia.pdf", tab);
        Assert.Contains("300 KB", tab);
    }

    [Fact]
    public async Task Премахването_връща_реда_в_състояние_некачен()
    {
        const string key = DownloadableFile.DocTemplate;

        using var admin = await SignedInAdminAsync();
        Assert.True((await UploadAsync(admin, key, UploadFile.Pdf("file", "shablon.pdf"))).Success);

        var reply = await PostAsync(admin, "RemoveDownload",
            new Dictionary<string, string> { ["fileKey"] = key });
        Assert.True(reply.Success, reply.Message);

        // The row stays: the key has to be able to take a new file.
        var saved = await RowAsync(key);
        Assert.NotNull(saved);
        Assert.Null(saved!.FilePath);
        Assert.Null(saved.DownloadName);
        Assert.False(saved.IsUploaded);

        var tab = DownloadsTab(await PanelAsync(admin));
        Assert.DoesNotContain("shablon.pdf", tab);
    }

    [Fact]
    public async Task Непознат_ключ_не_създава_шести_ред()
    {
        var before = await App.Db.ReadAsync(db => db.DownloadableFiles.CountAsync());

        using var admin = await SignedInAdminAsync();
        var reply = await PostMultipartAsync(admin, "UploadDownload",
            new Dictionary<string, string> { ["fileKey"] = "programme.de" },
            new[] { UploadFile.Pdf("file", "programm.pdf") });

        Assert.False(reply.Success);
        Assert.Equal(before, await App.Db.ReadAsync(db => db.DownloadableFiles.CountAsync()));
    }

    [Fact]
    public async Task Качването_и_премахването_оставят_следа_в_одита()
    {
        const string key = DownloadableFile.ProgrammeEn;

        using var admin = await SignedInAdminAsync();
        var lastId = await App.Db.LastAuditIdAsync();

        Assert.True((await UploadAsync(admin, key, UploadFile.Pdf("file", "programme.pdf"))).Success);
        Assert.True((await PostAsync(admin, "RemoveDownload",
            new Dictionary<string, string> { ["fileKey"] = key })).Success);

        var since = await App.Db.AuditSinceAsync(lastId);

        Assert.Contains(since, a => a.Action == "Download File Uploaded" && a.Details.Contains(key));
        Assert.Contains(since, a => a.Action == "Download File Removed" && a.Details.Contains(key));
    }

    [Fact]
    public async Task Кой_е_качил_се_запомня()
    {
        const string key = DownloadableFile.ProgrammeBg;

        using var admin = await SignedInAdminAsync();
        Assert.True((await UploadAsync(admin, key, UploadFile.Pdf("file", "programa.pdf"))).Success);

        var saved = await RowAsync(key);
        Assert.Equal(App.Credentials.AdminEmail, saved!.UpdatedBy);
        Assert.NotNull(saved.UpdatedAt);
    }
}
