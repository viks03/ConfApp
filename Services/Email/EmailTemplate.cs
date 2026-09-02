namespace ConferenceApp.Services.Email
{
    /// <summary>
    /// Видовете имейли, които приложението изпраща.
    /// <para>
    /// Всяка стойност съответства на един файл в
    /// <c>wwwroot/templates/bodies/</c>. Добавянето на нов имейл значи:
    /// нова стойност тук, нов файл в bodies/, нов метод в IMailComposer.
    /// Нищо друго не се пипа.
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
        /// <summary>Име на файла в bodies/ за всеки темплейт.</summary>
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
