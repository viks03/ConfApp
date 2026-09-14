// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Globalization;

namespace ConferenceApp.Services.Email
{
    public enum OtpPurpose { Registration, Login }

    /// <summary>
    /// The single point through which the application sends mail.
    /// <para>
    /// The caller passes DATA, not HTML and not translations. The composer
    /// picks the template, reads the resx strings for the given culture,
    /// renders, and puts the task on the background queue.
    /// </para>
    /// <para>
    /// The culture is passed EXPLICITLY, and that is not redundant: the
    /// background task runs after the HTTP request has ended, by which time
    /// CurrentUICulture is back to its default. Every translation is therefore
    /// resolved here, before the task enters the queue.
    /// </para>
    /// </summary>
    public interface IMailComposer
    {
        Task SendOtpAsync(string toEmail, string firstName, string code,
                          OtpPurpose purpose, CultureInfo culture, string baseUrl);

        Task SendPaymentConfirmedAsync(string toEmail, string firstName,
                                       string amount, string method, string reference,
                                       CultureInfo culture, string baseUrl);

        Task SendPaymentPendingAsync(string toEmail, string firstName,
                                     string amount, string method, string reference,
                                     CultureInfo culture, string baseUrl);

        Task SendVerificationApprovedAsync(string toEmail, string firstName,
                                           string participationType,
                                           CultureInfo culture, string baseUrl);

        Task SendVerificationRejectedAsync(string toEmail, string firstName,
                                           string participationType, string? reason,
                                           CultureInfo culture, string baseUrl);

        Task SendStatusChangedAsync(string toEmail, string firstName,
                                    string statusFrom, string statusTo,
                                    CultureInfo culture, string baseUrl);
    }
}
