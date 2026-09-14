// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Models
{
    // A one-time code for registration or sign-in. Rows live 15 minutes and are
    // deleted by the cleanup service seven days after they expire.
    public class OtpCode
    {
        public int Id { get; set; }

        // The address, not a user id: a code is issued before the account
        // exists, and in the sign-in case before anyone is authenticated.
        public string Email { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public DateTime ExpirationTime { get; set; }
        public bool IsUsed { get; set; }
        // "Registration" or "Login". Codes are looked up by address AND
        // purpose, so a registration code cannot be used to sign in.
        public string Purpose { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}