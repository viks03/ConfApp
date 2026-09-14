// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Services;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Services;

/// <summary>
/// Part 10: "Health shows a true state for each of them".
/// <para>
/// Against a live application each of the three cards is green and stays green;
/// that is covered in part 6 and is repeated here only for the three background
/// keys. The red states — a stopped consumer, stalled cleanup, a half-written
/// backup — cannot be produced against a live application without breaking it on
/// purpose, and those are exactly what the cards exist for. So the real check is
/// built around precisely that state (<see cref="HealthProbe"/>).
/// </para>
/// </summary>
public class BackgroundHealthTests
{
    // ════════════════════════════════════════════════════════════════════
    // The mail queue
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Опашката_върви_и_е_празна_е_зелено()
    {
        var service = HealthProbe.Build(new FakeQueue { SucceededCount = 12 });

        var (status, message, _, details) = await HealthProbe.ReadAsync(service, "emailQueue");

        Assert.Equal("ok", status);
        Assert.Contains("празна", message);
        Assert.Equal("работи", details["Консуматор"]);
        Assert.StartsWith("12 / 0", details["Изпратени / провалени"]);
    }

    /// <summary>
    /// The most dangerous case: the queue accepts work silently but nobody
    /// processes it. There is no error anywhere; the mail simply does not go
    /// out.
    /// </summary>
    [Fact]
    public async Task Спрян_консуматор_е_червено_и_казва_какво_да_се_прави()
    {
        var service = HealthProbe.Build(new FakeQueue { ConsumerRunning = false, PendingCount = 3 });

        var (status, _, hint, details) = await HealthProbe.ReadAsync(service, "emailQueue");

        Assert.Equal("fail", status);
        Assert.Contains("QueuedHostedService", hint);
        Assert.Equal("спрян", details["Консуматор"]);
    }

    /// <summary>
    /// [E-02] A failure after every retry means somebody never got their message.
    /// The threshold is deliberately zero.
    /// </summary>
    [Fact]
    public async Task Пресен_провал_е_червено()
    {
        var service = HealthProbe.Build(new FakeQueue
        {
            FailedCount        = 2,
            LastFailureAt      = DateTime.UtcNow.AddMinutes(-5),
            LastFailureMessage = "SmtpException: 550 mailbox unavailable"
        });

        var (status, message, hint, details) = await HealthProbe.ReadAsync(service, "emailQueue");

        Assert.Equal("fail", status);
        Assert.Contains("2 писма не тръгнаха", message);
        Assert.Contains("550 mailbox unavailable", hint);
        Assert.Contains("550 mailbox unavailable", details["Последен провал"]);
    }

    [Fact]
    public async Task Стар_провал_е_жълто_а_не_червено()
    {
        var service = HealthProbe.Build(new FakeQueue
        {
            FailedCount        = 1,
            LastFailureAt      = DateTime.UtcNow.AddDays(-3),
            LastFailureMessage = "SmtpException: timeout"
        });

        var (status, message, _, _) = await HealthProbe.ReadAsync(service, "emailQueue");

        Assert.Equal("warn", status);
        Assert.Contains("не в последните 24 часа", message);
    }

    [Fact]
    public async Task Натрупана_опашка_е_жълто()
    {
        var service = HealthProbe.Build(new FakeQueue { PendingCount = 51 });

        var (status, message, hint, _) = await HealthProbe.ReadAsync(service, "emailQueue");

        Assert.Equal("warn", status);
        Assert.Contains("51", message);
        Assert.Contains("SMTP", hint);
    }

    [Fact]
    public async Task Няколко_чакащи_задачи_още_са_зелено()
    {
        var service = HealthProbe.Build(new FakeQueue { PendingCount = 4 });

        var (status, message, _, details) = await HealthProbe.ReadAsync(service, "emailQueue");

        Assert.Equal("ok", status);
        Assert.Contains("4 задачи чакат ред", message);
        Assert.Equal("4", details["Чакащи задачи"]);
    }

    // ════════════════════════════════════════════════════════════════════
    // The cleanup ([S-04])
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Пресен_цикъл_е_зелено()
    {
        var service = HealthProbe.Build(cleanup: new FakeCleanupStatus
        {
            LastSuccessAt       = DateTime.UtcNow.AddMinutes(-10),
            LastDeletedAccounts = 2,
            LastExpiredOrders   = 5,
            TotalCycles         = 9
        });

        var (status, _, _, details) = await HealthProbe.ReadAsync(service, "cleanup");

        Assert.Equal("ok", status);
        Assert.Equal("работи", details["Услуга"]);
        Assert.Equal("на всеки 1 ч.", details["Интервал"]);
        Assert.Equal("2 профила, 5 крипто поръчки", details["Последният цикъл хвана"]);
    }

    [Fact]
    public async Task Спряло_чистене_е_червено()
    {
        var service = HealthProbe.Build(cleanup: new FakeCleanupStatus { Running = false });

        var (status, _, hint, details) = await HealthProbe.ReadAsync(service, "cleanup");

        Assert.Equal("fail", status);
        Assert.Contains("CleanupService", hint);
        Assert.Equal("спряна", details["Услуга"]);
    }

    [Fact]
    public async Task Услуга_без_нито_един_цикъл_е_жълто()
    {
        var service = HealthProbe.Build(cleanup: new FakeCleanupStatus
        {
            LastRunAt     = null,
            LastSuccessAt = null,
            TotalCycles   = 0
        });

        var (status, message, _, _) = await HealthProbe.ReadAsync(service, "cleanup");

        Assert.Equal("warn", status);
        Assert.Contains("още не е приключила цикъл", message);
    }

    [Fact]
    public async Task Първи_цикъл_с_грешка_е_червено()
    {
        var service = HealthProbe.Build(cleanup: new FakeCleanupStatus
        {
            LastRunAt     = DateTime.UtcNow,
            LastSuccessAt = null,
            LastError     = "SqliteException: database is locked",
            LastErrorAt   = DateTime.UtcNow,
            TotalCycles   = 1,
            TotalFailures = 1
        });

        var (status, message, hint, _) = await HealthProbe.ReadAsync(service, "cleanup");

        Assert.Equal("fail", status);
        Assert.Contains("Нито един цикъл", message);
        Assert.Contains("database is locked", hint);
    }

    /// <summary>
    /// One missed cycle is a warning and two are a failure, and the threshold is
    /// derived from the service's own interval so that the two cannot drift apart
    /// when the number changes.
    /// </summary>
    [Theory]
    [InlineData(70,  "warn")]   // a little over one interval
    [InlineData(150, "fail")]   // over two
    public async Task Закъснелият_цикъл_се_мери_спрямо_интервала(int minutesAgo, string expected)
    {
        var service = HealthProbe.Build(cleanup: new FakeCleanupStatus
        {
            Interval      = TimeSpan.FromHours(1),
            LastRunAt     = DateTime.UtcNow.AddMinutes(-minutesAgo),
            LastSuccessAt = DateTime.UtcNow.AddMinutes(-minutesAgo)
        });

        var (status, _, _, _) = await HealthProbe.ReadAsync(service, "cleanup");

        Assert.Equal(expected, status);
    }

    [Fact]
    public async Task Провалил_се_последен_цикъл_след_успешен_е_жълто()
    {
        var service = HealthProbe.Build(cleanup: new FakeCleanupStatus
        {
            LastSuccessAt = DateTime.UtcNow.AddMinutes(-20),
            LastRunAt     = DateTime.UtcNow.AddMinutes(-2),
            LastError     = "TimeoutException: изтече",
            LastErrorAt   = DateTime.UtcNow.AddMinutes(-2)
        });

        var (status, message, _, _) = await HealthProbe.ReadAsync(service, "cleanup");

        Assert.Equal("warn", status);
        Assert.Contains("Последният цикъл се провали", message);
    }

    /// <summary>
    /// The interval is published by the service ([S-04]); Health does not carry a
    /// copy of it. If the number in <c>CleanupService</c> changes, the card has to
    /// say so of its own accord.
    /// </summary>
    [Fact]
    public async Task Интервалът_идва_от_самата_услуга()
    {
        var status = new FakeCleanupStatus();
        status.MarkStarted(CleanupService.Interval);

        var service = HealthProbe.Build(cleanup: status);
        var (_, _, _, details) = await HealthProbe.ReadAsync(service, "cleanup");

        Assert.Equal($"на всеки {CleanupService.Interval.TotalHours:0.#} ч.", details["Интервал"]);
    }

    // ════════════════════════════════════════════════════════════════════
    // The backups ([S-01])
    // ════════════════════════════════════════════════════════════════════

    private static (string Root, string Db) NewBackupFolder(string name)
    {
        var root = Path.Combine(TestPaths.RunScratch, "health-backups-" + name + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(Path.Combine(root, "backups"));

        var db = Path.Combine(root, "conferenceapp.db");
        File.WriteAllBytes(db, new byte[200_000]);

        return (root, db);
    }

    private static string Plant(string root, string name, int bytes, DateTime writtenAtUtc)
    {
        var path = Path.Combine(root, "backups", name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, writtenAtUtc);
        return path;
    }

    [Fact]
    public async Task Липсваща_папка_е_червено()
    {
        var root = Path.Combine(TestPaths.RunScratch, "health-no-folder-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(root);

        var service = HealthProbe.Build(contentRoot: root);
        var (status, message, hint, _) = await HealthProbe.ReadAsync(service, "backups");

        Assert.Equal("fail", status);
        Assert.Contains("не съществува", message);
        Assert.Contains(Path.Combine(root, "backups"), hint);
    }

    // ── An empty folder: three situations, not one ([T-36]) ──────────────
    //
    // The card used to answer all three the same way — "Няма нито едно резервно
    // копие. Провери дали DatabaseBackupService е регистриран и се изпълнява" —
    // and only one of the three was about a broken service. On a freshly started
    // application the message sent the administrator hunting for a fault that
    // was not there: the windows are 03:00 and 15:00 UTC, so up to twelve hours
    // pass between a restart and the first copy.

    [Fact]
    public async Task Празна_папка_при_спряна_услуга_е_червено()
    {
        var (root, db) = NewBackupFolder("empty-stopped");

        var service = HealthProbe.Build(
            backups: new FakeBackupStatus { Running = false, NextRunAt = null },
            contentRoot: root, databaseFile: db);

        var (status, message, hint, details) = await HealthProbe.ReadAsync(service, "backups");

        Assert.Equal("fail", status);
        Assert.Contains("Няма нито едно", message);
        Assert.Contains("услугата не работи", message);
        Assert.Contains("DatabaseBackupService", hint);
        Assert.Equal("спряна", details["Услуга"]);
    }

    /// <summary>
    /// The running service with nothing in the folder yet: a warning, and the
    /// message says when the wait ends instead of naming a culprit. This is the
    /// state of every application for the first hours after a restart.
    /// </summary>
    [Fact]
    public async Task Празна_папка_при_работеща_услуга_е_предупреждение_с_час_на_следващото()
    {
        var (root, db) = NewBackupFolder("empty-running");

        var nextRun = DateTime.UtcNow.AddHours(11.9);
        var service = HealthProbe.Build(
            backups: new FakeBackupStatus { Running = true, NextRunAt = nextRun },
            contentRoot: root, databaseFile: db);

        var (status, message, hint, details) = await HealthProbe.ReadAsync(service, "backups");

        Assert.Equal("warn", status);

        // The two things the old message got wrong: it claimed something was
        // broken, and it did not say when the first copy would come.
        Assert.DoesNotContain("Провери дали DatabaseBackupService е регистриран", hint);
        // The decimal separator follows the culture the process happens to run
        // in, and the test is not about that.
        Assert.Matches(@"11[.,]9 ч\.", message);
        Assert.Contains(nextRun.ToLocalTime().ToString("HH:mm"), message);

        Assert.Equal("работи", details["Услуга"]);
        Assert.Contains(nextRun.ToLocalTime().ToString("HH:mm"), details["Следващо копие"]);
    }

    /// <summary>
    /// A service that is running and has already failed is broken whatever the
    /// folder looks like — a warning would hide it.
    /// </summary>
    [Fact]
    public async Task Празна_папка_след_провален_опит_е_червено_с_причината()
    {
        var (root, db) = NewBackupFolder("empty-failed");

        var service = HealthProbe.Build(
            backups: new FakeBackupStatus
            {
                Running   = true,
                LastError = "IOException: няма място на диска",
                LastErrorAt = DateTime.UtcNow.AddMinutes(-5)
            },
            contentRoot: root, databaseFile: db);

        var (status, _, hint, _) = await HealthProbe.ReadAsync(service, "backups");

        Assert.Equal("fail", status);
        Assert.Contains("няма място на диска", hint);
    }

    /// <summary>
    /// The state of the service goes on every branch, not only the empty ones —
    /// a green card still has to say when the next copy is due.
    /// </summary>
    [Fact]
    public async Task Картата_винаги_показва_състоянието_на_услугата()
    {
        var (root, db) = NewBackupFolder("service-row");
        Plant(root, "conferenceapp_20260912_0300.db", 200_000, DateTime.UtcNow.AddHours(-2));

        var service = HealthProbe.Build(contentRoot: root, databaseFile: db);
        var (status, _, _, details) = await HealthProbe.ReadAsync(service, "backups");

        Assert.Equal("ok", status);
        Assert.Equal("работи", details["Услуга"]);
        Assert.NotEqual("—", details["Следващо копие"]);
    }

    /// <summary>
    /// The other half of [T-36]. A stopped service with a fresh copy in the
    /// folder used to leave the card green until the file itself grew old — 26
    /// hours before so much as a warning, 48 before a problem. The card is
    /// asked to say it now, while the newest copy is two hours old.
    /// </summary>
    [Fact]
    public async Task Спряна_услуга_се_вижда_веднага_а_не_след_денонощие()
    {
        var (root, db) = NewBackupFolder("no-next");
        Plant(root, "conferenceapp_20260912_0300.db", 200_000, DateTime.UtcNow.AddHours(-2));

        var service = HealthProbe.Build(
            backups: new FakeBackupStatus { Running = false, NextRunAt = null },
            contentRoot: root, databaseFile: db);

        var (status, message, hint, details) = await HealthProbe.ReadAsync(service, "backups");

        Assert.Equal("warn", status);
        Assert.Contains("услугата не работи", message);
        Assert.Contains("ново няма да бъде направено", hint);

        Assert.Equal("спряна", details["Услуга"]);
        Assert.Contains("не работи", details["Следващо копие"]);
    }

    /// <summary>
    /// A failed attempt behind a good copy: the copies are fine, but something
    /// went wrong since, and the card must not hide it behind the green.
    /// </summary>
    [Fact]
    public async Task Провален_опит_зад_добро_копие_е_предупреждение()
    {
        var (root, db) = NewBackupFolder("failed-after-good");
        Plant(root, "conferenceapp_20260912_0300.db", 200_000, DateTime.UtcNow.AddHours(-2));

        var service = HealthProbe.Build(
            backups: new FakeBackupStatus
            {
                Running     = true,
                LastError   = "IOException: няма място на диска",
                LastErrorAt = DateTime.UtcNow.AddMinutes(-3)
            },
            contentRoot: root, databaseFile: db);

        var (status, message, hint, _) = await HealthProbe.ReadAsync(service, "backups");

        Assert.Equal("warn", status);
        Assert.Contains("се провали", message);
        Assert.Contains("няма място на диска", hint);
    }

    [Fact]
    public async Task Пресно_пълноценно_копие_е_зелено()
    {
        var (root, db) = NewBackupFolder("good");
        Plant(root, "conferenceapp_20260912_0300.db", 200_000, DateTime.UtcNow.AddHours(-2));

        var service = HealthProbe.Build(contentRoot: root, databaseFile: db);
        var (status, _, _, details) = await HealthProbe.ReadAsync(service, "backups");

        Assert.Equal("ok", status);
        Assert.Equal("0", details["Недовършени файлове"]);
        Assert.StartsWith("1 ", details["Брой копия"]);
    }

    /// <summary>
    /// [S-01] The check on the contents used to come AFTER the ones on age and was
    /// only <c>== 0</c>, so a 40 MB backup broken off halfway passed as valid.
    /// </summary>
    [Fact]
    public async Task Копие_наполовина_е_червено()
    {
        var (root, db) = NewBackupFolder("half");
        Plant(root, "conferenceapp_20260912_0300.db", 20_000, DateTime.UtcNow.AddMinutes(-30));

        var service = HealthProbe.Build(contentRoot: root, databaseFile: db);
        var (status, message, hint, _) = await HealthProbe.ReadAsync(service, "backups");

        Assert.Equal("fail", status);
        Assert.Contains("при база от", message);
        Assert.Contains("прекъснато по средата", hint);
    }

    [Fact]
    public async Task Празен_файл_е_червено()
    {
        var (root, db) = NewBackupFolder("zero");
        Plant(root, "conferenceapp_20260912_0300.db", 0, DateTime.UtcNow.AddMinutes(-30));

        var service = HealthProbe.Build(contentRoot: root, databaseFile: db);
        var (status, message, _, _) = await HealthProbe.ReadAsync(service, "backups");

        Assert.Equal("fail", status);
        Assert.Contains("нулев размер", message);
    }

    [Theory]
    [InlineData(30, "warn")]   // over 26 hours
    [InlineData(72, "fail")]   // over 48
    public async Task Старото_копие_се_мери_по_възраст(int hoursAgo, string expected)
    {
        var (root, db) = NewBackupFolder("age" + hoursAgo);
        Plant(root, "conferenceapp_20260101_0300.db", 200_000, DateTime.UtcNow.AddHours(-hoursAgo));

        var service = HealthProbe.Build(contentRoot: root, databaseFile: db);
        var (status, _, _, _) = await HealthProbe.ReadAsync(service, "backups");

        Assert.Equal(expected, status);
    }

    /// <summary>
    /// A leftover file does not make the latest backup invalid, but it stays a
    /// signal: the service stopped in the middle of a cycle.
    /// </summary>
    [Fact]
    public async Task Остатъкът_от_прекъснат_опит_е_жълто()
    {
        var (root, db) = NewBackupFolder("partial");
        Plant(root, "conferenceapp_20260912_0300.db", 200_000, DateTime.UtcNow.AddHours(-1));
        Plant(root, "conferenceapp_20260912_1500.db" + DatabaseLocation.PartialExtension,
              90_000, DateTime.UtcNow.AddMinutes(-5));

        var service = HealthProbe.Build(contentRoot: root, databaseFile: db);
        var (status, message, _, details) = await HealthProbe.ReadAsync(service, "backups");

        Assert.Equal("warn", status);
        Assert.Contains("недовършени файла", message);
        Assert.Equal("1", details["Недовършени файлове"]);
        // And most importantly: the unfinished one does not count as a backup.
        Assert.StartsWith("1 ", details["Брой копия"]);
    }

    [Fact]
    public async Task Ръчното_копие_не_се_брои_за_автоматично()
    {
        var (root, db) = NewBackupFolder("manual");
        Plant(root, "conferenceapp_20260912_0300.db", 200_000, DateTime.UtcNow.AddHours(-1));
        Plant(root, "conferenceapp_predeploy.db", 200_000, DateTime.UtcNow);

        var service = HealthProbe.Build(contentRoot: root, databaseFile: db);
        var (status, _, _, details) = await HealthProbe.ReadAsync(service, "backups");

        Assert.Equal("ok", status);
        Assert.StartsWith("1 ", details["Брой копия"]);
        Assert.Contains("conferenceapp_20260912_0300.db", details["Последно"]);
    }

    [Fact]
    public async Task Броят_копия_показва_и_колко_се_пазят()
    {
        var (root, db) = NewBackupFolder("keep");
        Plant(root, "conferenceapp_20260912_0300.db", 200_000, DateTime.UtcNow.AddHours(-1));

        var service = HealthProbe.Build(contentRoot: root, databaseFile: db, keepMaxBackups: 7);
        var (_, _, _, details) = await HealthProbe.ReadAsync(service, "backups");

        Assert.Equal("1 (пази се максимум 7)", details["Брой копия"]);
    }
}
