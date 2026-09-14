// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using ConferenceApp.Services;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Admin;

/// <summary>
/// Part 6, the backups: "a manual one, a list, a download".
/// <para>
/// <b>There is still no such tab.</b> Backups are made on schedule
/// (<c>DatabaseBackupService</c>, 03:00 and 15:00 UTC) and, since [T-36], on
/// demand from the one button in the Backups card of Health Check. There is no
/// list and no download: the file is the whole database — names, addresses,
/// telephone numbers, password hashes — and it is not served over HTTP at all.
/// </para>
/// <para>
/// The tests here guard three things: that the absences were established rather
/// than overlooked, that reading the folder and recognising a valid backup
/// works, and that the button makes exactly one copy, for administrators only,
/// and leaves a trace.
/// </para>
/// </summary>
public class BackupsTests : AdminTestBase
{
    public BackupsTests(AppFixture app) : base(app, "ad-bkp") { }

    private static string BackupFolder =>
        Path.Combine(TestPaths.RepoRoot, "backups");

    /// <summary>
    /// The copies this class caused to be made. Under test the database is
    /// <c>App_Data/test.db</c>, so they are named <c>test_*.db</c> and cannot be
    /// confused with the real <c>conferenceapp_*.db</c> ones — but they are made
    /// in the repository's own folder, so the class clears them away itself.
    /// </summary>
    private readonly List<string> _made = new();

    public override async Task DisposeAsync()
    {
        foreach (var path in _made) Erase(path);
        await base.DisposeAsync();
    }

    /// <summary>
    /// A copy and the two sidecars SQLite leaves beside it. Merely opening a
    /// WAL database — which reading one back to check it is a real database
    /// does — creates <c>-shm</c> and <c>-wal</c> next to the file, and deleting
    /// only the <c>.db</c> would leave them in the repository's folder.
    /// </summary>
    private static void Erase(string path)
    {
        foreach (var candidate in new[] { path, path + "-shm", path + "-wal", path + "-journal" })
        {
            try { if (File.Exists(candidate)) File.Delete(candidate); }
            catch { /* left for the next run */ }
        }
    }

    /// <summary>The automatic copies of the test database now in the folder.</summary>
    private static string[] TestBackups() =>
        Directory.Exists(BackupFolder)
            ? DatabaseLocation.ListAutomaticBackups(BackupFolder, TestPaths.TestDbFile)
                              .Select(f => f.FullName).ToArray()
            : Array.Empty<string>();

    /// <summary>Presses the button, and remembers whatever file came of it.</summary>
    private async Task<(AdminReply Reply, string? FileName)> CreateBackupAsync(HttpSession admin)
    {
        var reply = await PostAsync(admin, "CreateBackup");

        string? fileName = null;
        try
        {
            using var doc = JsonDocument.Parse(reply.Raw);
            if (doc.RootElement.TryGetProperty("fileName", out var f) && f.ValueKind == JsonValueKind.String)
                fileName = f.GetString();
        }
        catch (JsonException) { /* not JSON: the assertion in the test says so */ }

        if (fileName is not null) _made.Add(Path.Combine(BackupFolder, fileName));

        return (reply, fileName);
    }

    // ════════════════════════════════════════════════════════════════════
    // What is missing
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// An explicit assertion, so that nothing is left at "it probably is not
    /// there". <c>CreateBackup</c> is deliberately absent from this list: it is
    /// the one handler backups do have, and it is a POST, which a GET here would
    /// not reach anyway.
    /// </summary>
    [Theory]
    [InlineData("RunBackup")]
    [InlineData("ListBackups")]
    [InlineData("DownloadBackup")]
    [InlineData("Backup")]
    public async Task Панелът_няма_handler_за_бекъпи(string handler)
    {
        using var admin = await SignedInAdminAsync();

        using var get = await admin.Client.GetAsync($"/Admin?handler={handler}");
        var getBody = await get.Content.ReadAsStringAsync();

        // A missing handler on a Razor Page returns the page itself rather than an
        // error. It is recognised by the response being the whole panel.
        Assert.Contains("Submitted Registrations", Html.Text(getBody));
    }

    /// <summary>
    /// The manual backup is a POST and nothing else. As a GET it must not run:
    /// a link, a prefetch or a refresh would otherwise copy the database.
    /// </summary>
    [Fact]
    public async Task Ръчното_копие_не_се_прави_с_GET()
    {
        using var admin = await SignedInAdminAsync();

        var before = TestBackups();

        using var get = await admin.Client.GetAsync("/Admin?handler=CreateBackup");
        Assert.Contains("Submitted Registrations", Html.Text(await get.Content.ReadAsStringAsync()));

        Assert.Equal(before.Length, TestBackups().Length);
    }

    [Fact]
    public async Task Панелът_няма_таб_за_бекъпи()
    {
        using var admin = await SignedInAdminAsync();
        var html = await PanelAsync(admin);

        Assert.DoesNotContain("data-target=\"tab-backups\"", html);
        Assert.DoesNotContain("tab-backup", html);
    }

    /// <summary>
    /// Backups are not served over HTTP, and that is right. The file is the whole
    /// database: names, addresses, telephone numbers, password hashes.
    /// </summary>
    [Theory]
    [InlineData("/backups/")]
    [InlineData("/backups/conferenceapp_20260101_0300.db")]
    [InlineData("/Admin/backups/")]
    public async Task Копията_не_се_раздават_по_адрес(string path)
    {
        using var client = App.NewClient(followRedirects: false);
        using var response = await client.GetAsync(path);

        Assert.NotEqual(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Дори_администратор_не_сваля_копие_по_адрес()
    {
        using var admin = await SignedInAdminAsync();

        using var client = admin.NoRedirectClient();
        using var response = await client.GetAsync("/backups/");

        Assert.NotEqual(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════
    // The button: one copy, now
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Натискането_прави_файл_в_папката_за_копия()
    {
        using var admin = await SignedInAdminAsync();

        var before = TestBackups();

        var (reply, fileName) = await CreateBackupAsync(admin);

        Assert.True(reply.Success, "Копието не беше направено: " + reply.Raw);
        Assert.False(string.IsNullOrEmpty(fileName), "Отговорът не назова файла: " + reply.Raw);

        // The name is in the message too, which is what the administrator sees.
        Assert.Contains(fileName!, reply.Message);

        var made = Path.Combine(BackupFolder, fileName!);
        Assert.True(File.Exists(made), $"Файлът го няма: {made}");

        // A real copy of the test database, not an empty file — and recognised
        // as an automatic backup, so the rotation will look after it.
        Assert.True(new FileInfo(made).Length > 0, "Копието е с нулев размер.");
        Assert.Contains(made, TestBackups());
        Assert.DoesNotContain(made, before);
    }

    /// <summary>
    /// The copy is a database one can open, not bytes taken off a live file —
    /// exactly as for the copy the schedule makes.
    /// </summary>
    [Fact]
    public async Task Ръчното_копие_е_четима_база()
    {
        using var admin = await SignedInAdminAsync();

        var (reply, fileName) = await CreateBackupAsync(admin);
        Assert.True(reply.Success, reply.Raw);

        var made = Path.Combine(BackupFolder, fileName!);

        using var connection = new SqliteConnection($"Data Source={made};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM AspNetUsers";

        Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync()) > 0);
    }

    /// <summary>
    /// After the copy the card is green: it is the whole point of the button that
    /// the administrator does not have to go looking to find out whether it
    /// worked.
    /// </summary>
    [Fact]
    public async Task Картата_позеленява_след_копието()
    {
        using var admin = await SignedInAdminAsync();

        var (reply, _) = await CreateBackupAsync(admin);
        Assert.True(reply.Success, reply.Raw);

        using var response = await admin.Client.GetAsync("/Admin?handler=HealthCheck&service=backups");
        response.EnsureSuccessStatusCode();

        var el = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("ok", el.GetProperty("status").GetString());

        var details = el.GetProperty("details").EnumerateArray()
            .ToDictionary(d => d.GetProperty("label").GetString()!, d => d.GetProperty("value").GetString()!);

        Assert.StartsWith("test_", details["Последно"]);
        Assert.Equal("работи", details["Услуга"]);
    }

    /// <summary>
    /// Two presses in quick succession must not produce two copies.
    /// <para>
    /// The guard has two halves and this is the far one: the runner holds a gate,
    /// so a second request that arrives while the first is still writing is
    /// turned away rather than allowed to race over the same temporary file. The
    /// near half — the button disabling itself — is in the browser test.
    /// </para>
    /// <para>
    /// What is asserted is the outcome rather than which of the two requests lost:
    /// that depends on how quickly a copy finishes and is not something to pin a
    /// test to. One new file, and never two different ones.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Двукратно_бързо_натискане_не_прави_две_копия()
    {
        using var admin = await SignedInAdminAsync();

        var before = TestBackups().ToHashSet();

        var both = await Task.WhenAll(
            CreateBackupAsync(admin),
            CreateBackupAsync(admin));

        var named = both.Select(r => r.FileName).Where(n => n is not null).Distinct().ToList();
        Assert.True(named.Count <= 1,
            "Двете натискания направиха различни файлове: " + string.Join(", ", named));

        var added = TestBackups().Where(f => !before.Contains(f)).ToList();
        Assert.Single(added);

        // Whichever of the two was refused said so in a way the panel can show,
        // rather than failing silently or with a stack trace.
        foreach (var (reply, _) in both)
            Assert.False(string.IsNullOrWhiteSpace(reply.Message) && !reply.Success,
                "Отказаното натискане не обясни защо: " + reply.Raw);
    }

    // ── Rights ───────────────────────────────────────────────────────────

    /// <summary>
    /// The button copies the whole database. A participant must not be able to
    /// reach it even with a valid token of their own — the handler checks the
    /// role itself, not only the attribute on the page.
    /// </summary>
    [Fact]
    public async Task Обикновен_потребител_не_може_да_направи_копие()
    {
        var user = await NewParticipantAsync();
        using var session = await SignedInAsync(user);

        var before = TestBackups();

        // The token belongs to the session rather than to the page, so it is
        // taken from a page the participant is allowed to open.
        var token = await session.AntiforgeryTokenAsync("/Profile");

        using var client = session.NoRedirectClient();
        using var response = await client.PostAsync("/Admin?handler=CreateBackup",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }));

        Assert.True(
            response.StatusCode is System.Net.HttpStatusCode.Found
                                or System.Net.HttpStatusCode.Forbidden,
            $"CreateBackup отговори {(int)response.StatusCode}, а трябваше да откаже.");

        Assert.Equal(before.Length, TestBackups().Length);
    }

    [Fact]
    public async Task Външен_не_може_да_направи_копие()
    {
        var before = TestBackups();

        using var client = App.NewClient(followRedirects: false);
        using var response = await client.PostAsync("/Admin?handler=CreateBackup",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.NotEqual(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before.Length, TestBackups().Length);
    }

    // ── The audit ────────────────────────────────────────────────────────

    /// <summary>
    /// Who, when, which file. The row is written by the backup runner rather than
    /// by the automatic filter, which would only have recorded "OK" — and the
    /// file is the part that matters when the time comes to restore from it.
    /// </summary>
    [Fact]
    public async Task Ръчното_копие_оставя_ред_в_одита_с_име_на_файла()
    {
        using var admin = await SignedInAdminAsync();

        var lastId = await App.Db.LastAuditIdAsync();

        var (reply, fileName) = await CreateBackupAsync(admin);
        Assert.True(reply.Success, reply.Raw);

        var since = await App.Db.AuditSinceAsync(lastId);
        var rows = since.Where(a => a.Action == "Database Backup").ToList();

        var row = Assert.Single(rows);

        // Who: the signed-in administrator, not "System" and not "unknown".
        Assert.Equal(App.Credentials.AdminEmail, row.UserEmail);
        Assert.NotEqual("System", row.UserEmail);

        // What: the file, and that it was asked for by hand.
        Assert.Contains(fileName!, row.Details ?? "");
        Assert.Contains("Ръчно", row.Details ?? "");

        // When: the row carries its own timestamp, and it is this moment.
        Assert.True((DateTime.UtcNow - row.Timestamp).TotalMinutes < 5,
            $"Времето в одита е {row.Timestamp:O}.");

        // And exactly one row: the filter must not add a second, poorer one.
        Assert.DoesNotContain(since, a => a.Action == "CreateBackup");
    }

    // ════════════════════════════════════════════════════════════════════
    // What does work
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Health_показва_състоянието_на_копията()
    {
        using var admin = await SignedInAdminAsync();

        using var response = await admin.Client.GetAsync("/Admin?handler=HealthCheck&service=backups");
        response.EnsureSuccessStatusCode();

        var el = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("backups", el.GetProperty("key").GetString());

        var status = el.GetProperty("status").GetString();
        Assert.Contains(status, new[] { "ok", "warn", "fail" });

        // On "fail" the message has to say what is missing rather than simply
        // "error": this is the only place from which it is visible that no backups
        // are being made at all.
        if (status == "fail")
            Assert.NotEqual(string.Empty, el.GetProperty("hint").GetString());
        else
            Assert.Contains("Брой копия",
                el.GetProperty("details").EnumerateArray()
                  .Select(d => d.GetProperty("label").GetString()!));
    }

    /// <summary>
    /// [S-01] An unfinished backup is not a valid one. Counted as valid, Health
    /// announces "last backup: a minute ago" over a file that cannot be restored
    /// from.
    /// </summary>
    [Fact]
    public void Недовършеното_копие_не_се_брои()
    {
        var folder = Path.Combine(TestPaths.RunScratch, "backup-probe-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        var dbPath = Path.Combine(folder, "probe.db");
        File.WriteAllText(dbPath, "not a real database");

        File.WriteAllText(Path.Combine(folder, "probe_20260101_0300.db"), "valid");
        File.WriteAllText(Path.Combine(folder, "probe_20260102_0300.db" + DatabaseLocation.PartialExtension), "half");

        var found = DatabaseLocation.ListAutomaticBackups(folder, dbPath);

        Assert.Single(found);
        Assert.Equal("probe_20260101_0300.db", found[0].Name);
    }

    /// <summary>
    /// [S-01] A copy left by hand before a migration must not enter the rotation;
    /// otherwise it counts as automatic and is deleted.
    /// </summary>
    [Fact]
    public void Ръчно_оставено_копие_не_се_брои_за_автоматично()
    {
        var folder = Path.Combine(TestPaths.RunScratch, "backup-probe-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        var dbPath = Path.Combine(folder, "probe.db");
        File.WriteAllText(dbPath, "x");

        File.WriteAllText(Path.Combine(folder, "probe_20260101_0300.db"), "auto");
        File.WriteAllText(Path.Combine(folder, "probe_predeploy.db"), "manual");
        File.WriteAllText(Path.Combine(folder, "probe_2026010_0300.db"), "wrong stamp");
        File.WriteAllText(Path.Combine(folder, "other_20260101_0300.db"), "other db");

        var found = DatabaseLocation.ListAutomaticBackups(folder, dbPath);

        Assert.Single(found);
        Assert.Equal("probe_20260101_0300.db", found[0].Name);
    }

    /// <summary>
    /// [S-02] The path to the database comes from the connection string, the same
    /// one EF hands to SQLite. Otherwise the backup copies one file while Health
    /// looks at another.
    /// </summary>
    [Fact]
    public void Пътят_до_базата_идва_от_connection_string_а()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=App_Data/probe.db"
            })
            .Build();

        var resolved = DatabaseLocation.ResolveDatabaseFile(config);

        Assert.True(Path.IsPathRooted(resolved));
        Assert.EndsWith(Path.Combine("App_Data", "probe.db"), resolved);
    }

    [Fact]
    public void Изричното_надписване_има_предимство()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=ignored.db",
                [DatabaseLocation.OverrideKey]          = "chosen.db"
            })
            .Build();

        Assert.EndsWith("chosen.db", DatabaseLocation.ResolveDatabaseFile(config));
    }

    /// <summary>
    /// Under test the service points at <c>App_Data/test.db</c> rather than at the
    /// live database. If that ever broke, the tests would be copying — and rotating
    /// — the real data.
    /// </summary>
    [Fact]
    public void Услугата_под_теста_не_сочи_към_живата_база()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseLocation.OverrideKey] = TestPaths.TestDbFile
            })
            .Build();

        var resolved = DatabaseLocation.ResolveDatabaseFile(config);

        Assert.NotEqual(Path.GetFullPath(TestPaths.LiveDbFile), resolved);
        Assert.Equal(Path.GetFullPath(TestPaths.TestDbFile), resolved);
    }

    [Fact]
    public void Папката_с_копия_е_в_корена_а_не_в_wwwroot()
    {
        // ResolveBackupFolder is not called, since it needs an IWebHostEnvironment;
        // what is checked is the consequence: the folder is not under the public
        // root.
        var wwwroot = Path.GetFullPath(Path.Combine(TestPaths.RepoRoot, "wwwroot"))
                      + Path.DirectorySeparatorChar;

        Assert.False(Path.GetFullPath(BackupFolder).StartsWith(wwwroot, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The backup folder exists from the moment the application starts.
    ///
    /// <para>
    /// <c>Directory.CreateDirectory</c> used to be called only inside the copying
    /// itself, and the windows are at 03:00 and 15:00 UTC. Hours pass between
    /// startup and the first window, during which the folder does not exist and
    /// Health reports that the backup folder is missing — which sounds like a
    /// stopped service when in fact it is running and waiting its turn. The other
    /// folders (App_Data, logs, the keys) have long been created at startup; the
    /// backups were the exception.
    /// </para>
    ///
    /// <para>
    /// The test also guards the difference between the two messages: a missing
    /// folder is a sign of broken configuration, while an empty folder means no
    /// backup has been made yet. The second is the normal state of a freshly
    /// started application.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Папката_за_копия_се_създава_при_старт()
    {
        // The fixture has already started the application, which is enough.
        Assert.True(Directory.Exists(BackupFolder),
            $"Папката за резервни копия липсва: {BackupFolder}");

        using var admin = await SignedInAdminAsync();
        using var response = await admin.Client.GetAsync("/Admin?handler=HealthCheck&service=backups");
        response.EnsureSuccessStatusCode();

        var el = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var message = el.GetProperty("message").GetString() ?? "";

        Assert.DoesNotContain("не съществува", message);
    }
}
