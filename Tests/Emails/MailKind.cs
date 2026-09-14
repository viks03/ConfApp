// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Tests.Emails;

/// <summary>
/// The automatic messages seen from the side of the event that produces them.
/// <para>
/// It exists so that the checks on links and on language can run across
/// <b>every</b> kind without each of them rebuilding the event by hand. The
/// application's own enum (<c>EmailTemplate</c>) does not do this job: it knows
/// which template is rendered, not who presses the button.
/// </para>
/// </summary>
public enum MailKind
{
    OtpRegistration,
    OtpLogin,
    PaymentPending,
    PaymentConfirmed,
    VerificationApproved,
    VerificationRejected,
    StatusChanged
}

public static class MailKinds
{
    /// <summary>All of them, so that a new kind cannot be left out when one is added.</summary>
    public static readonly MailKind[] All = Enum.GetValues<MailKind>();

    /// <summary>The subject's key in <c>EmailMessages.*.resx</c>.</summary>
    public static string SubjectKey(MailKind kind) => kind switch
    {
        MailKind.OtpRegistration      => "Email_Otp_Registration_Subject",
        MailKind.OtpLogin             => "Email_Otp_Login_Subject",
        MailKind.PaymentPending       => "Email_PayPending_Subject",
        MailKind.PaymentConfirmed     => "Email_PayConfirmed_Subject",
        MailKind.VerificationApproved => "Email_VerifApproved_Subject",
        MailKind.VerificationRejected => "Email_VerifRejected_Subject",
        MailKind.StatusChanged        => "Email_StatusChanged_Subject",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Няма тема за този вид.")
    };

    /// <summary>
    /// Who triggers the event. The difference is not cosmetic: for these four the
    /// request belongs to the ADMINISTRATOR, so the language of the message cannot
    /// come from it. Since [T-29] it comes from the recipient's recorded language,
    /// and that is exactly what the samples of these kinds do: they record a
    /// language on the participant and sign the administrator in under the other.
    /// </summary>
    public static bool TriggeredByAdmin(MailKind kind) => kind is
        MailKind.PaymentConfirmed or
        MailKind.VerificationApproved or
        MailKind.VerificationRejected or
        MailKind.StatusChanged;

    /// <summary>The address the button in this message points at. The one-time codes have no button.</summary>
    public static string? ButtonPath(MailKind kind) => kind switch
    {
        MailKind.PaymentPending       => "/Payment",
        MailKind.PaymentConfirmed     => "/Profile",
        MailKind.VerificationApproved => "/Profile",
        MailKind.VerificationRejected => "/SubmitDocuments",
        MailKind.StatusChanged        => "/Profile",
        _ => null
    };
}
