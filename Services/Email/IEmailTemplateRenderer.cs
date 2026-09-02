namespace ConferenceApp.Services.Email
{
    public interface IEmailTemplateRenderer
    {
        /// <summary>
        /// Слепва рамката с тялото на темплейта и замества плейсхолдърите.
        /// Резултатът е готов за подаване на EmailSender.
        /// </summary>
        Task<string> RenderAsync(EmailTemplate template, EmailPlaceholders placeholders,
                                 CancellationToken ct = default);

        /// <summary>Изхвърля кеша — ползва се, ако темплейт се смени по време на работа.</summary>
        void ClearCache();
    }
}
