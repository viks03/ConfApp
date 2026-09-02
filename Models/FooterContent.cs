using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // Текстовото съдържание на footer-а: tagline-ът под логото, "Organized
    // by..." бележката в Contact колоната, и самите контактни данни.
    // Singleton ред (винаги Id = 1) — seed-нат с разумни default стойности
    // в FooterContentConfiguration, редактируем от админ панела (Site
    // Settings → Footer Content), точно като SocialLinksSetting.
    public class FooterContent
    {
        [Key]
        public int Id { get; set; }

        // Опционален нарочно — ако е празен, tagline редът просто не се
        // рендира на публичния footer (виж _Layout.cshtml), а логото до
        // заглавието автоматично се смалява да пасне на еднородов текст
        // вместо двуредов (виж .footer-brand-logo в mainStyle.css —
        // align-items: stretch + object-fit: contain, никаква отделна
        // логика тук не е нужна за това поведение).
        // MaxLength(45): рендира се с white-space: nowrap до логото
        // (виж .footer-brand-title/.footer-brand-tagline) — прекалено
        // дълъг текст би преляло извън картата вместо да пренесе ред.
        [MaxLength(45)]
        public string BrandTaglineEn { get; set; } = string.Empty;
        [MaxLength(45)]
        public string BrandTaglineBg { get; set; } = string.Empty;

        [Required, MaxLength(400)]
        public string OrgNoteEn { get; set; } = string.Empty;
        [Required, MaxLength(400)]
        public string OrgNoteBg { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string ContactLocationEn { get; set; } = string.Empty;
        [Required, MaxLength(100)]
        public string ContactLocationBg { get; set; } = string.Empty;

        [Required, MaxLength(150), EmailAddress]
        public string ContactEmail { get; set; } = string.Empty;
        [Required, MaxLength(30)]
        public string ContactPhone { get; set; } = string.Empty;

        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    }
}