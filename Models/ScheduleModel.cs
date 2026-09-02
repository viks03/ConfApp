using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    public class ScheduleModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Day { get; set; } = string.Empty; // Напр. "Day 1 (Oct 29, 2026)"

        [Required]
        public string StartTime { get; set; } = string.Empty; // Напр. "09:00"

        [Required]
        public string EndTime { get; set; } = string.Empty; // Напр. "10:00"

        [Required]
        public string TitleEn { get; set; } = string.Empty;

        [Required]
        public string TitleBg { get; set; } = string.Empty;

        public string SessionType { get; set; } = string.Empty; // Plenary, Workshop, Panel и т.н.

        public string? SpeakerEn { get; set; }
        public string? SpeakerBg { get; set; }

        public string? LocationEn { get; set; }
        public string? LocationBg { get; set; }

        public string? DescriptionEn { get; set; }
        public string? DescriptionBg { get; set; }

        // НОВО: Поле за линк към живо излъчване за конкретната сесия
        public string? LiveStreamUrl { get; set; } 
    }
}