// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
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

        // "Organizing Committee" or "Program Committee" — the public page
        // groups the members by this value.
        public string CommitteeType { get; set; } = string.Empty;

        // Relative to wwwroot; the file is written by the admin panel under a
        // GUID name.
        public string? AvatarImagePath { get; set; }
    }
}