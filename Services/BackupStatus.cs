// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Services
{
    /// <summary>
    /// What <see cref="ICleanupStatus"/> is to the cleanup service, this is to
    /// <see cref="DatabaseBackupService"/>: the difference between "no backup"
    /// and "no backup service".
    /// <para>
    /// The two used to look identical in the Health tab. The backups card judged
    /// solely by what was in the folder, so an empty folder produced
    /// „Провери дали DatabaseBackupService е регистриран и се изпълнява“ — while
    /// the service was running perfectly well and simply had not reached its
    /// first window yet (03:00 and 15:00 UTC, so up to twelve hours after a
    /// restart). The card sent the administrator hunting for a fault that was
    /// not there.
    /// </para>
    /// <para>
    /// <see cref="NextRunAt"/> is the part the folder cannot answer: when the
    /// waiting ends. It is published from the service's own scheduler rather
    /// than recomputed by the health check, so the two can never disagree —
    /// the same reason <see cref="ICleanupStatus.Interval"/> exists.
    /// </para>
    /// Singleton, written by one background service and read by the health
    /// check, which is why the fields go through a lock instead of being plain
    /// auto-properties.
    /// </summary>
    public interface IBackupStatus
    {
        /// <summary>Whether the scheduling loop is running right now.</summary>
        bool Running { get; }

        /// <summary>The next backup window, in UTC, as the service itself
        /// computed it. <c>null</c> until the loop has worked it out.</summary>
        DateTime? NextRunAt { get; }

        /// <summary>When a backup attempt last finished, successfully or not.</summary>
        DateTime? LastRunAt { get; }

        /// <summary>When a backup last finished WITHOUT an error.</summary>
        DateTime? LastSuccessAt { get; }

        /// <summary>The file the last successful backup produced.</summary>
        string? LastFileName { get; }

        /// <summary>The last error, if one has happened since the last success.</summary>
        string? LastError { get; }
        DateTime? LastErrorAt { get; }

        /// <summary>Totals since the process started. They reset on restart.</summary>
        long TotalBackups { get; }
        long TotalFailures { get; }

        void MarkStarted();
        void MarkStopped();
        void MarkNextRun(DateTime nextRunUtc);
        void RecordSuccess(string fileName);
        void RecordFailure(string message);
        void RecordFailure(Exception ex);
    }

    public sealed class BackupStatus : IBackupStatus
    {
        private readonly object _gate = new();

        private bool _running;
        private DateTime? _nextRunAt;
        private DateTime? _lastRunAt;
        private DateTime? _lastSuccessAt;
        private string? _lastFileName;
        private string? _lastError;
        private DateTime? _lastErrorAt;
        private long _totalBackups;
        private long _totalFailures;

        public bool Running            { get { lock (_gate) return _running; } }
        public DateTime? NextRunAt     { get { lock (_gate) return _nextRunAt; } }
        public DateTime? LastRunAt     { get { lock (_gate) return _lastRunAt; } }
        public DateTime? LastSuccessAt { get { lock (_gate) return _lastSuccessAt; } }
        public string? LastFileName    { get { lock (_gate) return _lastFileName; } }
        public string? LastError       { get { lock (_gate) return _lastError; } }
        public DateTime? LastErrorAt   { get { lock (_gate) return _lastErrorAt; } }
        public long TotalBackups       { get { lock (_gate) return _totalBackups; } }
        public long TotalFailures      { get { lock (_gate) return _totalFailures; } }

        public void MarkStarted()
        {
            lock (_gate) _running = true;
        }

        public void MarkStopped()
        {
            lock (_gate)
            {
                _running = false;
                // The window that will never be waited for now: leaving it
                // behind would let the card promise a backup from a service
                // that has stopped.
                _nextRunAt = null;
            }
        }

        public void MarkNextRun(DateTime nextRunUtc)
        {
            lock (_gate) _nextRunAt = nextRunUtc;
        }

        public void RecordSuccess(string fileName)
        {
            lock (_gate)
            {
                _lastRunAt     = DateTime.UtcNow;
                _lastSuccessAt = _lastRunAt;
                _lastFileName  = fileName;
                _lastError     = null;
                _totalBackups++;
            }
        }

        public void RecordFailure(string message)
        {
            lock (_gate)
            {
                _lastRunAt   = DateTime.UtcNow;
                _lastErrorAt = _lastRunAt;
                _lastError   = message;
                _totalFailures++;
            }
        }

        public void RecordFailure(Exception ex)
            => RecordFailure($"{ex.GetType().Name}: {ex.Message}");
    }
}
