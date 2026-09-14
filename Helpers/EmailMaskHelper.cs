// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Helpers
{
    // Lifted out of VerificationModel.HideEmail so that the Verification and
    // Done pages — and anywhere else that has to show a masked address — share
    // one implementation instead of keeping a second copy of the same logic.
    public static class EmailMaskHelper
    {
        public static string Mask(string email)
        {
            var parts = email.Split('@');
            if (parts.Length != 2 || string.IsNullOrEmpty(parts[0])) return email;

            // A local part of one or two characters gets a fixed number of
            // asterisks: the general rule below keeps the first and last
            // character, which for such a name would reveal all of it.
            var name = parts[0];
            if (name.Length <= 2) return $"{name[0]}***@{parts[1]}";
            return $"{name[0]}{new string('*', name.Length - 2)}{name[^1]}@{parts[1]}";
        }
    }
}
