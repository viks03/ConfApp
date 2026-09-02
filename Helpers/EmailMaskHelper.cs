namespace ConferenceApp.Helpers
{
    // Изваден от VerificationModel.HideEmail — сега споделен между Verification
    // и Done страниците (и всяко бъдещо място, което трябва да покаже маскиран
    // имейл), вместо да се дублира копие на същата логика на второ място.
    public static class EmailMaskHelper
    {
        public static string Mask(string email)
        {
            var parts = email.Split('@');
            if (parts.Length != 2 || string.IsNullOrEmpty(parts[0])) return email;

            var name = parts[0];
            if (name.Length <= 2) return $"{name[0]}***@{parts[1]}";
            return $"{name[0]}{new string('*', name.Length - 2)}{name[^1]}@{parts[1]}";
        }
    }
}
