// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Threading.Channels;

namespace ConferenceApp.Services
{
    /// <summary>
    /// Runs the work items from IBackgroundTaskQueue for as long as the
    /// application is up. Each item is wrapped in a try/catch — one failure (an
    /// unreachable SMTP server, say) must not bring the loop down and stop
    /// everything queued behind it.
    /// </summary>
    public class QueuedHostedService : BackgroundService
    {
        /// <summary>
        /// [E-01]: how long pending items get on shutdown. Deliberately below
        /// HostOptions.ShutdownTimeout (set in Program.cs) so that the other
        /// hosted services are left some room too.
        /// </summary>
        public static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(15);

        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly ILogger<QueuedHostedService> _logger;

        /// <summary>
        /// [E-01]: work items do NOT get the stoppingToken. They used to
        /// (`await workItem(stoppingToken)`), which meant the mail being sent at
        /// that very moment threw on a cancelled token at the first sign of
        /// shutdown — and disappeared into the catch without a line anywhere.
        /// <para>
        /// This token is cancelled only once the drain deadline passes, so work
        /// already under way has a chance to finish.
        /// </para>
        /// </summary>
        private readonly CancellationTokenSource _workCts = new();

        public QueuedHostedService(IBackgroundTaskQueue taskQueue, ILogger<QueuedHostedService> logger)
        {
            _taskQueue = taskQueue;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Background task queue started.");
            _taskQueue.ConsumerRunning = true;

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    Func<CancellationToken, Task> workItem;

                    try
                    {
                        workItem = await _taskQueue.DequeueAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        // A normal application shutdown, not an error.
                        break;
                    }
                    catch (ChannelClosedException)
                    {
                        // StopAsync closes the queue before it cancels the
                        // token. Without this catch the exception escapes
                        // ExecuteAsync and the host reports it as unhandled
                        // (BackgroundServiceExceptionBehavior.StopHost) — on
                        // every shutdown, in the log, at Critical level. Here it
                        // is a normal end, not an error: whatever is left is
                        // finished by DrainAsync.
                        break;
                    }

                    _taskQueue.LastActivityAt = DateTime.UtcNow;
                    await RunAsync(workItem, _workCts.Token);
                }
            }
            finally
            {
                _taskQueue.ConsumerRunning = false;
                _logger.LogInformation("Background task queue stopped.");
            }
        }

        /// <summary>
        /// Runs one work item and records its outcome. [E-02]: a failure is not
        /// only a line in the log — it goes into the queue state as well, which
        /// is where the Health tab shows "last failure" from.
        /// </summary>
        private async Task RunAsync(Func<CancellationToken, Task> workItem, CancellationToken ct)
        {
            try
            {
                await workItem(ct);
                _taskQueue.RecordSuccess();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while executing a background work item.");
                _taskQueue.RecordFailure($"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// [E-01]: pending items used to vanish on shutdown — the channel lives
        /// in process memory only and nothing was written down. For OTP that is
        /// expensive: the code is already in the database and already counts
        /// against the "three per 30 minutes" limit, while the user waits for a
        /// mail that will never arrive.
        /// <para>
        /// The queue is now first closed to new work, then whatever is left is
        /// finished with a separate token and a deadline. What does not make it
        /// is logged as an error with a count, instead of disappearing.
        /// </para>
        /// </summary>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            // No new work from here on; what is turned away is counted in
            // RejectedCount.
            _taskQueue.CompleteAdding();

            // From this point the work has DrainTimeout to finish — both what is
            // running and what is still queued.
            _workCts.CancelAfter(DrainTimeout);

            // Cancels stoppingToken and waits for ExecuteAsync to leave the
            // loop. The item currently running is no longer interrupted by that,
            // because it holds _workCts.Token instead.
            await base.StopAsync(cancellationToken);

            await DrainAsync(cancellationToken);

            _workCts.Cancel();
        }

        private async Task DrainAsync(CancellationToken hostToken)
        {
            var remaining = _taskQueue.PendingCount;
            if (remaining == 0) return;

            _logger.LogInformation("Източвам {Count} чакащи задачи преди спиране…", remaining);

            int sent = 0;
            int before = _taskQueue.FailedCount;

            while (!_workCts.IsCancellationRequested
                   && !hostToken.IsCancellationRequested
                   && _taskQueue.TryDequeue(out var workItem) && workItem != null)
            {
                await RunAsync(workItem, _workCts.Token);
                sent++;
            }

            var failed = _taskQueue.FailedCount - before;
            var lost   = _taskQueue.PendingCount;

            _logger.LogInformation(
                "Източване преди спиране: {Ok} изпратени, {Failed} провалени.", sent - failed, failed);

            if (lost > 0)
                _logger.LogError(
                    "Спирането отряза опашката: {Lost} задачи НЕ бяха изпратени и се губят. " +
                    "Ако сред тях е имало OTP код, потребителят го е изгорил, без да получи писмо.",
                    lost);

            if (_taskQueue.RejectedCount > 0)
                _logger.LogWarning(
                    "{Count} задачи бяха отказани, защото опашката вече беше затворена за спиране.",
                    _taskQueue.RejectedCount);
        }

        public override void Dispose()
        {
            _workCts.Dispose();
            base.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
