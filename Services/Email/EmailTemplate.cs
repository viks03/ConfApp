// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Services.Email
{
    /// <summary>
    /// The kinds of mail the application sends.
    /// <para>
    /// Every value corresponds to one file in
    /// <c>wwwroot/templates/bodies/</c>. Adding a new mail means: a new value
    /// here, a new file in bodies/, a new method on IMailComposer. Nothing
    /// else has to change.
    /// </para>
    /// </summary>
    public enum EmailTemplate
    {
        Otp,
        PaymentConfirmed,
        PaymentPending,
        VerificationApproved,
        VerificationRejected,
        StatusChanged
    }

    public static class EmailTemplateFiles
    {
        /// <summary>The file name in bodies/ for each template.</summary>
        public static string FileName(EmailTemplate template) => template switch
        {
            EmailTemplate.Otp                  => "otp.html",
            EmailTemplate.PaymentConfirmed     => "payment-confirmed.html",
            EmailTemplate.PaymentPending       => "payment-pending.html",
            EmailTemplate.VerificationApproved => "verification-approved.html",
            EmailTemplate.VerificationRejected => "verification-rejected.html",
            EmailTemplate.StatusChanged        => "status-changed.html",
            _ => throw new ArgumentOutOfRangeException(nameof(template), template, "Няма файл за този темплейт.")
        };
    }
}
