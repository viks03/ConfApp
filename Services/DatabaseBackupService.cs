// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConferenceApp.Services
{
    /// <summary>
    /// The schedule, and nothing else. The copying itself lives in
    /// <see cref="IDatabaseBackupRunner"/>, so that the button in the Health tab
    /// runs the same code rather than a second implementation of it.
    /// </summary>
    public class DatabaseBackupService : BackgroundService
    {
        private readonly ILogger<DatabaseBackupService> _logger;
        private readonly IDatabaseBackupRunner _runner;
        private readonly IBackupStatus _status;

        // Backup windows, in UTC. Two a day, twelve hours apart, both outside
        // Bulgarian working hours (06:00 and 18:00 local) so that the copy does
        // not compete with registrations.
        private static readonly int[] BackupHoursUtc = [3, 15];

        public DatabaseBackupService(
            ILogger<DatabaseBackupService> logger,
            IDatabaseBackupRunner runner,
            IBackupStatus status)
        {
            _logger = logger;
            _runner = runner;
            _status = status;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // The path goes into the log at startup: when the working directory
            // and the content root differ, this is the only place that shows
            // which file is actually being copied.
            _logger.LogInformation("Database Backup Service started. Database file: {Path}", _runner.DatabaseFile);

            // The folder is created HERE rather than at the first backup.
            //
            // The windows are 03:00 and 15:00 UTC, so hours pass between startup
            // and the first copy. Until then the folder did not exist and Health
            // reported "the backup folder does not exist" — which reads like a
            // broken service, while the service is running and waiting its turn.
            // The other folders (App_Data, logs, the Data Protection keys) have
            // long been created at startup; backups were the exception.
            _runner.EnsureFolder();

            // From here on the card in the Health tab can tell "the service is
            // not running" from "the service is running and the first window has
            // not come yet" — see IBackupStatus. The folder alone could not: an
            // empty folder looked like a fault in both cases.
            _status.MarkStarted();

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    // UTC throughout: a schedule in local time would run twice or
                    // not at all on the daylight-saving switch.
                    var now     = DateTime.UtcNow;
                    var nextRun = GetNextRunTime(now);
                    var delay   = nextRun - now;

                    // Published rather than recomputed by the health check, so
                    // that the card and the service can never disagree about
                    // when the next copy is due.
                    _status.MarkNextRun(nextRun);

                    _logger.LogInformation(
                        "Next database backup scheduled in {Hours:F1}h (at {Time:HH:mm} UTC).",
                        delay.TotalHours, nextRun);

                    // [S-09] The `when` clause matters: without it a cancellation
                    // from any other source looked like an application shutdown, the
                    // service left the loop for good, and the log line claimed it had
                    // stopped normally.
                    try
                    {
                        await Task.Delay(delay, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected cancellation while waiting for the next backup window.");
                        continue;
                    }

                    try
                    {
                        // The runner logs, records and audits the attempt itself;
                        // a failure comes back as an outcome rather than an
                        // exception, and the loop simply waits for the next
                        // window.
                        await _runner.RunAsync(BackupTrigger.Scheduled, "System", stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Database Backup Service is stopping gracefully.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error occurred while backing up the database.");
                    }
                }
            }
            finally
            {
                // Whichever way the loop ended — shutdown or an exception nobody
                // caught — the card must stop claiming the service is alive.
                _status.MarkStopped();
            }
        }

        // ── The next backup time, in UTC ───────────────────────────────────────
        private static DateTime GetNextRunTime(DateTime utcNow)
        {
            foreach (var hour in BackupHoursUtc)
            {
                var candidate = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, hour, 0, 0, DateTimeKind.Utc);
                if (candidate > utcNow)
                    return candidate;
            }

            // Every window today has passed: the first one tomorrow.
            return new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, BackupHoursUtc[0], 0, 0, DateTimeKind.Utc)
                .AddDays(1);
        }
    }
}
