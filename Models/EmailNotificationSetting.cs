namespace ConferenceApp.Models
{
    /// <summary>
    /// Включване и изключване на отделните видове известия по имейл.
    /// <para>
    /// Ред на вид имейл, а не колона на вид имейл. Причината: добавянето на
    /// нов имейл в бъдеще не иска миграция — редът се създава автоматично при
    /// първото обръщение, включен по подразбиране.
    /// </para>
    /// <para>
    /// Кодът с OTP УМИШЛЕНО няма запис тук. Без него никой не може да се
    /// регистрира и да влезе, така че изключването му би заключило сайта.
    /// </para>
    /// </summary>
    public class EmailNotificationSetting
    {
        public int Id { get; set; }

        /// <summary>
        /// Съответства на име от <c>EmailTemplate</c> enum-а
        /// (например "PaymentConfirmed").
        /// </summary>
        public string TemplateKey { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;

        /// <summary>Кой и кога го е променил последно — за проследимост.</summary>
        public DateTime? LastChangedAt { get; set; }
        public string? LastChangedBy { get; set; }
    }
}
