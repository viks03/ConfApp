using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    public class HotelModel
    {
        public int Id { get; set; }

        [Required]
        public string NameEn { get; set; } = string.Empty;

        [Required]
        public string NameBg { get; set; } = string.Empty;

        public string? DescriptionEn { get; set; }
        public string? DescriptionBg { get; set; }

        public string? Url { get; set; }
    }
}