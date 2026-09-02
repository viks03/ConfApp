using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // Съдържанието на страницата Terms of Use — singleton ред (винаги
    // Id = 1), редактируем от админ панела, вместо да е закодиран в resx
    // (compile-time) файлове. Заменя старите Pages.Terms_Sec1..9 в
    // Pages.Terms.en/bg.resx. Огледален модел на PrivacyPolicyContent.
    public class TermsOfUseContent
    {
        [Key]
        public int Id { get; set; }

        // Пълен HTML на условията (от Quill rich-text редактора), на английски.
        [Required]
        public string ContentEn { get; set; } = string.Empty;

        // Същото, на български.
        [Required]
        public string ContentBg { get; set; } = string.Empty;

        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
