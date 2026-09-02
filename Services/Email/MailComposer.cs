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
        //  Публични методи — по един на вид имейл
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

                // Причината идва от свободно текстово поле, което админът пише.
                // .Set() я екранира — иначе "<" в текста би счупил HTML-а, а
                // умишлен <script> би бил инжекция право в пощата на клиента.
                var reasonText = string.IsNullOrWhiteSpace(reason)
                    ? t["Email_VerifRejected_NoReason"].Value
                    : reason;

                var ph = Common(t, baseUrl, subject, t["Email_VerifRejected_Preheader"])
                    .Set("Greeting",    string.Format(t["Email_Common_Greeting"], firstName))
                    .SetRaw("MainText", t["Email_VerifRejected_MainText"])
                    .Set("StatusLabel", t["Email_VerifRejected_Status"])
                    .Set("ReasonLabel", t["Email_VerifRejected_ReasonLabel"])
                    // SetMultiline, не Set: причината идва от textarea и може да е на
                    // няколко реда. Виж коментара в EmailPlaceholders — Gmail маха
                    // новите редове и слепва думите. Екранирането се запазва.
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
        //  Вътрешни
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Плейсхолдърите, които всяка рамка очаква.</summary>
        private EmailPlaceholders Common(IStringLocalizer t, string baseUrl, string subject,
                                         string preheader)
            => new EmailPlaceholders()
                .Set("EmailSubject",  subject)
                // Скритият ред, който клиентът показва до темата в списъка и в
                // известието на телефона. Тук стоеше самата тема и тя излизаше
                // два пъти една след друга. Затова е отделен текст, който
                // допълва темата, вместо да я повтаря.
                .Set("Preheader",     preheader)
                .Set("FooterRights",  t["Email_Common_FooterRights"])
                .SetRaw("BaseUrl",    baseUrl.TrimEnd('/'));   // URL, не се екранира

        private static string FormatDate(CultureInfo culture)
            => DateTime.Now.ToString("dd.MM.yyyy, HH:mm", culture);

        /// <summary>
        /// Общият път: чете преводите за подадената култура ТУК (в контекста на
        /// заявката), рендира HTML-а, и чак тогава слага изпращането в опашката.
        /// Задачата във фона вече не зависи от култура, resx или HTTP контекст.
        /// </summary>
        private async Task ComposeAndQueueAsync(
            EmailTemplate template,
            string toEmail,
            CultureInfo culture,
            Func<IStringLocalizer, (string Subject, EmailPlaceholders Ph)> build)
        {
            // Проверката е ТУК, а не на дванайсетте места, които пращат имейл.
            // Така изключването важи навсякъде наведнъж и нито един извикващ
            // не може да я пропусне по невнимание.
            // OTP не се проверява — IsEnabledAsync винаги връща true за него.
            if (!await _settings.IsEnabledAsync(template))
            {
                _logger.LogInformation(
                    "Известието {Template} е изключено от админ панела. Пропускам {Email}.",
                    template, toEmail);
                return;
            }

            string subject;
            EmailPlaceholders ph;

            // IStringLocalizer чете CurrentUICulture. Сменяме я временно, за да
            // получим преводите на езика на ПОЛУЧАТЕЛЯ, а не на този, който е
            // предизвикал действието (важно при админските действия).
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

            _queue.QueueBackgroundWorkItem(async ct =>
            {
                try
                {
                    var html = await _renderer.RenderAsync(template, ph, ct);
                    await _sender.SendAsync(toEmail, subject, html);
                }
                catch (Exception ex)
                {
                    // Провален имейл никога не бива да събаря нищо друго —
                    // записът в базата вече е направен преди това повикване.
                    _logger.LogError(ex,
                        "Неуспешно изпращане на {Template} към {Email}.", template, toEmail);
                }
            });
        }
    }
}
