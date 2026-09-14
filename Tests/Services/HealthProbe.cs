// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Services;
using ConferenceApp.Services.Health;
using ConferenceApp.Tests.Fixtures;
using Microsoft.Extensions.Configuration;

namespace ConferenceApp.Tests.Services;

/// <summary>
/// The three background checks (<c>emailQueue</c>, <c>cleanup</c>,
/// <c>backups</c>) read <b>only</b> in-memory state and one folder on disk: none
/// of them touches the database, SMTP or any external API.
/// <para>
/// So the real <see cref="HealthCheckService"/> is built here with exactly the
/// state that needs checking. Otherwise "the queue has stopped" and "the latest
/// backup is half-written" cannot be produced against a live application at all
/// without breaking it — and those are precisely the cases these cards exist
/// for.
/// </para>
/// </summary>
internal static class HealthProbe
{
    public static HealthCheckService Build(
        IBackgroundTaskQueue? queue = null,
        ICleanupStatus? cleanup = null,
        IBackupStatus? backups = null,
        string? contentRoot = null,
        string? databaseFile = null,
        int keepMaxBackups = 14)
    {
        var root = contentRoot ?? Path.Combine(TestPaths.RunScratch, "health-" + Guid.NewGuid().ToString("N")[..8]);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseLocation.OverrideKey]    = databaseFile ?? Path.Combine(root, "conferenceapp.db"),
                ["BackupSettings:KeepMaxBackups"] = keepMaxBackups.ToString()
            })
            .Build();

        return new HealthCheckService(
            scopes:      null!,   // used only by the database check
            config:      config,
            env:         new ScratchEnvironment(root),
            queue:       queue   ?? new FakeQueue(),
            templates:   null!,   // only by the templates check
            httpFactory: null!,   // only by Stripe and Go28
            uploadPaths: null!,   // only by the disk check
            cleanup:     cleanup ?? new FakeCleanupStatus(),
            backups:     backups ?? new FakeBackupStatus(),
            logger:      new CapturingLogger<HealthCheckService>());
    }

    public static async Task<(string Status, string Message, string? Hint, Dictionary<string, string> Details)>
        ReadAsync(HealthCheckService service, string key)
    {
        var result = await service.CheckAsync(key);

        var details = result.Details?.ToDictionary(d => d.Label, d => d.Value)
                      ?? new Dictionary<string, string>();

        return (result.Status, result.Message, result.Hint, details);
    }
}

/// <summary>
/// A queue whose state is set directly. <see cref="BackgroundTaskQueue"/> itself
/// is exercised in <see cref="EmailQueueTests"/>; the subject here is reading the
/// state, and some of those states — a failure two days ago — cannot be produced
/// any other way than by waiting.
/// </summary>
internal sealed class FakeQueue : IBackgroundTaskQueue
{
    public int PendingCount { get; set; }
    public int RejectedCount { get; set; }
    public int FailedCount { get; set; }
    public int SucceededCount { get; set; }
    public bool ConsumerRunning { get; set; } = true;
    public DateTime? LastActivityAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastFailureAt { get; set; }
    public string? LastFailureMessage { get; set; }

    public void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem) => PendingCount++;
    public ValueTask<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();
    public bool TryDequeue(out Func<CancellationToken, Task>? workItem) { workItem = null; return false; }
    public void CompleteAdding() { }
    public void RecordSuccess() => SucceededCount++;
    public void RecordFailure(string message)
    {
        FailedCount++;
        LastFailureAt = DateTime.UtcNow;
        LastFailureMessage = message;
    }
}

/// <summary>
/// The backup service's state, set directly. [T-36]: "the folder is empty" means
/// one thing when the service is running and waiting for 03:00 and quite another
/// when it is not running at all, and no arrangement of files on disk can tell
/// the two apart — only this can.
/// </summary>
internal sealed class FakeBackupStatus : IBackupStatus
{
    public bool Running { get; set; } = true;
    public DateTime? NextRunAt { get; set; } = DateTime.UtcNow.AddHours(11.9);
    public DateTime? LastRunAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public string? LastFileName { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastErrorAt { get; set; }
    public long TotalBackups { get; set; }
    public long TotalFailures { get; set; }

    public void MarkStarted() => Running = true;
    public void MarkStopped() { Running = false; NextRunAt = null; }
    public void MarkNextRun(DateTime nextRunUtc) => NextRunAt = nextRunUtc;

    public void RecordSuccess(string fileName)
    {
        LastRunAt = LastSuccessAt = DateTime.UtcNow;
        LastFileName = fileName;
        LastError = null;
        TotalBackups++;
    }

    public void RecordFailure(string message)
    {
        LastRunAt = LastErrorAt = DateTime.UtcNow;
        LastError = message;
        TotalFailures++;
    }

    public void RecordFailure(Exception ex) => RecordFailure($"{ex.GetType().Name}: {ex.Message}");
}

/// <summary>The cleanup's state, set directly, for the same reason.</summary>
internal sealed class FakeCleanupStatus : ICleanupStatus
{
    public bool Running { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);
    public DateTime? LastRunAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSuccessAt { get; set; } = DateTime.UtcNow;
    public string? LastError { get; set; }
    public DateTime? LastErrorAt { get; set; }
    public int LastDeletedAccounts { get; set; }
    public int LastExpiredOrders { get; set; }
    public long TotalCycles { get; set; } = 1;
    public long TotalFailures { get; set; }

    public void MarkStarted(TimeSpan interval) { Running = true; Interval = interval; }
    public void MarkStopped() => Running = false;
    public void RecordSuccess(int deletedAccounts, int expiredOrders)
    {
        LastRunAt = LastSuccessAt = DateTime.UtcNow;
        LastDeletedAccounts = deletedAccounts;
        LastExpiredOrders = expiredOrders;
    }
    public void RecordFailure(Exception ex)
    {
        LastRunAt = LastErrorAt = DateTime.UtcNow;
        LastError = ex.Message;
        TotalFailures++;
    }
}
