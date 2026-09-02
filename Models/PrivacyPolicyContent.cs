using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // Съдържанието на страницата Privacy Policy / GDPR — singleton ред (винаги
    // Id = 1), редактируем от админ панела, вместо да е закодиран в resx
    // (compile-time) файлове. Заменя старите Pages.Privacy.en/bg.resx.
    public class PrivacyPolicyContent
    {
        [Key]
        public int Id { get; set; }

        // Пълен HTML на политиката (от Quill rich-text редактора), на английски.
        [Required]
        public string ContentEn { get; set; } = string.Empty;

        // Същото, на български.
        [Required]
        public string ContentBg { get; set; } = string.Empty;

        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    }
}