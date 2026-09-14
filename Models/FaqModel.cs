// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
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

        // Set by drag-and-drop in the admin panel.
        public int DisplayOrder { get; set; }
        
        // Hides a question from the public page without deleting it.
        public bool IsActive { get; set; } = true;
    }
}