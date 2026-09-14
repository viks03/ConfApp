// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Data;
using ConferenceApp.Services;
using ConferenceApp.Tests.Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConferenceApp.Tests.Services;

/// <summary>
/// One backup of the database, made by <see cref="DatabaseBackupRunner"/>
/// <b>itself</b>, with a database, a backup folder and a logger of its own.
/// <para>
/// The runner is the one piece of code that copies, whether the 03:00 / 15:00
/// schedule called it or the button in the Health tab did — so calling it
/// directly here tests both. What is asserted on is what it leaves behind: the
/// file, its contents, the audit row, the leftovers cleared away, and the
/// rotation. Nothing here checks that it "does not throw".
/// </para>
/// <para>
/// The schedule itself has no button and no setting for the hour, so waiting for
/// a real window would mean waiting up to twelve hours; the arithmetic that
/// picks the window is tested separately in <see cref="BackupServiceTests"/>.
/// </para>
/// </summary>
internal sealed class BackupWorkbench : IDisposable
{
    public string Root         { get; }
    public string DbFile       { get; }
    public string BackupFolder => Path.Combine(Root, "backups");

    public CapturingLogger<DatabaseBackupRunner> Log { get; } = new();
    public DatabaseBackupRunner Runner { get; }
    public FakeBackupStatus Status { get; } = new();
    public TestDb Db { get; }
    public IConfiguration Config { get; }
    public ScratchEnvironment Env { get; }

    private readonly ServiceProvider _services;

    private BackupWorkbench(string name, string dbFileName, int keepMax)
    {
        Root   = Path.Combine(TestPaths.RunScratch, "backup-" + name);
        DbFile = Path.Combine(Root, dbFileName);

        Directory.CreateDirectory(Root);

        Config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Set explicitly so that no relative path is resolved against the
                // test process's working directory.
                [DatabaseLocation.OverrideKey]         = DbFile,
                ["BackupSettings:KeepMaxBackups"]      = keepMax.ToString()
            })
            .Build();

        Env = new ScratchEnvironment(Root);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={DbFile}"));
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<AuditService>();
        _services = services.BuildServiceProvider();

        Db = new TestDb(DbFile);

        Runner = new DatabaseBackupRunner(
            Log, Env, _services.GetRequiredService<IServiceScopeFactory>(), Status, Config);
    }

    /// <summary>
    /// A workbench with a real database in it: the migrations are applied and there
    /// is at least one participant, so that the backup is not an empty file.
    /// </summary>
    public static async Task<BackupWorkbench> CreateAsync(
        string name, int keepMax = 14, bool withDatabase = true, string dbFileName = "conferenceapp.db")
    {
        var bench = new BackupWorkbench(name, dbFileName, keepMax);

        if (withDatabase)
        {
            TestDb.Recreate(bench.DbFile);
            await bench.Db.MigrateAsync();
            await bench.Db.CreateParticipantAsync($"bkp-{Guid.NewGuid():N}@example.test");
        }

        return bench;
    }

    /// <summary>One backup cycle: exactly the one the schedule calls.</summary>
    public Task<BackupOutcome> RunAsync(CancellationToken ct = default)
        => Runner.RunAsync(BackupTrigger.Scheduled, "System", ct);

    /// <summary>The same copy, asked for from the panel instead.</summary>
    public Task<BackupOutcome> RunManualAsync(string actor, CancellationToken ct = default)
        => Runner.RunAsync(BackupTrigger.Manual, actor, ct);

    // ── What is in the folder ────────────────────────────────────────────

    public IReadOnlyList<FileInfo> Automatic() =>
        Directory.Exists(BackupFolder)
            ? DatabaseLocation.ListAutomaticBackups(BackupFolder, DbFile)
            : Array.Empty<FileInfo>();

    public string[] Partials() =>
        Directory.Exists(BackupFolder)
            ? Directory.GetFiles(BackupFolder, "*.db" + DatabaseLocation.PartialExtension)
            : Array.Empty<string>();

    public string[] AllFiles() =>
        Directory.Exists(BackupFolder)
            ? Directory.GetFiles(BackupFolder).Select(Path.GetFileName).ToArray()!
            : Array.Empty<string>();

    /// <summary>A finished backup with a given date, for the rotation checks.</summary>
    public FileInfo PlantBackup(string stamp, DateTime writtenAt, string content = "стар бекъп")
    {
        Directory.CreateDirectory(BackupFolder);

        var path = Path.Combine(BackupFolder,
            $"{Path.GetFileNameWithoutExtension(DbFile)}_{stamp}.db");

        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, writtenAt);

        return new FileInfo(path);
    }

    public string PlantFile(string name, string content = "x")
    {
        Directory.CreateDirectory(BackupFolder);
        var path = Path.Combine(BackupFolder, name);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        Db.Dispose();
        _services.Dispose();
    }
}
