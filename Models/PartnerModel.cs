using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    public class PartnerModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string NameEn { get; set; } = string.Empty;

        [Required]
        public string NameBg { get; set; } = string.Empty;

        // Съхранява категорията: "Institutional", "Business", или "Media"
        public string Category { get; set; } = string.Empty;

        // Пътят до логото на партньора
        public string? LogoImagePath { get; set; }

        // Линк към уебсайта на партньора (незадължителен)
        public string? WebsiteUrl { get; set; }
    }
}