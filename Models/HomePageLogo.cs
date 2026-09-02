using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    public class HomePageLogo
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string ImagePath { get; set; } = string.Empty;

        // Опционално, но е добра практика да пазим името за "alt" тага на снимката
        public string? PartnerName { get; set; } 
    }
}