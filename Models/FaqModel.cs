// Models/FaqModel.cs
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    public class FaqModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string QuestionEn { get; set; } = string.Empty;

        [Required]
        public string QuestionBg { get; set; } = string.Empty;

        [Required]
        public string AnswerEn { get; set; } = string.Empty;

        [Required]
        public string AnswerBg { get; set; } = string.Empty;

        // Поле за контрол на подредбата на въпросите
        public int DisplayOrder { get; set; }
        
        // Поле за временно скриване на въпрос без изтриване
        public bool IsActive { get; set; } = true;
    }
}