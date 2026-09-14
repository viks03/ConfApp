// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Threading.Channels;

namespace ConferenceApp.Services
{
    /// <summary>
    /// A queue for work that must not hold up the HTTP response.
    /// <para>
    /// It exists because sending the OTP mail used to happen synchronously
    /// inside the request (`await _emailSender.SendAsync`). EmailSender has a
    /// 15-second SMTP timeout, so the browser waited up to 15 seconds before it
    /// even got the redirect — and for nothing: the user is already stored and
    /// the OTP code already generated before the mail goes out.
    /// </para>
    /// <para>
    /// The work is now handed in here (immediately, without blocking) and run
    /// by QueuedHostedService in the background.
    /// </para>
    /// </summary>
    public interface IBackgroundTaskQueue
    {
        void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem);

        /// <summary>
        /// Waits for the next work item. Throws
        /// <see cref="System.Threading.Channels.ChannelClosedException"/> if the
        /// queue has been closed by <see cref="CompleteAdding"/> in the
        /// meantime — the consumer reads that as "end", not as an error.
        /// </summary>
        ValueTask<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);

        // ── Draining on shutdown ([E-01]) ─────────────────────────────────
        // The channel lives in process memory only. Pending items used to
        // simply vanish on shutdown: nothing was written down, nobody found
        // out. For OTP that is expensive — the code is already in the database
        // and already counts against the "three per 30 minutes" limit, while
        // the mail never goes out.

        /// <summary>
        /// Takes a work item if one is there, without waiting. Used by the
        /// shutdown drain, where the blocking <see cref="DequeueAsync"/> would
        /// hang on an empty queue until the shutdown deadline.
        /// </summary>
        bool TryDequeue(out Func<CancellationToken, Task>? workItem);

        /// <summary>
        /// Closes the queue to new work. After this
        /// <see cref="QueueBackgroundWorkItem"/> accepts nothing and counts what
        /// it turned away in <see cref="RejectedCount"/>.
        /// </summary>
        void CompleteAdding();

        /// <summary>How many items were turned away because the queue was
        /// already closed.</summary>
        int RejectedCount { get; }

        // ── State read by the health check ────────────────────────────────
        // Without these three an administrator has no way of telling whether
        // mail is piling up: the queue accepts work silently even when nobody
        // is consuming it, so "no error" does not mean "running".

        /// <summary>How many items are waiting right now.</summary>
        int PendingCount { get; }

        /// <summary>Whether the consumer (QueuedHostedService) is running.</summary>
        bool ConsumerRunning { get; set; }

        /// <summary>When an item was last taken off the queue.</summary>
        DateTime? LastActivityAt { get; set; }

        // ── Failures ([E-02]) ─────────────────────────────────────────────
        // A failed mail was logged and forgotten. The Health tab read only
        // PendingCount/ConsumerRunning/LastActivityAt, that is, it reported
        // "all fine" in exactly the case where nothing had gone out.

        /// <summary>How many items failed for good, after every retry.</summary>
        int FailedCount { get; }

        /// <summary>How many items went through successfully.</summary>
        int SucceededCount { get; }

        /// <summary>When the last final failure happened.</summary>
        DateTime? LastFailureAt { get; }

        /// <summary>What the last final failure was, for the line in the Health
        /// tab.</summary>
        string? LastFailureMessage { get; }

        void RecordSuccess();
        void RecordFailure(string message);
    }

    public class BackgroundTaskQueue : IBackgroundTaskQueue
    {
        private readonly Channel<Func<CancellationToken, Task>> _queue;

        public BackgroundTaskQueue()
        {
            // Unbounded: mail arrives as a rare, short burst (registrations and
            // sign-ins), and dropping an OTP mail is far worse than holding a
            // little memory.
            _queue = Channel.CreateUnbounded<Func<CancellationToken, Task>>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        }

        // This used to be `_queue.Reader.Count`, which threw
        // NotSupportedException: the channel is created with SingleReader =
        // true, so .NET picks the optimised SingleConsumerUnboundedChannel,
        // whose reader does NOT support Count (Reader.CanCount is false)
        // because its internal queue keeps no count.
        //
        // A counter of our own works with any implementation and is just as
        // accurate: incremented on write, decremented on read.
        private int _pending;
        private int _rejected;
        private int _failed;
        private int _succeeded;

        private readonly object _failureGate = new();
        private DateTime? _lastFailureAt;
        private string? _lastFailureMessage;

        public int PendingCount  => Volatile.Read(ref _pending);
        public int RejectedCount => Volatile.Read(ref _rejected);
        public int FailedCount   => Volatile.Read(ref _failed);
        public int SucceededCount => Volatile.Read(ref _succeeded);

        public DateTime? LastFailureAt      { get { lock (_failureGate) return _lastFailureAt; } }
        public string? LastFailureMessage   { get { lock (_failureGate) return _lastFailureMessage; } }

        public bool ConsumerRunning { get; set; }
        public DateTime? LastActivityAt { get; set; }

        public void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);
            // TryWrite on an unbounded channel is synchronous and effectively
            // instant — which is the whole point: the request no longer waits
            // for SMTP.
            if (_queue.Writer.TryWrite(workItem))
            {
                Interlocked.Increment(ref _pending);
            }
            else
            {
                // The only case today: the queue is closed because the
                // application is shutting down. That rejection used to be
                // completely silent.
                Interlocked.Increment(ref _rejected);
            }
        }

        public async ValueTask<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
        {
            var item = await _queue.Reader.ReadAsync(cancellationToken);
            Interlocked.Decrement(ref _pending);
            return item;
        }

        public bool TryDequeue(out Func<CancellationToken, Task>? workItem)
        {
            if (_queue.Reader.TryRead(out var item))
            {
                Interlocked.Decrement(ref _pending);
                workItem = item;
                return true;
            }

            workItem = null;
            return false;
        }

        public void CompleteAdding() => _queue.Writer.TryComplete();

        public void RecordSuccess() => Interlocked.Increment(ref _succeeded);

        public void RecordFailure(string message)
        {
            Interlocked.Increment(ref _failed);

            lock (_failureGate)
            {
                _lastFailureAt = DateTime.UtcNow;
                _lastFailureMessage = message;
            }
        }
    }
}
