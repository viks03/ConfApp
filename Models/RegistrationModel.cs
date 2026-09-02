using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    public class RegistrationModel
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string AcademicTitle { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Workplace { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string ExtraInfo { get; set; } = string.Empty;
        public int PartForm { get; set; }
        public string? FilePath { get; set; }

        // ── НОВО: Съгласие за публикуване ──
        public bool ConsentToPublishPaper { get; set; }
        public DateTime? PublishConsentDate { get; set; }

        public DateTime RegisteredAt { get; set; }

        public bool IsForeigner { get; set; }
        public bool Transfer { get; set; }
        public bool Accommodation { get; set; }

        // ── Плащане ───────────────────────────────────────────────────────────
        public string ReferenceNumber { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = "Pending";
        public string PaymentMethod { get; set; } = string.Empty;
        public DateTime? PaidAt { get; set; }
    }
}