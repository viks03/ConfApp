namespace ConferenceApp.Models
{
    public class OtpCode
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public DateTime ExpirationTime { get; set; }
        public bool IsUsed { get; set; }
        public string Purpose { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}