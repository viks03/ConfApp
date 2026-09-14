// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Reflection;
using ConferenceApp.Services;
using ConferenceApp.Tests.Fixtures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ConferenceApp.Tests.Services;

/// <summary>
/// Part 10: "the backup is made on schedule" and "a backup broken off halfway
/// does not count as valid".
/// </summary>
[Collection(BackgroundCollection.Name)]
public class BackupServiceTests
{
    private readonly CleanupRun _run;

    public BackupServiceTests(CleanupRun run) => _run = run;

    // ════════════════════════════════════════════════════════════════════
    // The schedule
    // ════════════════════════════════════════════════════════════════════

    private static readonly MethodInfo NextRun =
        typeof(DatabaseBackupService).GetMethod(
            "GetNextRunTime", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("DatabaseBackupService.GetNextRunTime го няма.");

    private static DateTime Next(DateTime utcNow) => (DateTime)NextRun.Invoke(null, new object[] { utcNow })!;

    [Theory]
    [InlineData("2026-09-12T00:00:00Z", "2026-09-12T03:00:00Z")]
    [InlineData("2026-09-12T02:59:59Z", "2026-09-12T03:00:00Z")]
    [InlineData("2026-09-12T03:00:00Z", "2026-09-12T15:00:00Z")] // on the hour itself: the next window
    [InlineData("2026-09-12T03:00:01Z", "2026-09-12T15:00:00Z")]
    [InlineData("2026-09-12T14:59:59Z", "2026-09-12T15:00:00Z")]
    [InlineData("2026-09-12T15:00:01Z", "2026-09-13T03:00:00Z")]
    [InlineData("2026-09-12T23:59:59Z", "2026-09-13T03:00:00Z")]
    // Across a month and a year boundary: the arithmetic uses AddDays rather than
    // incrementing the day.
    [InlineData("2026-09-30T20:00:00Z", "2026-10-01T03:00:00Z")]
    [InlineData("2026-12-31T20:00:00Z", "2027-01-01T03:00:00Z")]
    public void Разписанието_е_в_03_и_15_UTC(string now, string expected)
    {
        var utcNow = DateTime.Parse(now, null, System.Globalization.DateTimeStyles.AdjustToUniversal
                                                | System.Globalization.DateTimeStyles.AssumeUniversal);

        Assert.Equal(
            DateTime.Parse(expected, null, System.Globalization.DateTimeStyles.AdjustToUniversal
                                           | System.Globalization.DateTimeStyles.AssumeUniversal),
            Next(utcNow));
    }

    /// <summary>
    /// The next window is always in the future and never more than twelve hours
    /// away. Otherwise <c>Task.Delay</c> gets a negative interval — an exception at
    /// startup — or the service skips a whole day.
    /// </summary>
    [Fact]
    public void Разписанието_винаги_е_напред_и_не_повече_от_дванайсет_часа()
    {
        var start = new DateTime(2026, 3, 28, 0, 0, 0, DateTimeKind.Utc);

        // Two days minute by minute, including the day local time moves to summer
        // time. The hours are deliberately in UTC: that is exactly what keeps the
        // arithmetic from shifting twice a year.
        for (int minute = 0; minute < 60 * 48; minute++)
        {
            var now  = start.AddMinutes(minute);
            var next = Next(now);
            var wait = next - now;

            Assert.True(wait > TimeSpan.Zero, $"В {now:O} разписанието връща минало време: {next:O}.");
            Assert.True(wait <= TimeSpan.FromHours(12), $"В {now:O} се чака {wait.TotalHours:0.0} часа.");
            Assert.Equal(DateTimeKind.Utc, next.Kind);
            Assert.Contains(next.Hour, new[] { 3, 15 });
            Assert.Equal(0, next.Minute);
        }
    }

    /// <summary>
    /// [S-02]: the path to the file goes into the log at startup. When the working
    /// directory and the content root disagree, that is the only place the file
    /// actually being copied can be seen.
    /// </summary>
    [Fact]
    public async Task Услугата_обявява_в_лога_кой_файл_копира()
    {
        var line = await _run.Probe.WaitForLogLineAsync(
            "Database Backup Service started", TimeSpan.FromSeconds(20));

        Assert.NotNull(line);
        Assert.Contains(Path.GetFullPath(_run.DbFile), line);
        Assert.DoesNotContain(Path.GetFullPath(TestPaths.LiveDbFile), line);
    }

    [Fact]
    public async Task Услугата_обявява_кога_е_следващото_копие()
    {
        var line = await _run.Probe.WaitForLogLineAsync(
            "Next database backup scheduled", TimeSpan.FromSeconds(20));

        Assert.NotNull(line);
        Assert.Contains("UTC", line);
    }

    // ════════════════════════════════════════════════════════════════════
    // The backup itself
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Копието_се_създава_под_име_с_дата_и_час()
    {
        using var bench = await BackupWorkbench.CreateAsync("made");

        await bench.RunAsync();

        var made = Assert.Single(bench.Automatic());

        Assert.StartsWith("conferenceapp_", made.Name);
        Assert.EndsWith(".db", made.Name);
        Assert.Equal(DateTime.UtcNow.ToString("yyyyMMdd_HH")[..8], made.Name[14..22]);
    }

    /// <summary>
    /// The backup has to be a database one can restore from, not bytes copied off a
    /// live file. So it is opened and read.
    /// </summary>
    [Fact]
    public async Task Копието_е_четима_база_със_същите_редове()
    {
        using var bench = await BackupWorkbench.CreateAsync("readable");

        var expected = await bench.Db.ReadAsync(db => db.Users.CountAsync());

        await bench.RunAsync();

        var made = Assert.Single(bench.Automatic());

        using var connection = new SqliteConnection($"Data Source={made.FullName};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM AspNetUsers";

        Assert.Equal(expected, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Копието_оставя_ред_в_одита()
    {
        using var bench = await BackupWorkbench.CreateAsync("audit");

        await bench.RunAsync();

        var made = Assert.Single(bench.Automatic());

        var rows = await bench.Db.ReadAsync(db => db.AuditLogs
            .AsNoTracking()
            .Where(a => a.Action == "Database Backup")
            .ToListAsync());

        var row = Assert.Single(rows);

        Assert.Equal("System", row.UserEmail);
        // A background service has no request, so the address column holds "System"
        // rather than "Unknown".
        Assert.Equal("System", row.IpAddress);
        Assert.Null(row.UserId);
        Assert.Contains(made.Name, row.Details);
    }

    [Fact]
    public async Task След_успешно_копие_в_папката_няма_недовършени_файлове()
    {
        using var bench = await BackupWorkbench.CreateAsync("clean");

        await bench.RunAsync();

        Assert.Empty(bench.Partials());
    }

    // ════════════════════════════════════════════════════════════════════
    // A backup broken off halfway
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// [S-01] An interrupted copy must leave nothing behind. The file used to be
    /// written straight under its final name and stayed in the folder as the newest
    /// one, so every check declared it the latest backup.
    /// </summary>
    [Fact]
    public async Task Прекъснато_копиране_не_оставя_файл()
    {
        using var bench = await BackupWorkbench.CreateAsync("interrupted");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bench.RunAsync(cts.Token));

        Assert.Empty(bench.Automatic());
        Assert.Empty(bench.Partials());
    }

    /// <summary>
    /// If the process stopped in the middle, the leftover stays on disk. The next
    /// cycle has to clear it away; otherwise files the size of the database pile
    /// up.
    /// </summary>
    [Fact]
    public async Task Остатък_от_прекъснат_опит_се_чисти_при_следващото_копие()
    {
        using var bench = await BackupWorkbench.CreateAsync("leftover");

        var leftover = bench.PlantFile(
            "conferenceapp_20260101_0300.db" + DatabaseLocation.PartialExtension,
            "половин база");

        await bench.RunAsync();

        Assert.False(File.Exists(leftover), "Недовършеният файл от предишния опит още стои.");
        Assert.Empty(bench.Partials());
        Assert.True(bench.Log.Has(LogLevel.Warning, "Намерено недовършено копие"),
            "Изчистването на остатъка стана мълчаливо:\n" + bench.Log.Text);
    }

    [Fact]
    public async Task Недовършеният_файл_не_се_брои_за_копие()
    {
        using var bench = await BackupWorkbench.CreateAsync("not-counted");

        bench.PlantFile("conferenceapp_20260101_0300.db" + DatabaseLocation.PartialExtension, "половин база");

        Assert.Empty(bench.Automatic());
    }

    // ════════════════════════════════════════════════════════════════════
    // Rotation
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Ротацията_пази_само_последните()
    {
        using var bench = await BackupWorkbench.CreateAsync("rotate", keepMax: 3);

        for (int day = 1; day <= 5; day++)
            bench.PlantBackup($"2026010{day}_0300", new DateTime(2026, 1, day, 3, 0, 0, DateTimeKind.Utc));

        await bench.RunAsync();

        var left = bench.Automatic();

        Assert.Equal(3, left.Count);
        // The new one and the two most recent old ones; the three oldest are gone.
        Assert.Contains(left, f => f.Name.Contains(DateTime.UtcNow.ToString("yyyyMMdd")));
        Assert.Contains(left, f => f.Name == "conferenceapp_20260105_0300.db");
        Assert.Contains(left, f => f.Name == "conferenceapp_20260104_0300.db");
        Assert.DoesNotContain(left, f => f.Name == "conferenceapp_20260101_0300.db");
    }

    /// <summary>
    /// [S-01] A copy left by hand before a migration is not an automatic one and
    /// must not enter the rotation; otherwise it is the first to go.
    /// </summary>
    [Fact]
    public async Task Ротацията_не_пипа_ръчно_оставено_копие()
    {
        using var bench = await BackupWorkbench.CreateAsync("rotate-manual", keepMax: 1);

        var manual = bench.PlantFile("conferenceapp_predeploy.db", "ръчно копие преди миграция");
        var other  = bench.PlantFile("something_else.db", "чужд файл");

        for (int day = 1; day <= 3; day++)
            bench.PlantBackup($"2026010{day}_0300", new DateTime(2026, 1, day, 3, 0, 0, DateTimeKind.Utc));

        await bench.RunAsync();

        Assert.True(File.Exists(manual), "Ротацията изтри ръчно оставеното копие.");
        Assert.True(File.Exists(other),  "Ротацията изтри чужд файл от папката.");
        Assert.Single(bench.Automatic());
    }

    // ════════════════════════════════════════════════════════════════════
    // When there is nothing to copy
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// [S-02] A missing database file means no backups are being made at all. It
    /// used to be a <c>LogWarning</c>: the quietest possible message about the most
    /// expensive possible failure. The line has to name both the path that was
    /// looked for and the setting that supplies it.
    /// </summary>
    [Fact]
    public async Task Липсващ_файл_на_базата_е_грешка_с_път_и_настройка()
    {
        using var bench = await BackupWorkbench.CreateAsync("missing", withDatabase: false);

        await bench.RunAsync();

        Assert.Empty(bench.Automatic());
        Assert.Empty(bench.Partials());

        Assert.True(bench.Log.Has(LogLevel.Error, "Database file not found"),
            "Липсващата база не е обявена като грешка:\n" + bench.Log.Text);
        Assert.Contains(bench.DbFile, bench.Log.Text);
        Assert.Contains("ConnectionStrings:DefaultConnection", bench.Log.Text);
        Assert.Contains(DatabaseLocation.OverrideKey, bench.Log.Text);
    }
}
