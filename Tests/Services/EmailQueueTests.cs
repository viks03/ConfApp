// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Services;
using Microsoft.Extensions.Logging;

namespace ConferenceApp.Tests.Services;

/// <summary>
/// Part 10: "the mail queue survives a restart".
/// <para>
/// The channel lives only in the process's memory: on shutdown there is nowhere
/// to save it and nothing to read back at the next start. [E-01] therefore
/// promises no literal survival but something narrower and checkable:
/// <b>whatever was accepted is executed before the process goes away</b>, and
/// whatever does not make it is logged with a count instead of vanishing
/// silently.
/// </para>
/// <para>
/// The tests here go through the service's real lifecycle (<c>StartAsync</c> and
/// <c>StopAsync</c>) — exactly the two methods the host calls on startup and
/// shutdown. The same thing through a real process and a real SIGTERM is in
/// <see cref="QueueShutdownTests"/>.
/// </para>
/// </summary>
public class EmailQueueTests
{
    private static (BackgroundTaskQueue Queue, QueuedHostedService Service, CapturingLogger<QueuedHostedService> Log) Build()
    {
        var queue = new BackgroundTaskQueue();
        var log   = new CapturingLogger<QueuedHostedService>();
        return (queue, new QueuedHostedService(queue, log), log);
    }

    // ════════════════════════════════════════════════════════════════════
    // The ordinary course of things
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Приетата_задача_се_изпълнява()
    {
        var (queue, service, _) = Build();
        var done = new TaskCompletionSource();

        await service.StartAsync(CancellationToken.None);

        queue.QueueBackgroundWorkItem(_ => { done.SetResult(); return Task.CompletedTask; });

        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The flag is raised inside the loop itself, so it is read after the loop
        // has picked up a task rather than right after StartAsync.
        Assert.True(queue.ConsumerRunning);

        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, queue.SucceededCount);
        Assert.Equal(0, queue.PendingCount);
        Assert.Equal(0, queue.FailedCount);
        Assert.NotNull(queue.LastActivityAt);
        Assert.False(queue.ConsumerRunning);

        service.Dispose();
    }

    /// <summary>
    /// [E-02] A failed message used to be logged and then vanish. The Health tab
    /// read only whether the queue was moving, so it said "all is well" precisely
    /// in the case where nothing had gone out.
    /// </summary>
    [Fact]
    public async Task Провалена_задача_не_спира_следващите_и_оставя_следа()
    {
        var (queue, service, log) = Build();
        var second = new TaskCompletionSource();

        await service.StartAsync(CancellationToken.None);

        queue.QueueBackgroundWorkItem(_ => throw new InvalidOperationException("SMTP отказа"));
        queue.QueueBackgroundWorkItem(_ => { second.SetResult(); return Task.CompletedTask; });

        await second.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, queue.FailedCount);
        Assert.Equal(1, queue.SucceededCount);
        Assert.NotNull(queue.LastFailureAt);
        Assert.Contains("InvalidOperationException", queue.LastFailureMessage!);
        Assert.Contains("SMTP отказа", queue.LastFailureMessage!);
        Assert.True(log.Has(LogLevel.Error, "Error while executing a background work item"));

        service.Dispose();
    }

    // ════════════════════════════════════════════════════════════════════
    // Shutdown ([E-01])
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For a one-time code a lost task is expensive: the code is already in the
    /// database and already counts against the limit of three per 30 minutes, while
    /// the user waits for a message that will never come.
    /// </summary>
    [Fact]
    public async Task Чакащите_задачи_се_доизпълняват_при_спиране()
    {
        var (queue, service, _) = Build();
        var executed = 0;

        await service.StartAsync(CancellationToken.None);

        for (int i = 0; i < 8; i++)
            queue.QueueBackgroundWorkItem(async _ =>
            {
                await Task.Delay(120);
                Interlocked.Increment(ref executed);
            });

        // No waiting: at this very moment some of the tasks have not started yet.
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(8, executed);
        Assert.Equal(0, queue.PendingCount);
        Assert.Equal(8, queue.SucceededCount);

        service.Dispose();
    }

    /// <summary>
    /// [E-01] The tasks do NOT receive <c>stoppingToken</c>. They used to, so the
    /// message being sent at that very moment threw on a cancelled token at the
    /// first sign of shutdown and disappeared into the catch.
    /// </summary>
    [Fact]
    public async Task Започнатото_писмо_не_се_отменя_от_спирането()
    {
        var (queue, service, _) = Build();

        var started   = new TaskCompletionSource();
        var cancelled = false;
        var finished  = false;

        await service.StartAsync(CancellationToken.None);

        queue.QueueBackgroundWorkItem(async ct =>
        {
            started.SetResult();

            try
            {
                // About as long as a slow SMTP answer takes.
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                finished = true;
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                throw;
            }
        });

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await service.StopAsync(CancellationToken.None);

        Assert.False(cancelled, "Спирането отмени писмо, което вече се изпраща.");
        Assert.True(finished);
        Assert.Equal(1, queue.SucceededCount);

        service.Dispose();
    }

    [Fact]
    public async Task Задача_подадена_след_затварянето_се_отказва_и_се_брои()
    {
        var (queue, service, _) = Build();
        var ran = false;

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        queue.QueueBackgroundWorkItem(_ => { ran = true; return Task.CompletedTask; });

        Assert.Equal(1, queue.RejectedCount);
        Assert.Equal(0, queue.PendingCount);
        Assert.False(ran);

        service.Dispose();
    }

    /// <summary>
    /// If the drain timeout is not enough, the rest is lost — but it is written
    /// down. The test takes as long as the timeout itself
    /// (<see cref="QueuedHostedService.DrainTimeout"/>), because there is no other
    /// way to reach the end of it.
    /// </summary>
    [Fact]
    public async Task Отрязаната_опашка_влиза_в_лога_като_грешка()
    {
        var (queue, service, log) = Build();

        var started = new TaskCompletionSource();
        var secondRan = false;

        await service.StartAsync(CancellationToken.None);

        queue.QueueBackgroundWorkItem(async ct =>
        {
            started.SetResult();
            await Task.Delay(QueuedHostedService.DrainTimeout + TimeSpan.FromSeconds(10), ct);
        });

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        queue.QueueBackgroundWorkItem(_ => { secondRan = true; return Task.CompletedTask; });

        await service.StopAsync(CancellationToken.None);

        Assert.False(secondRan, "Задачата се изпълни, въпреки че срокът за източване изтече.");
        Assert.True(queue.FailedCount >= 1);
        Assert.True(log.Has(LogLevel.Error, "Спирането отряза опашката"),
            "Изгубените задачи не са обявени:\n" + log.Text);
        Assert.Contains("1 задачи НЕ бяха изпратени", log.Text);

        service.Dispose();
    }

    /// <summary>
    /// The drain timeout stays below <c>HostOptions.ShutdownTimeout</c>; otherwise
    /// the host cuts the drain off before it finishes on its own, leaving exactly
    /// the cases [E-01] is trying to catch.
    /// </summary>
    [Fact]
    public void Срокът_за_източване_стои_под_срока_за_спиране()
    {
        // The 45 seconds are set in Program.cs.
        Assert.True(QueuedHostedService.DrainTimeout < TimeSpan.FromSeconds(45),
            $"DrainTimeout е {QueuedHostedService.DrainTimeout.TotalSeconds} s.");
    }
}
