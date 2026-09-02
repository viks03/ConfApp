using Microsoft.AspNetCore.Identity;

namespace ConferenceApp.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int Age { get; set; }
        public string AcademicTitle { get; set; } = string.Empty;
        public string Workplace { get; set; } = string.Empty;
        public string PartForm { get; set; } = string.Empty;
        public bool IsForeigner { get; set; }

        // GDPR Tracking
        public bool HasAcceptedGdpr { get; set; }
        public DateTime? GdprConsentDate { get; set; }

        public bool WantsMarketing { get; set; }
        public string? PaperFilePath { get; set; }
        public DateTime? MarketingConsentDate { get; set; }

        // ── Съгласие за публикуване на доклада ──────────────────────────────
        public bool ConsentToPublishPaper { get; set; }
        public DateTime? PublishConsentDate { get; set; }

        // ── Плащане ───────────────────────────────────────────────────────────
        public string ReferenceNumber { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = "Pending";
        public string PaymentMethod { get; set; } = string.Empty;
        public DateTime? PaidAt { get; set; }

        // ── IBAN ──────────────────────────────────────────────────────────────
        public DateTime? IbanTransferSubmittedAt { get; set; }

        // ── Верификация (Student / Journalist) ────────────────────────────────
        // None | Pending | Approved | Rejected
        public string VerificationStatus { get; set; } = "None";

        // Документ 1: студентска книжка / прессарта
        public string? VerificationDocumentPath { get; set; }

        public string? VerificationInstitution { get; set; }   // Университет / Медия
        public string? VerificationSpecialty    { get; set; }  // Специалност / Позиция
        public string? VerificationYear         { get; set; }  // Година / URL на медията
        public string? VerificationStudentId    { get; set; }  // Факултетен № (само Student)

        public DateTime? VerificationSubmittedAt { get; set; }

        // Причина за отхвърляне — попълва се от администратора в Admin панела.
        // null = няма причина предоставена.
        public string? VerificationRejectionReason { get; set; }

        // ── Профил ───────────────────────────────────────────────────────────
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}