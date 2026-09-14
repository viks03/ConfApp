// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Helpers
{
    using Microsoft.AspNetCore.Mvc.ModelBinding;
    using Microsoft.Extensions.Localization;

    /// <summary>
    /// Turns the resx KEYS that validation attributes carry in
    /// <c>ErrorMessage</c> into real text.
    ///
    /// <para>
    /// An attribute cannot use <see cref="IStringLocalizer"/> directly, so it
    /// carries a key instead (for example <c>"Error_NameLatinOnly"</c>) and the
    /// translation happens after validation. Without this the user saw the
    /// literal <c>Error_NameLatinOnly</c> on screen.
    /// </para>
    ///
    /// <para>
    /// <b>Why not</b> <c>AddDataAnnotationsLocalization()</c>: the default
    /// provider calls <c>factory.Create(modelType)</c>, that is, it looks for a
    /// resource file named <c>Pages.RegisterModel+InputModel</c>. In this
    /// project all 52 files in <c>Resources/</c> are named after the view
    /// (<c>Pages.Register.bg.resx</c>), so nothing ever matched and the
    /// registration had no effect. It was removed from <c>Program.cs</c> and
    /// the pages call this method explicitly, the same way they already call
    /// <c>localizerFactory.Create("Pages.Login", …)</c>.
    /// </para>
    ///
    /// <para>
    /// The same loop used to be copied verbatim into <c>Register</c>,
    /// <c>Profile</c> and <c>SubmitDocuments</c>; only the prefix differed.
    /// </para>
    /// </summary>
    public static class ModelStateLocalizer
    {
        public static void LocalizeErrors(
            this ModelStateDictionary modelState,
            IStringLocalizer localizer,
            string keyPrefix)
        {
            foreach (var entry in modelState)
            {
                foreach (var error in entry.Value.Errors.ToList())
                {
                    if (error.ErrorMessage.StartsWith(keyPrefix))
                    {
                        entry.Value.Errors.Remove(error);
                        entry.Value.Errors.Add(localizer[error.ErrorMessage].Value);
                    }
                }
            }
        }
    }
}
