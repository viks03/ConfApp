using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // Основният, дългият текст на /Cookies страницата (какво са бисквитките,
    // как ги ползваме, трети страни, управление през браузъра, промени в
    // политиката, контакт) — singleton ред, редактируем от админ панела с
    // Quill, точно като PrivacyPolicyContent. НЕ е същото като
    // CookieNoticeContent (това е кратък банер текст) — тук е пълния текст
    // на самата страница, около динамичния списък с категории.
    public class CookiePolicyContent
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string ContentEn { get; set; } = string.Empty;
        [Required]
        public string ContentBg { get; set; } = string.Empty;

        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
