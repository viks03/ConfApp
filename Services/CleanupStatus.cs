// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Services
{
    /// <summary>
    /// [S-04]: <see cref="CleanupService"/> used to leave a trace only when it
    /// had deleted something. A stopped service and a quiet week looked exactly
    /// alike, both in the panel and in the database.
    /// <para>
    /// This object is to the cleanup service what <c>ConsumerRunning</c> and
    /// <c>LastActivityAt</c> are to the mail queue
    /// (<see cref="IBackgroundTaskQueue"/>): the difference between "no error"
    /// and "running". The ninth check in the Health tab reads it without going
    /// to the database.
    /// </para>
    /// Singleton, written by one background service and read by the health
    /// check — which is why the fields go through a lock instead of being plain
    /// auto-properties.
    /// </summary>
    public interface ICleanupStatus
    {
        /// <summary>Whether the loop is running right now.</summary>
        bool Running { get; }

        /// <summary>How often a cycle is expected. Published from here so that
        /// the panel does not carry a second copy of the number.</summary>
        TimeSpan Interval { get; }

        /// <summary>When a cycle last finished, successfully or not.</summary>
        DateTime? LastRunAt { get; }

        /// <summary>When a cycle last finished WITHOUT an exception.</summary>
        DateTime? LastSuccessAt { get; }

        /// <summary>The text of the last error, if one has happened since the
        /// last success.</summary>
        string? LastError { get; }

        /// <summary>When the last error happened.</summary>
        DateTime? LastErrorAt { get; }

        /// <summary>How many accounts and how many crypto orders the last cycle
        /// caught.</summary>
        int LastDeletedAccounts { get; }
        int LastExpiredOrders { get; }

        /// <summary>Totals since the process started. They reset on restart —
        /// the panel shows them next to the uptime for that reason.</summary>
        long TotalCycles { get; }
        long TotalFailures { get; }

        void MarkStarted(TimeSpan interval);
        void MarkStopped();
        void RecordSuccess(int deletedAccounts, int expiredOrders);
        void RecordFailure(Exception ex);
    }

    public sealed class CleanupStatus : ICleanupStatus
    {
        private readonly object _gate = new();

        private bool _running;
        private TimeSpan _interval = TimeSpan.FromHours(1);
        private DateTime? _lastRunAt;
        private DateTime? _lastSuccessAt;
        private string? _lastError;
        private DateTime? _lastErrorAt;
        private int _lastDeletedAccounts;
        private int _lastExpiredOrders;
        private long _totalCycles;
        private long _totalFailures;

        public bool Running                { get { lock (_gate) return _running; } }
        public TimeSpan Interval           { get { lock (_gate) return _interval; } }
        public DateTime? LastRunAt         { get { lock (_gate) return _lastRunAt; } }
        public DateTime? LastSuccessAt     { get { lock (_gate) return _lastSuccessAt; } }
        public string? LastError           { get { lock (_gate) return _lastError; } }
        public DateTime? LastErrorAt       { get { lock (_gate) return _lastErrorAt; } }
        public int LastDeletedAccounts     { get { lock (_gate) return _lastDeletedAccounts; } }
        public int LastExpiredOrders       { get { lock (_gate) return _lastExpiredOrders; } }
        public long TotalCycles            { get { lock (_gate) return _totalCycles; } }
        public long TotalFailures          { get { lock (_gate) return _totalFailures; } }

        public void MarkStarted(TimeSpan interval)
        {
            lock (_gate)
            {
                _running  = true;
                _interval = interval;
            }
        }

        public void MarkStopped()
        {
            lock (_gate) _running = false;
        }

        public void RecordSuccess(int deletedAccounts, int expiredOrders)
        {
            lock (_gate)
            {
                _lastRunAt           = DateTime.UtcNow;
                _lastSuccessAt       = _lastRunAt;
                _lastDeletedAccounts = deletedAccounts;
                _lastExpiredOrders   = expiredOrders;
                _lastError           = null;
                _totalCycles++;
            }
        }

        public void RecordFailure(Exception ex)
        {
            lock (_gate)
            {
                _lastRunAt   = DateTime.UtcNow;
                _lastErrorAt = _lastRunAt;
                _lastError   = $"{ex.GetType().Name}: {ex.Message}";
                _totalCycles++;
                _totalFailures++;
            }
        }
    }
}
