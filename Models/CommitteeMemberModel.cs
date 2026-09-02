using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    public class CommitteeMemberModel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string FullNameEn { get; set; } = string.Empty;

        [Required]
        public string FullNameBg { get; set; } = string.Empty;

        public string? RoleEn { get; set; }
        public string? RoleBg { get; set; }

        public string? OrganizationEn { get; set; }
        public string? OrganizationBg { get; set; }

        // Съхранява типа: "Organizing Committee" или "Program Committee"
        public string CommitteeType { get; set; } = string.Empty;

        // Пътят до качената снимка
        public string? AvatarImagePath { get; set; }
    }
}