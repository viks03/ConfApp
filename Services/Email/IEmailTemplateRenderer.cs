// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Services.Email
{
    public interface IEmailTemplateRenderer
    {
        /// <summary>
        /// Joins the layout frame with the template body and substitutes the
        /// placeholders. The result is ready to hand to EmailSender.
        /// </summary>
        Task<string> RenderAsync(EmailTemplate template, EmailPlaceholders placeholders,
                                 CancellationToken ct = default);

        /// <summary>Drops the cache, for when a template file changes while the
        /// application is running.</summary>
        void ClearCache();
    }
}
