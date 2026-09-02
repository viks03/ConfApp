using System.Threading.Channels;

namespace ConferenceApp.Services
{
    /// <summary>
    /// Опашка за работа, която не бива да бави HTTP отговора.
    /// <para>
    /// БЪГ ФИКС (лаг при регистрация/вход): изпращането на OTP имейла се
    /// извикваше синхронно вътре в request-а (`await _emailSender.SendAsync`).
    /// EmailSender има 15-секунден SMTP timeout, така че браузърът чакаше до
    /// 15 секунди, преди изобщо да получи редиректа — при това напълно
    /// излишно: потребителят вече е записан в базата и OTP кодът вече е
    /// генериран, преди имейлът да тръгне.
    /// </para>
    /// <para>
    /// Сега работата се пуска тук (мигновено, без блокиране) и се изпълнява
    /// от QueuedHostedService във фонов режим.
    /// </para>
    /// </summary>
    public interface IBackgroundTaskQueue
    {
        void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem);
        ValueTask<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);

        // ── Състояние, четено от Health Check ─────────────────────────────
        // Без тези три администраторът няма как да разбере дали писмата
        // засядат: опашката приема работа мълчаливо дори когато никой не я
        // консумира, така че „няма грешка“ не значи „работи“.

        /// <summary>Колко задачи чакат в момента.</summary>
        int PendingCount { get; }

        /// <summary>Върти ли се консуматорът (QueuedHostedService).</summary>
        bool ConsumerRunning { get; set; }

        /// <summary>Кога за последно е взета задача от опашката.</summary>
        DateTime? LastActivityAt { get; set; }
    }

    public class BackgroundTaskQueue : IBackgroundTaskQueue
    {
        private readonly Channel<Func<CancellationToken, Task>> _queue;

        public BackgroundTaskQueue()
        {
            // Unbounded: имейлите са рядък, кратък burst (регистрации/логини),
            // а изпускането на OTP имейл е далеч по-лошо от малко памет.
            _queue = Channel.CreateUnbounded<Func<CancellationToken, Task>>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        }

        // БЪГ ФИКС: тук стоеше `_queue.Reader.Count`, което хвърляше
        // NotSupportedException. Причината: каналът е създаден със
        // SingleReader = true, при което .NET избира оптимизираната
        // SingleConsumerUnboundedChannel — нейният reader НЕ поддържа Count
        // (Reader.CanCount е false), защото вътрешната опашка не пази брой.
        //
        // Собствен брояч работи при всяка реализация и е също толкова точен:
        // увеличава се при подаване, намалява при взимане.
        private int _pending;

        public int PendingCount => Volatile.Read(ref _pending);

        public bool ConsumerRunning { get; set; }
        public DateTime? LastActivityAt { get; set; }

        public void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);
            // TryWrite при unbounded channel е синхронен и практически мигновен —
            // точно затова request-ът вече не чака SMTP.
            if (_queue.Writer.TryWrite(workItem))
                Interlocked.Increment(ref _pending);
        }

        public async ValueTask<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
        {
            var item = await _queue.Reader.ReadAsync(cancellationToken);
            Interlocked.Decrement(ref _pending);
            return item;
        }
    }
}
