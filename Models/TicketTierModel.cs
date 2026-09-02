using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    public class TicketTierModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string NameEn { get; set; } = string.Empty;

        [Required]
        public string NameBg { get; set; } = string.Empty;

        [Required]
        public string DescriptionEn { get; set; } = string.Empty;

        [Required]
        public string DescriptionBg { get; set; } = string.Empty;

        [Required]
        public string RegularPriceEn { get; set; } = string.Empty;

        [Required]
        public string RegularPriceBg { get; set; } = string.Empty;

        public string? PromoPriceEn { get; set; }

        public string? PromoPriceBg { get; set; }

        [Required]
        public string PerksEn { get; set; } = string.Empty;

        [Required]
        public string PerksBg { get; set; } = string.Empty;
    }
}