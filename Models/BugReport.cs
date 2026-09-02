using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // Един ред на всеки подаден bug report от плаващия widget (виж
    // Pages/Shared/_BugReportWidget.cshtml + Controllers/BugReportController.cs).
    // Достъпен само за Admin роля — login-ът е споделен между всички
    // администратори, затова ReportedByEmail е само за одит/контекст, не за
    // разграничаване "кой вижда какво" (виж коментара в BugReportController).
    public class BugReport
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Description { get; set; } = string.Empty;

        // "Bug" | "UI" | "Content" | "Performance" | "Other"
        public string Category { get; set; } = "Bug";

        // "Low" | "Medium" | "High" | "Critical"
        public string Severity { get; set; } = "Medium";

        // Автоматично уловени от JS в момента на подаване — админът не пипа нищо
        public string? PageUrl { get; set; }
        public string? UserAgent { get; set; }

        // Хванат server-side (не от клиента — client-подадено IP е спуфируемо
        // и няма смисъл, виж GetClientIp в BugReportController).
        public string? IpAddress { get; set; }

        // Споделеният admin login по User.Identity.Name — само за одит trail,
        // НЕ разчитаме на него да различава кой конкретно е подал репорта.
        public string? ReportedByEmail { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // "Open" | "InProgress" | "Resolved" | "WontFix"
        public string Status { get; set; } = "Open";

        public DateTime? ResolvedAt { get; set; }
        public string? ResolvedByEmail { get; set; }
        public string? ResolutionNotes { get; set; }
    }
}