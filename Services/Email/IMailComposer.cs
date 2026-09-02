using System.Globalization;

namespace ConferenceApp.Services.Email
{
    public enum OtpPurpose { Registration, Login }

    /// <summary>
    /// Единствената точка, през която приложението изпраща имейл.
    /// <para>
    /// Извикващият подава ДАННИ, не HTML и не преводи. Композиторът избира
    /// темплейт, чете resx низовете за подадената култура, рендира и слага
    /// задачата във фоновата опашка.
    /// </para>
    /// <para>
    /// КУЛТУРАТА СЕ ПОДАВА ЯВНО и това не е излишно: фоновата задача се
    /// изпълнява СЛЕД края на HTTP заявката, когато CurrentUICulture вече е
    /// върната към стойността по подразбиране. Затова всички преводи се четат
    /// ТУК, преди задачата да влезе в опашката.
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
