using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // Основният текст на банера/preferences модала (виж Pages/Shared/_DataNotice.cshtml) —
    // singleton ред (винаги Id = 1), редактируем от админ панела, точно като
    // PrivacyPolicyContent.
    public class CookieNoticeContent
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