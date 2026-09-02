using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    public class LecturerModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string FullNameEn { get; set; } = string.Empty;
        
        [Required]
        public string FullNameBg { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty; // Keynote, Academic, Industry, Regulatory & Policy

        public string? RoleEn { get; set; }
        public string? RoleBg { get; set; }

        public string? OrganizationEn { get; set; }
        public string? OrganizationBg { get; set; }

        public string? BiographyEn { get; set; }
        public string? BiographyBg { get; set; }

        // Обединеното URL поле
        public string? ProfileUrl { get; set; }

        // Пътят до качената снимка (напр. "/uploads/people/lecturers/ivan.jpg")
        public string? AvatarImagePath { get; set; }
    }
}