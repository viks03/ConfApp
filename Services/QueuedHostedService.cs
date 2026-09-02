namespace ConferenceApp.Services
{
    /// <summary>
    /// Изпълнява задачите от IBackgroundTaskQueue, докато приложението работи.
    /// Всяка задача е обвита в try/catch — една провалена (напр. недостъпен
    /// SMTP сървър) не бива да събаря целия loop и да спира всички следващи.
    /// </summary>
    public class QueuedHostedService : BackgroundService
    {
        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly ILogger<QueuedHostedService> _logger;

        public QueuedHostedService(IBackgroundTaskQueue taskQueue, ILogger<QueuedHostedService> logger)
        {
            _taskQueue = taskQueue;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Background task queue started.");
            _taskQueue.ConsumerRunning = true;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var workItem = await _taskQueue.DequeueAsync(stoppingToken);
                    _taskQueue.LastActivityAt = DateTime.UtcNow;
                    await workItem(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Нормално спиране на приложението — не е грешка.
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while executing a background work item.");
                }
            }

            _taskQueue.ConsumerRunning = false;
            _logger.LogInformation("Background task queue stopped.");
        }
    }
}
