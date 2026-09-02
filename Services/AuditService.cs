using ConferenceApp.Data;
using ConferenceApp.Models;

namespace ConferenceApp.Services
{
    public class AuditService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditService(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task LogAsync(string? userId, string email, string action, string details = "")
        {
            var ip = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            var log = new AuditLog {
                UserId = userId, 
                UserEmail = email, 
                Action = action, 
                IpAddress = ip, 
                Details = details
            };
            
            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();
        }
    }
}