// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    public class ScheduleModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        // Free text, and also what the programme groups by: two sessions are
        // on the same day only if this string matches exactly.
        public string Day { get; set; } = string.Empty; // e.g. "Day 1 (Oct 29, 2026)"

        [Required]
        public string StartTime { get; set; } = string.Empty; // e.g. "09:00"

        [Required]
        public string EndTime { get; set; } = string.Empty; // e.g. "10:00"

        [Required]
        public string TitleEn { get; set; } = string.Empty;

        [Required]
        public string TitleBg { get; set; } = string.Empty;

        // One of Services.Schedule.SessionTypes.All. The value is also the resx
        // key the public programme translates it with, so it must match
        // character for character.
        public string SessionType { get; set; } = string.Empty;

        public string? SpeakerEn { get; set; }
        public string? SpeakerBg { get; set; }

        public string? LocationEn { get; set; }
        public string? LocationBg { get; set; }

        public string? DescriptionEn { get; set; }
        public string? DescriptionBg { get; set; }

        // A link to the live stream of this session, if there is one.
        public string? LiveStreamUrl { get; set; } 
    }
}