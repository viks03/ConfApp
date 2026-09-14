// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using ConferenceApp.Models;
using ConferenceApp.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Tests.Files;

/// <summary>
/// Part 5, the five files in the Downloads tab: the programme in both languages
/// and the three documents for authors.
/// <para>
/// These deliberately stay under <c>wwwroot</c> and are served anonymously: they
/// are public documents, not personal data. The test guards both sides — that
/// they download without signing in, and that they are only shown when they
/// really have been uploaded.
/// </para>
/// </summary>
[Collection(AppCollection.Name)]
public class PublicDownloadsTests : FileTestBase
{
    private readonly List<string> _touchedKeys = new();

    public PublicDownloadsTests(AppFixture app) : base(app, "fl-dl") { }

    public override async Task DisposeAsync()
    {
        // These rows live outside the participants, so they are cleared separately;
        // otherwise an uploaded file would go on showing on a public page during
        // the next test.
        foreach (var key in _touchedKeys)
        {
            var row = await RowAsync(key);
            if (row?.FilePath != null)
            {
                var physical = PublicPhysicalPath(row.FilePath);
                if (File.Exists(physical)) File.Delete(physical);
            }

            await App.Db.WriteAsync(async db =>
            {
                var saved = await db.Set<DownloadableFile>()
                    .FirstOrDefaultAsync(f => f.FileKey == key);
                if (saved != null) db.Set<DownloadableFile>().Remove(saved);
            });
        }

        await base.DisposeAsync();
    }

    private Task<DownloadableFile?> RowAsync(string key) =>
        App.Db.ReadAsync(db => db.Set<DownloadableFile>().AsNoTracking()
            .FirstOrDefaultAsync(f => f.FileKey == key));

    /// <summary>Uploads through the same handler the admin panel calls from the browser.</summary>
    private async Task<HttpResponseMessage> UploadAsync(
        HttpSession admin, string fileKey, UploadFile file)
    {
        if (!_touchedKeys.Contains(fileKey)) _touchedKeys.Add(fileKey);

        return await admin.PostMultipartAsync("/Admin?handler=UploadDownload",
            new Dictionary<string, string> { ["fileKey"] = fileKey },
            new[] { file },
            tokenFrom: "/Admin");
    }

    private async Task<string> PageAsync(string path)
    {
        using var client = App.NewClient();
        using var response = await client.GetAsync(path);
        return await response.ReadPageAsync();
    }

    private async Task<HttpStatusCode> StatusOfAsync(string path)
    {
        using var client = App.NewClient(followRedirects: false);
        using var response = await client.GetAsync(path);
        return response.StatusCode;
    }

    private static UploadFile Doc(string marker, string name = "programa.pdf") =>
        UploadFile.Pdf("file", name) with
        {
            Content = System.Text.Encoding.ASCII.GetBytes($"%PDF-1.4\n% {marker}\n%%EOF\n")
        };

    // ════════════════════════════════════════════════════════════════════
    // From the admin panel to the page
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Качената_програма_се_показва_и_сваля_от_Програма()
    {
        using var admin = await SignedInAdminAsync();

        var response = await UploadAsync(admin, DownloadableFile.ProgrammeBg,
            Doc("programa-bg", "Programa-2026.pdf"));

        Assert.Contains("\"success\":true", await response.Content.ReadAsStringAsync());

        var row = await RowAsync(DownloadableFile.ProgrammeBg);
        Assert.NotNull(row!.FilePath);
        Assert.Equal("Programa-2026.pdf", row.DownloadName);
        Assert.StartsWith("/uploads/documents/", row.FilePath);

        // ── The page shows the button with the same address ────────────
        var page = await PageAsync("/Schedule");

        Assert.Contains($"href=\"{row.FilePath}\"", page);
        Assert.Contains("download=\"Programa-2026.pdf\"", page);

        // ── And it really downloads the file, without signing in ───────
        using var anonymous = App.NewClient();
        using var file = await anonymous.GetAsync(row.FilePath);

        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(Doc("programa-bg").Content, await file.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData(DownloadableFile.DocTemplate)]
    [InlineData(DownloadableFile.DocDeclaration)]
    [InlineData(DownloadableFile.DocCopyright)]
    public async Task Качените_документи_се_показват_и_свалят_от_За_конференцията(string key)
    {
        using var admin = await SignedInAdminAsync();

        await UploadAsync(admin, key, Doc(key, "shablon.docx") with
        {
            ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        });

        var row = await RowAsync(key);
        Assert.NotNull(row!.FilePath);

        var page = await PageAsync("/Conference");
        Assert.Contains($"href=\"{row.FilePath}\"", page);
        Assert.Contains("DOCX", page);

        using var anonymous = App.NewClient();
        using var file = await anonymous.GetAsync(row.FilePath);

        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(Doc(key).Content, await file.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Некачен_файл_не_показва_бутон_който_води_наникъде()
    {
        // Both pages open with an empty table: there is no button at all, rather
        // than a button leading to a 404.
        Assert.DoesNotContain("class=\"sc-dl\"",  await PageAsync("/Schedule"));
        Assert.DoesNotContain("class=\"cf-doc\"", await PageAsync("/Conference"));
    }

    [Fact]
    public async Task Премахнатият_файл_изчезва_от_страницата_и_от_диска()
    {
        using var admin = await SignedInAdminAsync();
        await UploadAsync(admin, DownloadableFile.ProgrammeEn, Doc("za-mahane"));

        var row = await RowAsync(DownloadableFile.ProgrammeEn);
        var physical = PublicPhysicalPath(row!.FilePath!);
        Assert.True(File.Exists(physical));

        var response = await admin.PostHandlerAsync("/Admin", "RemoveDownload",
            new Dictionary<string, string> { ["fileKey"] = DownloadableFile.ProgrammeEn });

        Assert.Contains("\"success\":true", await response.Content.ReadAsStringAsync());

        var after = await RowAsync(DownloadableFile.ProgrammeEn);
        Assert.Null(after!.FilePath);
        Assert.Null(after.DownloadName);
        Assert.Equal(0, after.SizeBytes);

        Assert.False(File.Exists(physical), "Премахнатият файл е останал на диска.");

        Assert.DoesNotContain(row.FilePath!, await PageAsync("/Schedule"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusOfAsync(row.FilePath!));
    }

    [Fact]
    public async Task Замяната_трие_стария_файл()
    {
        using var admin = await SignedInAdminAsync();

        await UploadAsync(admin, DownloadableFile.ProgrammeBg, Doc("purvi"));
        var first = (await RowAsync(DownloadableFile.ProgrammeBg))!.FilePath!;

        await UploadAsync(admin, DownloadableFile.ProgrammeBg, Doc("vtori"));
        var second = (await RowAsync(DownloadableFile.ProgrammeBg))!.FilePath!;

        Assert.NotEqual(first, second);
        Assert.False(File.Exists(PublicPhysicalPath(first)),
            "Старият файл е останал на диска — сираци се трупат при всяка замяна.");
        Assert.True(File.Exists(PublicPhysicalPath(second)));

        Assert.Equal(HttpStatusCode.NotFound, await StatusOfAsync(first));
    }

    // ════════════════════════════════════════════════════════════════════
    // What is not accepted
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("kartinka.png",  "image/png")]
    [InlineData("skript.exe",    "application/octet-stream")]
    [InlineData("stranica.html", "text/html")]
    public async Task Непозволено_разширение_не_се_приема_и_не_пипа_каченото(string name, string mime)
    {
        using var admin = await SignedInAdminAsync();

        await UploadAsync(admin, DownloadableFile.DocTemplate, Doc("dobur", "shablon.pdf"));
        var before = (await RowAsync(DownloadableFile.DocTemplate))!.FilePath!;

        var response = await UploadAsync(admin, DownloadableFile.DocTemplate,
            Doc("losh", name) with { ContentType = mime });

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);

        var after = await RowAsync(DownloadableFile.DocTemplate);
        Assert.Equal(before, after!.FilePath);
        Assert.True(File.Exists(PublicPhysicalPath(before)),
            "Отказаното качване е отнело вече качения документ.");
    }

    [Fact]
    public async Task Файл_над_двайсет_мегабайта_не_се_приема()
    {
        using var admin = await SignedInAdminAsync();

        var tooBig = Doc("golyam") with { Content = new byte[20 * 1024 * 1024 + 1024] };
        var response = await UploadAsync(admin, DownloadableFile.DocCopyright, tooBig);

        Assert.Contains("\"success\":false", await response.Content.ReadAsStringAsync());
        Assert.Null(await RowAsync(DownloadableFile.DocCopyright));
    }

    [Fact]
    public async Task Непознат_ключ_не_създава_шести_документ()
    {
        using var admin = await SignedInAdminAsync();

        var response = await UploadAsync(admin, "doc.shesti", Doc("shesti"));

        Assert.Contains("\"success\":false", await response.Content.ReadAsStringAsync());
        Assert.Null(await RowAsync("doc.shesti"));
    }

    [Fact]
    public async Task Качване_без_избран_файл_не_се_приема()
    {
        using var admin = await SignedInAdminAsync();

        var response = await admin.PostHandlerAsync("/Admin", "UploadDownload",
            new Dictionary<string, string> { ["fileKey"] = DownloadableFile.ProgrammeBg });

        Assert.Contains("\"success\":false", await response.Content.ReadAsStringAsync());
        Assert.Null(await RowAsync(DownloadableFile.ProgrammeBg));
    }

    // ════════════════════════════════════════════════════════════════════
    // Who is allowed to
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Участник_не_качва_и_не_маха_файлове_за_изтегляне()
    {
        using var admin = await SignedInAdminAsync();
        await UploadAsync(admin, DownloadableFile.ProgrammeBg, Doc("na-admina"));
        var before = (await RowAsync(DownloadableFile.ProgrammeBg))!.FilePath!;

        var user = await NewParticipantAsync();
        using var session = await SignedInAsync(user);

        // The token belongs to the session, so it is taken from a page the
        // participant can see; otherwise the test would fail on a missing form
        // rather than on the permissions.
        var upload = await session.PostMultipartAsync("/Admin?handler=UploadDownload",
            new Dictionary<string, string> { ["fileKey"] = DownloadableFile.ProgrammeBg },
            new[] { Doc("na-uchastnika") },
            tokenFrom: "/Profile");

        // The client follows the redirect to the access-denied page, so the status
        // may well be 200; the proof is that the handler did not return its JSON.
        Assert.DoesNotContain("\"success\":true", await upload.Content.ReadAsStringAsync());

        var remove = await session.PostHandlerAsync("/Admin", "RemoveDownload",
            new Dictionary<string, string> { ["fileKey"] = DownloadableFile.ProgrammeBg },
            tokenFrom: "/Profile");

        Assert.DoesNotContain("\"success\":true", await remove.Content.ReadAsStringAsync());

        var after = await RowAsync(DownloadableFile.ProgrammeBg);
        Assert.Equal(before, after!.FilePath);
        Assert.True(File.Exists(PublicPhysicalPath(before)));
    }

    [Fact]
    public async Task Външен_не_качва_файлове_за_изтегляне()
    {
        using var anonymous = App.NewClient(followRedirects: false);

        var content = new MultipartFormDataContent
        {
            { new StringContent(DownloadableFile.ProgrammeBg), "fileKey" }
        };
        var part = new ByteArrayContent(Doc("otvън").Content);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(part, "file", "programa.pdf");

        var response = await anonymous.PostAsync("/Admin?handler=UploadDownload", content);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(await RowAsync(DownloadableFile.ProgrammeBg));
    }
}
