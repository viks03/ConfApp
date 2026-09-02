using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    public class EventModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string TitleEn { get; set; } = string.Empty;

        [Required]
        public string TitleBg { get; set; } = string.Empty;

        public string LocationEn { get; set; } = string.Empty; // напр. "Sofia - 2025"
        public string LocationBg { get; set; } = string.Empty;

        public string? EventUrl { get; set; } // Обединеният линк

        public string? ImagePath { get; set; } // Пътят до снимката
    }
}