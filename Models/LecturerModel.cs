// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
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

        // Keynote, Academic, Industry, or Regulatory & Policy. The public page
        // renders one section per category, in that order.
        public string Category { get; set; } = string.Empty;

        public string? RoleEn { get; set; }
        public string? RoleBg { get; set; }

        public string? OrganizationEn { get; set; }
        public string? OrganizationBg { get; set; }

        public string? BiographyEn { get; set; }
        public string? BiographyBg { get; set; }

        public string? ProfileUrl { get; set; }

        // Relative to wwwroot, e.g. "/uploads/people/lecturers/…".
        public string? AvatarImagePath { get; set; }
    }
}