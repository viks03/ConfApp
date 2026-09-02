namespace ConferenceApp.Models
{
    public class AuditLog
    {
        public int Id { get; set; }
        public string? UserId { get; set; }
        public string UserEmail { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? Details { get; set; }
    }
}