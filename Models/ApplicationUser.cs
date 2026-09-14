// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
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

        // ── GDPR ──────────────────────────────────────────────────────────────
        // The consent dates are kept, not just the flags: a claim that consent
        // was never given is answered by a date.
        public bool HasAcceptedGdpr { get; set; }
        public DateTime? GdprConsentDate { get; set; }

        public bool WantsMarketing { get; set; }
        public string? PaperFilePath { get; set; }
        public DateTime? MarketingConsentDate { get; set; }

        // ── Consent to publish the paper ────────────────────────────────────
        // Separate from the GDPR consent: agreeing to take part is not agreeing
        // to have your paper published.
        public bool ConsentToPublishPaper { get; set; }
        public DateTime? PublishConsentDate { get; set; }

        // ── Payment ───────────────────────────────────────────────────────────
        // ReferenceNumber (BCE2026-XXXXX) is what ties a payment to a person at
        // both gateways; PaymentStatus is one of Pending | Confirmed.
        public string ReferenceNumber { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = "Pending";
        public string PaymentMethod { get; set; } = string.Empty;
        public DateTime? PaidAt { get; set; }

        // What was actually paid, in euro, written at the moment of
        // confirmation (Stripe, crypto, bank transfer, or by an administrator).
        // The amount used to exist only as free text in AuditLog.Details.
        // null means the payment is not confirmed yet, or the tier is not
        // payable at all (student, journalist).
        public decimal? PaidAmountEUR { get; set; }

        // ── Bank transfer ─────────────────────────────────────────────────────
        // Set when the participant states they have made the transfer. It is not
        // a payment yet — an administrator confirms it — but it does protect the
        // account from the cleanup service.
        public DateTime? IbanTransferSubmittedAt { get; set; }

        // ── Verification (student / journalist) ───────────────────────────────
        // These two forms do not pay; they prove who they are instead.
        // None | Pending | Approved | Rejected
        public string VerificationStatus { get; set; } = "None";

        // The scanned document: a student card or a press card. Stored outside
        // wwwroot and served only through a handler that checks who is asking
        // (see [F-02]).
        public string? VerificationDocumentPath { get; set; }

        // One set of columns for both forms; the label in the UI changes with
        // the participation form.
        public string? VerificationInstitution { get; set; }   // university / media outlet
        public string? VerificationSpecialty    { get; set; }  // field of study / position
        public string? VerificationYear         { get; set; }  // year of study / outlet URL
        public string? VerificationStudentId    { get; set; }  // student number (students only)

        public DateTime? VerificationSubmittedAt { get; set; }

        // The rejection reason, typed by an administrator in the panel and sent
        // to the participant in the rejection mail. null means none was given.
        public string? VerificationRejectionReason { get; set; }

        // ── Account ──────────────────────────────────────────────────────────
        // CreatedAt is what the cleanup service measures the 24 hours against.
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The language this person uses the site in — "bg" or "en".
        /// <para>
        /// [T-29] It exists for the mails. The language of a mail used to be
        /// read from the culture of the CURRENT request, which is only right
        /// when the recipient triggered it themselves. Approving a verification,
        /// confirming a payment and changing a status are all done by an
        /// administrator — and a foreign participant received their rejection
        /// reason in Bulgarian. A webhook request has no culture at all.
        /// </para>
        /// <para>
        /// Filled in at registration (the language of the form) and on every
        /// switch from the language bar while the person is signed in.
        /// <c>null</c> means "unknown" — for rows that predate the column — and
        /// the old behaviour applies. It is read only through
        /// <see cref="Services.Email.MailContext.CultureFor"/>.
        /// </para>
        /// </summary>
        public string? PreferredLanguage { get; set; }
    }
}