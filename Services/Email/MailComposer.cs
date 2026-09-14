// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Localization;

namespace ConferenceApp.Services.Email
{
    public sealed class MailComposer : IMailComposer
    {
        private readonly IEmailTemplateRenderer _renderer;
        private readonly EmailSender _sender;
        private readonly IBackgroundTaskQueue _queue;
        private readonly IStringLocalizer _t;          // EmailMessages resx
        private readonly IEmailNotificationSettings _settings;
        private readonly ILogger<MailComposer> _logger;

        public MailComposer(
            IEmailTemplateRenderer renderer,
            EmailSender sender,
            IBackgroundTaskQueue queue,
            IStringLocalizerFactory localizerFactory,
            IEmailNotificationSettings settings,
            ILogger<MailComposer> logger)
        {
            _renderer = renderer;
            _sender   = sender;
            _queue    = queue;
            _settings = settings;
            _logger   = logger;
            _t = localizerFactory.Create("EmailMessages",
                     Assembly.GetExecutingAssembly().GetName().Name!);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Public methods — one per kind of mail
        // ─────────────────────────────────────────────────────────────────────

        public Task SendOtpAsync(string toEmail, string firstName, string code,
                                 OtpPurpose purpose, CultureInfo culture, string baseUrl)
        {
            var p = purpose == OtpPurpose.Login ? "Login" : "Registration";

            return ComposeAndQueueAsync(EmailTemplate.Otp, toEmail, culture, t =>
            {
                var subject = t[$"Email_Otp_{p}_Subject"];
                var ph = Common(t, baseUrl, subject, t[$"Email_Otp_{p}_Preheader"])
                    .Set("Greeting",    string.Format(t["Email_Common_Greeting"], firstName))
                    .SetRaw("MainText", t[$"Email_Otp_{p}_MainText"])
                    .Set("CodeLabel",   t["Email_Otp_CodeLabel"])
                    .Set("OtpCode",     code)
                    .Set("WarningText", t["Email_Otp_Warning"]);
                return (subject, ph);
            });
        }

        public Task SendPaymentConfirmedAsync(string toEmail, string firstName,
                                              string amount, string method, string reference,
                                              CultureInfo culture, string baseUrl)
            => ComposeAndQueueAsync(EmailTemplate.PaymentConfirmed, toEmail, culture, t =>
            {
                var subject = t["Email_PayConfirmed_Subject"];
                var ph = Common(t, baseUrl, subject, t["Email_PayConfirmed_Preheader"])
                    .Set("Greeting",       string.Format(t["Email_Common_Greeting"], firstName))
                    .SetRaw("MainText",    t["Email_PayConfirmed_MainText"])
                    .Set("StatusLabel",    t["Email_PayConfirmed_Status"])
                    .Set("AmountLabel",    t["Email_Common_AmountLabel"])
                    .Set("AmountValue",    amount)
                    .Set("MethodLabel",    t["Email_Common_MethodLabel"])
                    .Set("MethodValue",    method)
                    .Set("ReferenceLabel", t["Email_Common_ReferenceLabel"])
                    .Set("ReferenceValue", reference)
                    .Set("DateLabel",      t["Email_Common_DateLabel"])
                    .Set("DateValue",      FormatDate(culture))
                    .Set("ButtonLabel",    t["Email_Common_ButtonProfile"]);
                return (subject, ph);
            });

        public Task SendPaymentPendingAsync(string toEmail, string firstName,
                                            string amount, string method, string reference,
                                            CultureInfo culture, string baseUrl)
            => ComposeAndQueueAsync(EmailTemplate.PaymentPending, toEmail, culture, t =>
            {
                var subject = t["Email_PayPending_Subject"];
                var ph = Common(t, baseUrl, subject, t["Email_PayPending_Preheader"])
                    .Set("Greeting",       string.Format(t["Email_Common_Greeting"], firstName))
                    .SetRaw("MainText",    t["Email_PayPending_MainText"])
                    .Set("StatusLabel",    t["Email_PayPending_Status"])
                    .Set("AmountLabel",    t["Email_Common_AmountLabel"])
                    .Set("AmountValue",    amount)
                    .Set("MethodLabel",    t["Email_Common_MethodLabel"])
                    .Set("MethodValue",    method)
                    .Set("ReferenceLabel", t["Email_Common_ReferenceLabel"])
                    .Set("ReferenceValue", reference)
                    .Set("DateLabel",      t["Email_Common_DateLabel"])
                    .Set("DateValue",      FormatDate(culture))
                    .Set("NoticeLabel",    t["Email_PayPending_NoticeLabel"])
                    .SetRaw("NoticeText",  t["Email_PayPending_NoticeText"])
                    .Set("ButtonLabel",    t["Email_Common_ButtonPayment"]);
                return (subject, ph);
            });

        public Task SendVerificationApprovedAsync(string toEmail, string firstName,
                                                  string participationType,
                                                  CultureInfo culture, string baseUrl)
            => ComposeAndQueueAsync(EmailTemplate.VerificationApproved, toEmail, culture, t =>
            {
                var subject = t["Email_VerifApproved_Subject"];
                var ph = Common(t, baseUrl, subject, t["Email_VerifApproved_Preheader"])
                    .Set("Greeting",    string.Format(t["Email_Common_Greeting"], firstName))
                    .SetRaw("MainText", t["Email_VerifApproved_MainText"])
                    .Set("StatusLabel", t["Email_VerifApproved_Status"])
                    .Set("TypeLabel",   t["Email_Common_TypeLabel"])
                    .Set("TypeValue",   participationType)
                    .Set("DateLabel",   t["Email_Common_DateLabel"])
                    .Set("DateValue",   FormatDate(culture))
                    .Set("ButtonLabel", t["Email_Common_ButtonProfile"]);
                return (subject, ph);
            });

        public Task SendVerificationRejectedAsync(string toEmail, string firstName,
                                                   string participationType, string? reason,
                                                   CultureInfo culture, string baseUrl)
            => ComposeAndQueueAsync(EmailTemplate.VerificationRejected, toEmail, culture, t =>
            {
                var subject = t["Email_VerifRejected_Subject"];

                // The reason comes from a free-text field the administrator
                // fills in, so it reaches the mail through SetMultiline below,
                // which escapes it: otherwise a "<" in the text would break the
                // HTML and a deliberate <script> would be an injection straight
                // into the recipient's inbox.
                var reasonText = string.IsNullOrWhiteSpace(reason)
                    ? t["Email_VerifRejected_NoReason"].Value
                    : reason;

                var ph = Common(t, baseUrl, subject, t["Email_VerifRejected_Preheader"])
                    .Set("Greeting",    string.Format(t["Email_Common_Greeting"], firstName))
                    .SetRaw("MainText", t["Email_VerifRejected_MainText"])
                    .Set("StatusLabel", t["Email_VerifRejected_Status"])
                    .Set("ReasonLabel", t["Email_VerifRejected_ReasonLabel"])
                    // SetMultiline rather than Set: the reason comes from a
                    // textarea and may span several lines. See the comment in
                    // EmailPlaceholders — Gmail strips the newlines and runs the
                    // words together. The escaping is kept either way.
                    .SetMultiline("ReasonText", reasonText)
                    .Set("TypeLabel",   t["Email_Common_TypeLabel"])
                    .Set("TypeValue",   participationType)
                    .Set("DateLabel",   t["Email_Common_DateLabel"])
                    .Set("DateValue",   FormatDate(culture))
                    .Set("ButtonLabel", t["Email_Common_ButtonDocuments"]);
                return (subject, ph);
            });

        public Task SendStatusChangedAsync(string toEmail, string firstName,
                                           string statusFrom, string statusTo,
                                           CultureInfo culture, string baseUrl)
            => ComposeAndQueueAsync(EmailTemplate.StatusChanged, toEmail, culture, t =>
            {
                var subject = t["Email_StatusChanged_Subject"];
                var ph = Common(t, baseUrl, subject, t["Email_StatusChanged_Preheader"])
                    .Set("Greeting",        string.Format(t["Email_Common_Greeting"], firstName))
                    .SetRaw("MainText",     t["Email_StatusChanged_MainText"])
                    .Set("StatusLabel",     t["Email_StatusChanged_Status"])
                    .Set("StatusFromLabel", t["Email_StatusChanged_FromLabel"])
                    .Set("StatusFromValue", statusFrom)
                    .Set("StatusToLabel",   t["Email_StatusChanged_ToLabel"])
                    .Set("StatusToValue",   statusTo)
                    .Set("DateLabel",       t["Email_Common_DateLabel"])
                    .Set("DateValue",       FormatDate(culture))
                    .Set("ButtonLabel",     t["Email_Common_ButtonProfile"]);
                return (subject, ph);
            });

        // ─────────────────────────────────────────────────────────────────────
        //  Internals
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>The placeholders every frame expects.</summary>
        private EmailPlaceholders Common(IStringLocalizer t, string baseUrl, string subject,
                                         string preheader)
            => new EmailPlaceholders()
                .Set("EmailSubject",  subject)
                // The hidden line a mail client shows next to the subject in the
                // list and in a phone notification. The subject itself used to go
                // here and appeared twice in a row, so this is a separate text
                // that complements the subject instead of repeating it.
                .Set("Preheader",     preheader)
                .Set("FooterRights",  t["Email_Common_FooterRights"])
                .SetRaw("BaseUrl",    baseUrl.TrimEnd('/'));   // a URL; escaping it would break the links

        private static string FormatDate(CultureInfo culture)
            => DateTime.Now.ToString("dd.MM.yyyy, HH:mm", culture);

        /// <summary>
        /// The shared path: reads the translations for the given culture HERE
        /// (inside the request context), renders the HTML, and only then puts
        /// the send on the queue. The background task then depends on neither
        /// culture, resx nor HTTP context.
        /// </summary>
        private async Task ComposeAndQueueAsync(
            EmailTemplate template,
            string toEmail,
            CultureInfo culture,
            Func<IStringLocalizer, (string Subject, EmailPlaceholders Ph)> build)
        {
            // The check lives HERE rather than at the twelve call sites that
            // send mail, so that switching a notification off takes effect
            // everywhere at once and no caller can forget it.
            // OTP is never gated — IsEnabledAsync always returns true for it.
            if (!await _settings.IsEnabledAsync(template))
            {
                _logger.LogInformation(
                    "Известието {Template} е изключено от админ панела. Пропускам {Email}.",
                    template, toEmail);
                return;
            }

            string subject;
            EmailPlaceholders ph;

            // IStringLocalizer reads CurrentUICulture. It is swapped for the
            // duration of the build so the translations come out in the
            // RECIPIENT's language rather than that of whoever triggered the
            // action — which is what makes the admin-initiated mails correct.
            var prevUi = CultureInfo.CurrentUICulture;
            var prev   = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.CurrentCulture   = culture;
                (subject, ph) = build(_t);
            }
            finally
            {
                CultureInfo.CurrentUICulture = prevUi;
                CultureInfo.CurrentCulture   = prev;
            }

            _queue.QueueBackgroundWorkItem(ct => SendWithRetriesAsync(template, toEmail, subject, ph, ct));
        }

        /// <summary>
        /// [E-02]: the body of this task used to be a single attempt in a
        /// try/catch — with SMTP unreachable the mail failed once, wrote a line
        /// in the log and vanished. No retry, no record anywhere, and the panel
        /// showed "confirmed" while the person's inbox stayed empty. For OTP the
        /// same, except that the user burns one of their three attempts per 30
        /// minutes on every failure.
        /// <para>
        /// The rule itself — how many attempts, with what pauses, which errors
        /// are worth repeating — lives in <see cref="MailRetry"/>: bug-report
        /// notifications go through the same queue and must behave the same way.
        /// </para>
        /// </summary>
        private Task SendWithRetriesAsync(
            EmailTemplate template, string toEmail, string subject,
            EmailPlaceholders ph, CancellationToken ct)
            => MailRetry.RunAsync(
                async token =>
                {
                    var html = await _renderer.RenderAsync(template, ph, token);
                    await _sender.SendAsync(toEmail, subject, html);
                },
                template.ToString(), toEmail, _logger, ct);
    }
}
