// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Services.Email
{
    /// <summary>
    /// The retry rule for sending a mail, in one place.
    /// <para>
    /// [E-02] introduced it inside <see cref="MailComposer"/>, back when the
    /// composer was the only caller that went through the background queue.
    /// Bug-report notifications now go through it too, and two copies of
    /// "three attempts, 3 and 15 seconds apart" would drift apart at the first
    /// change.
    /// </para>
    /// </summary>
    public static class MailRetry
    {
        /// <summary>
        /// The pauses BETWEEN attempts. The first is short, for a blip in the
        /// network; the second gives a temporarily refusing server time to
        /// recover.
        /// </summary>
        public static readonly TimeSpan[] Delays =
        [
            TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(15)
        ];

        /// <summary>Total attempts, the first one included.</summary>
        public const int MaxAttempts = 3;   // Delays.Length + 1

        /// <summary>
        /// Runs the send with retries. A final failure is rethrown:
        /// <see cref="QueuedHostedService"/> logs it and records it in the
        /// queue status, which is where the Health tab reads "last failure"
        /// from.
        /// </summary>
        /// <param name="send">The send itself — renders and sends.</param>
        /// <param name="what">What is being sent, for the log line.</param>
        /// <param name="toEmail">Who it goes to, for the log line.</param>
        public static async Task RunAsync(
            Func<CancellationToken, Task> send,
            string what,
            string toEmail,
            ILogger logger,
            CancellationToken ct)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await send(ct);

                    if (attempt > 1)
                        logger.LogInformation(
                            "{What} към {Email} мина на опит {Attempt} от {Max}.",
                            what, toEmail, attempt, MaxAttempts);

                    return;
                }
                catch (Exception ex) when (attempt < MaxAttempts
                                           && IsWorthRetrying(ex)
                                           && !ct.IsCancellationRequested)
                {
                    var wait = Delays[attempt - 1];

                    logger.LogWarning(ex,
                        "Опит {Attempt} от {Max} за {What} към {Email} се провали. Повтарям след {Seconds}s.",
                        attempt, MaxAttempts, what, toEmail, wait.TotalSeconds);

                    await Task.Delay(wait, ct);
                }
            }
        }

        /// <summary>
        /// Tells "SMTP did not answer" apart from "the address is invalid".
        /// The second will not work on the tenth attempt either, and three
        /// attempts 15 seconds apart would block the single consumer for
        /// nothing.
        /// </summary>
        public static bool IsWorthRetrying(Exception ex) => ex switch
        {
            ArgumentException                           => false,  // validation in EmailSender
            System.Net.Mail.SmtpFailedRecipientException => false,  // the address does not exist
            OperationCanceledException                  => false,  // the application is shutting down
            _                                           => true
        };
    }
}
