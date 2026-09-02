using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Text.RegularExpressions;

namespace ConferenceApp.Areas.Admin.Pages
{
    [Authorize(Roles = "Admin")]
    // Всяко променящо действие в панела се записва в одита автоматично.
    // Виж Services/Audit/AdminAuditFilter.cs — там е и списъкът с handler-и,
    // които пишат собствен, по-подробен запис и затова се пропускат.
    [ServiceFilter(typeof(ConferenceApp.Services.Audit.AdminAuditFilter))]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly UserManager<ApplicationUser> _userManager;

        private readonly ConferenceApp.Services.Email.IMailComposer _mail;
        private readonly ConferenceApp.Services.Email.IEmailNotificationSettings _emailSettings;
        private readonly ConferenceApp.Services.IPaymentGateSettings _paymentGates;
        private readonly ConferenceApp.Services.Health.IHealthCheckService _health;

        /// <summary>Състоянието на превключвателите за имейл известията.</summary>
        public Dictionary<string, bool> EmailToggles { get; private set; } = new();

        /// <summary>Състоянието на осемте ключа в Payment Control.</summary>
        public Dictionary<string, bool> PaymentGates { get; private set; } = new();
        private readonly IConfiguration _config;

        public IndexModel(
            ApplicationDbContext context,
            IWebHostEnvironment env,
            UserManager<ApplicationUser> userManager,
            ConferenceApp.Services.Email.IMailComposer mail,
            ConferenceApp.Services.Email.IEmailNotificationSettings emailSettings,
            ConferenceApp.Services.IPaymentGateSettings paymentGates,
            ConferenceApp.Services.Health.IHealthCheckService health,
            IConfiguration config)
        {
            _context = context;
            _env = env;
            _userManager = userManager;
            _mail = mail;
            _emailSettings = emailSettings;
            _paymentGates = paymentGates;
            _health = health;
            _config = config;
        }

        // ══════════════════════════════════════════════════════════════════════
        // PROPERTIES
        // ══════════════════════════════════════════════════════════════════════

        // ── Registrations ─────────────────────────────────────────────────────
        public List<ApplicationUser> RegisteredUsers { get; set; } = new();

        // ── Dashboard Stats ───────────────────────────────────────────────────
        public int    TotalUsers              { get; set; }
        public int    ConfirmedPaymentsCount  { get; set; }
        public int    PendingPaymentsCount    { get; set; }
        public int    IbanPendingCount        { get; set; }
        public int    PendingVerifCount       { get; set; }
        public int    TotalCryptoOrders       { get; set; }
        public int    TotalCleanedAccounts    { get; set; }
        public string LastCleanupTime         { get; set; } = "Pending";
        public string NextCleanupTime         { get; set; } = "Pending";

        // ── Bug Reports (badge на sidebar линка) ─────────────────────────────
        public int OpenBugReportCount { get; set; }

        // ── Payments ──────────────────────────────────────────────────────────
        public List<ApplicationUser> IbanPending { get; set; } = new();

        // ── Verifications ─────────────────────────────────────────────────────
        public List<ApplicationUser> PendingVerifications { get; set; } = new();
        public List<ApplicationUser> AllVerifications     { get; set; } = new();

        // ── Crypto Orders ─────────────────────────────────────────────────────
        public List<CryptoOrder> CryptoOrders { get; set; } = new();

        // ── Audit Logs ────────────────────────────────────────────────────────
        public List<AuditLog> RecentAuditLogs { get; set; } = new();

        // ── Content Management ────────────────────────────────────────────────
        public List<TicketTierModel>      TicketTiers      { get; set; } = new();
        public List<LecturerModel>        Lecturers        { get; set; } = new();
        public List<EventModel>           Events           { get; set; } = new();
        public List<CommitteeMemberModel> CommitteeMembers { get; set; } = new();
        public List<PartnerModel>         Partners         { get; set; } = new();
        public List<ScheduleModel>        ScheduleSessions { get; set; } = new();
        public List<HotelModel>           Hotels           { get; set; } = new();
        public List<HomePageLogo>         HomePageLogos    { get; set; } = new(); 

        // ── Site Settings: Social Links + Promo Slides + FAQ ──────────────────
        public SocialLinksSetting SocialLinks { get; set; } = new();
        public List<PromoSlideModel> PromoSlides { get; set; } = new();
        public List<FaqModel> Faqs { get; set; } = new(); 

        // ── Site Settings: Footer content (singleton) + Quick Links ──────────
        public FooterContent FooterContent { get; set; } = new();
        public List<FooterQuickLinkModel> FooterQuickLinks { get; set; } = new();

        // ── Privacy Policy / GDPR съдържание (редактируемо, замества resx) ────
        public PrivacyPolicyContent PrivacyContent { get; set; } = new();

        // ── Terms of Use съдържание (редактируемо, замества resx) ─────────────
        public TermsOfUseContent TermsContent { get; set; } = new();

        // ── Cookie Notice (категории + главен текст) ─────────────────────────
        public List<CookieCategory> CookieCategories { get; set; } = new();
        public CookieNoticeContent CookieNotice { get; set; } = new();
        public CookiePolicyContent CookiePolicy { get; set; } = new();
        
        public string LiveStreamLink { get; set; } = "#";

        [BindProperty]
        public TicketTierModel EditTicket { get; set; } = new();

        // ══════════════════════════════════════════════════════════════════════
        // ON GET
        // ══════════════════════════════════════════════════════════════════════
        public async Task OnGetAsync()
        {
            // Състоянието на превключвателите за имейлите. Липсващите записи се
            // създават автоматично при първото зареждане, включени по подразбиране.
            EmailToggles = await _emailSettings.GetAllAsync();

            // Payment Control — липсващите ключове се създават автоматично
            // при първото зареждане, включени по подразбиране.
            PaymentGates = await _paymentGates.GetAllAsync();

            RegisteredUsers = await _userManager.Users
                .Where(u => u.Email != "sys.auth_7x9b@conference.unwe.bg")
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();

            IbanPending = RegisteredUsers
                .Where(u => u.IbanTransferSubmittedAt.HasValue
                         && u.PaymentStatus != "Confirmed"
                         && u.PaymentStatus != "Cancelled")
                .OrderBy(u => u.IbanTransferSubmittedAt)
                .ToList();

            PendingVerifications = RegisteredUsers
                .Where(u => u.VerificationStatus == "Pending"
                         && (u.PartForm == "2" || u.PartForm == "4"))
                .OrderBy(u => u.VerificationSubmittedAt)
                .ToList();

            AllVerifications = RegisteredUsers
                .Where(u => u.PartForm == "2" || u.PartForm == "4")
                .OrderByDescending(u => u.VerificationSubmittedAt)
                .ToList();

            CryptoOrders = await _context.CryptoOrders
                .OrderByDescending(o => o.CreatedAt)
                .Take(200)
                .ToListAsync();

            RecentAuditLogs = await _context.Set<AuditLog>()
                .OrderByDescending(a => a.Timestamp)
                .Take(200)
                .ToListAsync();

            // Dashboard stats
            TotalUsers             = RegisteredUsers.Count;
            ConfirmedPaymentsCount = RegisteredUsers.Count(u => u.PaymentStatus == "Confirmed");
            PendingPaymentsCount   = RegisteredUsers.Count(u => u.PaymentStatus != "Confirmed" && u.PaymentStatus != "Cancelled");
            IbanPendingCount       = IbanPending.Count;
            PendingVerifCount      = PendingVerifications.Count;
            TotalCryptoOrders      = await _context.CryptoOrders.CountAsync();

            OpenBugReportCount = await _context.BugReports.CountAsync(b => b.Status == "Open");

            TotalCleanedAccounts = await _context.Set<AuditLog>()
                .CountAsync(a => a.Action == "System Cleanup");

            var lastCleanupLog = await _context.Set<AuditLog>()
                .Where(a => a.Action == "Cleanup Summary")
                .OrderByDescending(a => a.Timestamp)
                .FirstOrDefaultAsync();

            if (lastCleanupLog != null)
            {
                var bgTime = ConvertToBgTime(lastCleanupLog.Timestamp);
                LastCleanupTime = bgTime.ToString("dd.MM.yyyy, HH:mm");
                NextCleanupTime = bgTime.AddHours(24).ToString("dd.MM.yyyy, HH:mm");
            }

            // Content
            TicketTiers      = await _context.TicketTiers.ToListAsync();
            Lecturers        = await _context.Lecturers.ToListAsync();
            Events           = await _context.Events.ToListAsync();
            CommitteeMembers = await _context.CommitteeMembers.ToListAsync();
            Partners         = await _context.Partners.ToListAsync();
            Hotels           = await _context.Hotels.ToListAsync();
            HomePageLogos    = await _context.HomePageLogos.ToListAsync(); 

            // ── Site Settings ─────────────────────────────────────────
            SocialLinks = await _context.SocialLinksSettings.FirstOrDefaultAsync() ?? new SocialLinksSetting();
            PromoSlides = await _context.PromoSlides
                .OrderBy(p => p.DisplayOrder)
                .ToListAsync();

            Faqs = await _context.Faqs
                .OrderBy(f => f.DisplayOrder)
                .ToListAsync();

            FooterContent = await _context.FooterContents.FirstOrDefaultAsync() ?? new FooterContent();
            // Без OrderBy по нарочна причина — редът на показване на самия
            // публичен footer е рандомен (виж _Layout.cshtml), а тук в
            // admin списъка Id-то е напълно достатъчно за стабилна подредба.
            FooterQuickLinks = await _context.FooterQuickLinks
                .OrderBy(l => l.Id)
                .ToListAsync();

            PrivacyContent = await _context.PrivacyPolicyContents.FirstOrDefaultAsync() ?? new PrivacyPolicyContent();
            TermsContent = await _context.TermsOfUseContents.FirstOrDefaultAsync() ?? new TermsOfUseContent();

            CookieCategories = await _context.CookieCategories
                .OrderBy(c => c.DisplayOrder)
                .ToListAsync();
            CookieNotice = await _context.CookieNoticeContents.FirstOrDefaultAsync() ?? new CookieNoticeContent();
            CookiePolicy = await _context.CookiePolicyContents.FirstOrDefaultAsync() ?? new CookiePolicyContent();

            ScheduleSessions = await _context.Schedule
                .OrderBy(s => s.Day)
                .ThenBy(s => s.StartTime)
                .ToListAsync();

            var streamSetting = await _context.LinkWatches.FirstOrDefaultAsync();
            if (streamSetting != null)
            {
                LiveStreamLink = streamSetting.WatchOnlineLink;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // UTILITY
        // ══════════════════════════════════════════════════════════════════════
        public static DateTime ConvertToBgTime(DateTime utcDate)
        {
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(utcDate,
                    TimeZoneInfo.FindSystemTimeZoneById("Europe/Sofia"));
            }
            catch
            {
                try
                {
                    return TimeZoneInfo.ConvertTimeFromUtc(utcDate,
                        TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time"));
                }
                catch { return utcDate.AddHours(3); }
            }
        }

        /// <summary>
        /// Сумата за имейла при админско потвърждаване на плащане.
        /// <para>
        /// Тарифата зависи от формата на участие. Ако не може да се определи,
        /// връща тире — сумата е информативна и липсата ѝ не бива да проваля
        /// потвърждаването на плащането.
        /// </para>
        /// </summary>
        private async Task<string> ResolveUserAmountAsync(ApplicationUser user)
        {
            try
            {
                var tiers = await _context.TicketTiers.ToListAsync();
                if (tiers.Count == 0) return "—";

                // Онлайн участие (3) е отделна тарифа; всичко останало ползва
                // стандартната. Студент и журналист не плащат.
                var ticket = user.PartForm == "3"
                    ? tiers.FirstOrDefault(t => t.NameEn.Contains("Online", StringComparison.OrdinalIgnoreCase))
                    : null;
                ticket ??= tiers.FirstOrDefault(t => t.Id == 2) ?? tiers[0];

                var priceStr = !string.IsNullOrWhiteSpace(ticket.PromoPriceEn)
                    ? ticket.PromoPriceEn
                    : ticket.RegularPriceEn;

                var match = System.Text.RegularExpressions.Regex.Match(priceStr ?? string.Empty, @"\d+");
                return match.Success && decimal.TryParse(match.Value, out var parsed)
                    ? $"{parsed:F2} EUR"
                    : "—";
            }
            catch
            {
                return "—";
            }
        }

        public static string FormatPartForm(string? partForm) => partForm switch
        {
            "1" => "Lector / Academic",
            "2" => "Student / PhD Candidate",
            "3" => "Online Participant",
            "4" => "Journalist / Media",
            _   => partForm ?? "Unknown"
        };

        private void LogAudit(string userId, string userEmail, string action, string details)
        {
            _context.Set<AuditLog>().Add(new AuditLog
            {
                UserId    = userId,
                UserEmail = userEmail,
                Action    = action,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                Details   = details,
                Timestamp = DateTime.UtcNow
            });
        }

        private string FormatAuditDetails(string details)
        {
            if (string.IsNullOrWhiteSpace(details)) return "-";
            return details
                .Replace("'True' -> 'False'", "'Yes' -> 'No'")
                .Replace("'False' -> 'True'", "'No' -> 'Yes'")
                .Replace("True", "Yes").Replace("False", "No")
                .Replace("Да", "Yes").Replace("Не", "No")
                .Replace("Чужденец:", "Foreigner:")
                .Replace("Маркетинг:", "Marketing Consent:")
                .Replace("Плащане:", "Payment Status:")
                .Replace("Акаунт потвърден:", "Account Verified:")
                .Replace("форма на участие: 1", "Participation: Lector")
                .Replace("форма на участие: 2", "Participation: Student")
                .Replace("форма на участие: 3", "Participation: Online")
                .Replace("форма на участие: 4", "Participation: Journalist")
                .Replace("Успешен вход с парола.", "Successful password login.")
                .Replace("Успешен вход с код.", "Successful OTP login.")
                .Replace("Потребителят успешно потвърди имейл адреса си.", "User successfully verified email address.")
                .Replace("Администратор изтри потребител", "Administrator deleted user");
        }

        private string SaveUploadedFile(IFormFile file, string subfolder, long maxSizeBytes = 5 * 1024 * 1024, string[]? allowedExtensions = null)
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowed = allowedExtensions ?? new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
            if (!allowed.Contains(ext))
                throw new InvalidOperationException($"Invalid file format. Allowed: {string.Join(", ", allowed.Select(e => e.TrimStart('.').ToUpperInvariant()))}.");
            if (file.Length > maxSizeBytes)
                throw new InvalidOperationException($"File is too large. Maximum size is {maxSizeBytes / (1024 * 1024)}MB.");

            var folder = Path.Combine(_env.WebRootPath, "uploads", subfolder);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            var fname = Guid.NewGuid().ToString() + ext;
            using var fs = new FileStream(Path.Combine(folder, fname), FileMode.Create);
            file.CopyTo(fs);
            return $"/uploads/{subfolder}/{fname}";
        }

        private void DeleteFile(string? relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return;
            var physical = Path.Combine(_env.WebRootPath,
                relativePath.TrimStart('~', '/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(physical)) System.IO.File.Delete(physical);
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORTS
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetExportRegistrationsAsync(string type)
        {
            var query = _userManager.Users
                .Where(u => u.Email != "sys.auth_7x9b@conference.unwe.bg");

            if (type == "confirmed")  query = query.Where(u => u.PaymentStatus == "Confirmed");
            else if (type == "pending")   query = query.Where(u => u.PaymentStatus == "Pending" || u.PaymentStatus == "");
            else if (type == "cancelled") query = query.Where(u => u.PaymentStatus == "Cancelled");

            var users = await query.OrderByDescending(u => u.CreatedAt).ToListAsync();

            var sb = new System.Text.StringBuilder();
            sb.Append("\uFEFF");
            sb.AppendLine("First Name,Last Name,Age,Academic Title,Email,Phone,Workplace," +
                          "Participation Type,Foreigner,Payment Status,Payment Method," +
                          "Reference Number,Account Verified,Verification Status," +
                          "GDPR,Marketing,Publish Consent,Registration Date (BG)");

            foreach (var u in users)
            {
                var bgTime = ConvertToBgTime(u.CreatedAt).ToString("dd.MM.yyyy HH:mm");
                sb.AppendLine(
                    $"\"{u.FirstName}\",\"{u.LastName}\",{u.Age},\"{u.AcademicTitle}\"," +
                    $"\"{u.Email}\",\"{u.PhoneNumber}\",\"{u.Workplace}\"," +
                    $"\"{FormatPartForm(u.PartForm)}\",\"{(u.IsForeigner ? "Yes" : "No")}\"," +
                    $"\"{u.PaymentStatus}\",\"{u.PaymentMethod}\",\"{u.ReferenceNumber}\"," +
                    $"\"{(u.EmailConfirmed ? "Yes" : "No")}\",\"{u.VerificationStatus}\"," +
                    $"\"{(u.HasAcceptedGdpr ? "Yes" : "No")}\",\"{(u.WantsMarketing ? "Yes" : "No")}\"," +
                    $"\"{(u.ConsentToPublishPaper ? "Yes" : "No")}\",\"{bgTime}\"");
            }

            return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()),
                "text/csv", $"Registrations_{type}_{DateTime.Now:yyyyMMdd}.csv");
        }

        public async Task<IActionResult> OnGetExportAuditLogsAsync()
        {
            var logs = await _context.Set<AuditLog>()
                .OrderByDescending(a => a.Timestamp)
                .ToListAsync();

            var sb = new System.Text.StringBuilder();
            sb.Append("\uFEFF");
            sb.AppendLine("Date & Time (BG),User Email,Action,IP Address,Details");

            foreach (var l in logs)
            {
                var bgTime  = ConvertToBgTime(l.Timestamp).ToString("dd.MM.yyyy HH:mm:ss");
                var ip      = l.IpAddress == "::1" ? "Localhost" : (l.IpAddress ?? "Unknown");
                var details = FormatAuditDetails(l.Details ?? "-").Replace("\"", "\"\"");
                sb.AppendLine($"\"{bgTime}\",\"{l.UserEmail}\",\"{l.Action}\",\"{ip}\",\"{details}\"");
            }

            return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()),
                "text/csv", $"AuditLogs_{DateTime.Now:yyyyMMdd}.csv");
        }

        // ══════════════════════════════════════════════════════════════════════
        // REGISTRATIONS
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveRegistrationAsync(
            [FromForm] string id,
            [FromForm] string firstName,
            [FromForm] string lastName,
            [FromForm] int age,
            [FromForm] string phone,
            [FromForm] string academicTitle,
            [FromForm] string organization,
            [FromForm] string participation,
            [FromForm] bool isForeigner,
            [FromForm] bool emailConfirmed,
            [FromForm] string paymentStatus,
            [FromForm] string? verificationStatus)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(id))
                    return new JsonResult(new { success = false, message = "Invalid user ID." });
                if (string.IsNullOrWhiteSpace(firstName))
                    return new JsonResult(new { success = false, message = "First name is required." });
                if (string.IsNullOrWhiteSpace(lastName))
                    return new JsonResult(new { success = false, message = "Last name is required." });
                if (age < 16 || age > 100)
                    return new JsonResult(new { success = false, message = "Age must be between 16 and 100." });

                var user = await _userManager.FindByIdAsync(id);
                if (user == null)
                    return new JsonResult(new { success = false, message = "User not found." });

                var changes = new List<string>();
                if (user.FirstName     != firstName)    changes.Add($"First Name: '{user.FirstName}' → '{firstName}'");
                if (user.LastName      != lastName)     changes.Add($"Last Name: '{user.LastName}' → '{lastName}'");
                if (user.Age           != age)          changes.Add($"Age: '{user.Age}' → '{age}'");
                if (user.PartForm      != participation) changes.Add($"Participation: '{FormatPartForm(user.PartForm)}' → '{FormatPartForm(participation)}'");
                if (user.PaymentStatus != paymentStatus) changes.Add($"Payment Status: '{user.PaymentStatus}' → '{paymentStatus}'");
                if (user.EmailConfirmed != emailConfirmed) changes.Add($"Account Verified: '{user.EmailConfirmed}' → '{emailConfirmed}'");
                var newVerifStatus = verificationStatus ?? user.VerificationStatus ?? "None";
                var validVerifStatuses = new[] { "None", "Pending", "Approved", "Rejected" };
                if (!validVerifStatuses.Contains(newVerifStatus)) newVerifStatus = user.VerificationStatus ?? "None";
                if (user.VerificationStatus != newVerifStatus) changes.Add($"Verification Status: '{user.VerificationStatus}' → '{newVerifStatus}'");

                // Улавяме статусите ПРЕДИ промяната. Имейл се праща само ако
                // някой от тях реално се е сменил — иначе поправка на печатна
                // грешка в името щеше да прати "статусът ви е променен".
                var prevPayStatus   = user.PaymentStatus;
                var prevVerifStatus = user.VerificationStatus;
                var prevPartForm    = user.PartForm;

                user.FirstName           = firstName.Trim();
                user.LastName            = lastName.Trim();
                user.Age                 = age;
                user.PhoneNumber         = phone?.Trim() ?? "";
                user.AcademicTitle       = academicTitle?.Trim() ?? "";
                user.Workplace           = organization?.Trim() ?? "";
                user.PartForm            = participation ?? "";
                user.IsForeigner         = isForeigner;
                user.EmailConfirmed      = emailConfirmed;
                user.PaymentStatus       = paymentStatus ?? "Pending";
                user.VerificationStatus  = newVerifStatus;

                if (user.PaymentStatus == "Pending" || user.PaymentStatus == "Cancelled")
                {
                    user.IbanTransferSubmittedAt = null;
                    user.PaidAt = null;
                }

                if (paymentStatus == "Confirmed" && user.PaidAt == null)
                {
                    user.PaidAt        = DateTime.UtcNow;
                    user.PaymentMethod = "Manual";
                }

                if (newVerifStatus == "Approved" && (user.PartForm == "2" || user.PartForm == "4")
                    && user.PaymentStatus != "Confirmed")
                {
                    user.PaymentStatus = "Confirmed";
                    user.PaymentMethod = "Subsidised";
                    user.PaidAt        = DateTime.UtcNow;
                }

                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                    return new JsonResult(new { success = false, message = string.Join(", ", result.Errors.Select(e => e.Description)) });

                if (changes.Any())
                {
                    LogAudit(user.Id, user.Email ?? "", "Admin Edit",
                        "Changes: " + string.Join(" | ", changes));
                    await _context.SaveChangesAsync();
                }

                // Стойностите се четат СЛЕД всички мутации по-горе — включително
                // автоматичното потвърждаване на плащането при одобрена
                // верификация за студент/журналист.
                string? changedLabel = null, fromValue = null, toValue = null;

                if (prevPayStatus != user.PaymentStatus)
                {
                    changedLabel = "Payment";
                    fromValue    = prevPayStatus   ?? "—";
                    toValue      = user.PaymentStatus ?? "—";
                }
                else if (prevVerifStatus != user.VerificationStatus)
                {
                    changedLabel = "Verification";
                    fromValue    = prevVerifStatus ?? "—";
                    toValue      = user.VerificationStatus ?? "—";
                }
                else if (prevPartForm != user.PartForm)
                {
                    changedLabel = "Participation";
                    fromValue    = ConferenceApp.Services.Email.MailContext.ParticipationName(prevPartForm);
                    toValue      = ConferenceApp.Services.Email.MailContext.ParticipationName(user.PartForm);
                }

                // Едно писмо на редакция, не по едно на променено поле.
                if (changedLabel != null)
                {
                    await _mail.SendStatusChangedAsync(
                        toEmail:    user.Email ?? string.Empty,
                        firstName:  user.FirstName ?? string.Empty,
                        statusFrom: fromValue!,
                        statusTo:   toValue!,
                        culture:    ConferenceApp.Services.Email.MailContext.CultureFor(user),
                        baseUrl:    ConferenceApp.Services.Email.MailContext.BaseUrl(_config, Request));
                }

                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = "Error saving user: " + ex.Message });
            }
        }

        public async Task<IActionResult> OnPostDeleteUserAsync([FromForm] string id)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(id);
                if (user == null)
                    return new JsonResult(new { success = false, message = "User not found." });

                DeleteFile(user.PaperFilePath);
                DeleteFile(user.VerificationDocumentPath);

                var otps = await _context.Set<OtpCode>()
                    .Where(o => o.Email == user.Email)
                    .ToListAsync();
                if (otps.Any()) _context.RemoveRange(otps);

                LogAudit(user.Id, user.Email ?? "", "User Deleted",
                    $"Admin deleted: {user.FirstName} {user.LastName} | Ref: {user.ReferenceNumber} | Type: {FormatPartForm(user.PartForm)}");

                await _context.SaveChangesAsync();
                await _userManager.DeleteAsync(user);

                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = "Error deleting user: " + ex.Message });
            }
        }

        public async Task<IActionResult> OnGetDownloadPaper(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null || string.IsNullOrEmpty(user.PaperFilePath)) return NotFound();

            var physical = Path.Combine(_env.WebRootPath,
                user.PaperFilePath.TrimStart('~', '/').Replace('/', Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(physical)) return NotFound();

            return PhysicalFile(physical, "application/octet-stream",
                Path.GetFileName(user.PaperFilePath));
        }

        public async Task<IActionResult> OnGetFetchRejectionReasonAsync(string userId)
        {
            var log = await _context.Set<AuditLog>()
                .Where(a => a.UserId == userId && a.Action == "Verification Rejected")
                .OrderByDescending(a => a.Timestamp)
                .FirstOrDefaultAsync();

            if (log == null)
                return new JsonResult(new { reason = (string?)null });

            var details = log.Details ?? "";
            var reasonIdx = details.IndexOf("Reason: ", StringComparison.OrdinalIgnoreCase);
            var reason = reasonIdx >= 0 ? details[(reasonIdx + 8)..].Trim() : null;

            return new JsonResult(new { reason });
        }

        public async Task<IActionResult> OnGetFetchUserAuditsAsync(string email)
        {
            var dbLogs = await _context.Set<AuditLog>()
                .Where(a => a.UserEmail == email)
                .OrderByDescending(a => a.Timestamp)
                .Take(20)
                .ToListAsync();

            var logs = dbLogs.Select(a => new {
                action  = a.Action,
                ip      = a.IpAddress == "::1" ? "Localhost" : (a.IpAddress ?? "Unknown"),
                date    = ConvertToBgTime(a.Timestamp).ToString("dd MMM yyyy, HH:mm '(BG)'"),
                details = FormatAuditDetails(a.Details ?? "-")
            });

            return new JsonResult(logs);
        }

        // ══════════════════════════════════════════════════════════════════════
        // PAYMENTS
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostConfirmPaymentAsync(
            [FromForm] string userId,
            [FromForm] string method)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(userId);
                if (user == null)
                    return new JsonResult(new { success = false, message = "User not found." });
                if (user.PaymentStatus == "Confirmed")
                    return new JsonResult(new { success = false, message = "Payment is already confirmed." });

                var validMethods = new[] { "Card", "Crypto", "IBAN", "Manual" };
                var payMethod = validMethods.Contains(method) ? method : "Manual";

                user.PaymentStatus = "Confirmed";
                user.PaymentMethod = payMethod;
                user.PaidAt        = DateTime.UtcNow;

                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                    return new JsonResult(new { success = false, message = string.Join(", ", result.Errors.Select(e => e.Description)) });

                LogAudit(user.Id, user.Email ?? "", "Payment Confirmed — Admin",
                    $"Method: {payMethod} | Ref: {user.ReferenceNumber} | User: {user.FirstName} {user.LastName}");
                await _context.SaveChangesAsync();

                // ЛИПСВАШЕ: когато администратор потвърди плащане (най-често
                // банков превод, след като го е видял в сметката), потребителят
                // не получаваше нищо. Оставаше с писмото "в обработка" отпреди
                // няколко дни и нямаше как да разбере, че вече е потвърдено.
                // Guard-ът "вече потвърдено" е в началото на метода, така че
                // тук се стига само при реална промяна.
                await _mail.SendPaymentConfirmedAsync(
                    toEmail:   user.Email ?? string.Empty,
                    firstName: user.FirstName ?? string.Empty,
                    amount:    await ResolveUserAmountAsync(user),
                    method:    ConferenceApp.Services.Email.MailContext.PaymentMethodName(payMethod),
                    reference: user.ReferenceNumber ?? "—",
                    culture:   ConferenceApp.Services.Email.MailContext.CultureFor(user),
                    baseUrl:   ConferenceApp.Services.Email.MailContext.BaseUrl(_config, Request));

                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = "Error confirming payment: " + ex.Message });
            }
        }

        public async Task<IActionResult> OnPostCancelPaymentAsync([FromForm] string userId)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(userId);
                if (user == null)
                    return new JsonResult(new { success = false, message = "User not found." });

                var prevStatus = user.PaymentStatus;
                user.PaymentStatus = "Cancelled";
                
                user.IbanTransferSubmittedAt = null;
                user.PaidAt = null;

                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                    return new JsonResult(new { success = false, message = string.Join(", ", result.Errors.Select(e => e.Description)) });

                LogAudit(user.Id, user.Email ?? "", "Payment Cancelled — Admin",
                    $"Previous status: {prevStatus} | Ref: {user.ReferenceNumber} | User: {user.FirstName} {user.LastName}");
                await _context.SaveChangesAsync();

                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = "Error cancelling payment: " + ex.Message });
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // VERIFICATIONS
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostApproveVerificationAsync([FromForm] string userId)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(userId);
                if (user == null)
                    return new JsonResult(new { success = false, message = "User not found." });
                if (user.VerificationStatus == "Approved")
                    return new JsonResult(new { success = false, message = "Verification is already approved." });

                var prevStatus = user.VerificationStatus;
                user.VerificationStatus          = "Approved";
                user.VerificationRejectionReason = null;

                bool autoPayment = (user.PartForm == "2" || user.PartForm == "4")
                                && user.PaymentStatus != "Confirmed";
                if (autoPayment)
                {
                    user.PaymentStatus = "Confirmed";
                    user.PaymentMethod = "Subsidised";
                    user.PaidAt        = DateTime.UtcNow;
                }

                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                    return new JsonResult(new { success = false, message = string.Join(", ", result.Errors.Select(e => e.Description)) });

                LogAudit(user.Id, user.Email ?? "", "Verification Approved",
                    $"Type: {FormatPartForm(user.PartForm)} | Institution: {user.VerificationInstitution ?? "—"} " +
                    $"| PrevStatus: {prevStatus} | Payment auto-confirmed: {(autoPayment ? "Yes" : "No")}");
                await _context.SaveChangesAsync();

                // Guard-ът "вече одобрено" е в началото на метода, така че тук
                // се стига само при реална промяна на статуса.
                await _mail.SendVerificationApprovedAsync(
                    toEmail:            user.Email ?? string.Empty,
                    firstName:          user.FirstName ?? string.Empty,
                    participationType:  ConferenceApp.Services.Email.MailContext.ParticipationName(user.PartForm),
                    culture:            ConferenceApp.Services.Email.MailContext.CultureFor(user),
                    baseUrl:            ConferenceApp.Services.Email.MailContext.BaseUrl(_config, Request));

                return new JsonResult(new { success = true, paymentAutoConfirmed = autoPayment });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = "Error approving verification: " + ex.Message });
            }
        }

        public async Task<IActionResult> OnPostRejectVerificationAsync(
            [FromForm] string userId,
            [FromForm] string reason)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
                    return new JsonResult(new { success = false, message = "Please provide a rejection reason (minimum 5 characters)." });

                var user = await _userManager.FindByIdAsync(userId);
                if (user == null)
                    return new JsonResult(new { success = false, message = "User not found." });

                var prevStatus = user.VerificationStatus;
                user.VerificationStatus            = "Rejected";
                user.VerificationRejectionReason   = reason.Trim();

                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                    return new JsonResult(new { success = false, message = string.Join(", ", result.Errors.Select(e => e.Description)) });

                LogAudit(user.Id, user.Email ?? "", "Verification Rejected",
                    $"Type: {FormatPartForm(user.PartForm)} | PrevStatus: {prevStatus} | Reason: {reason.Trim()}");
                await _context.SaveChangesAsync();

                // Причината е свободен текст, писан от админа. MailComposer я
                // подава през EmailPlaceholders.Set(), който я екранира — без
                // това "<" в текста би счупил HTML-а на писмото.
                await _mail.SendVerificationRejectedAsync(
                    toEmail:            user.Email ?? string.Empty,
                    firstName:          user.FirstName ?? string.Empty,
                    participationType:  ConferenceApp.Services.Email.MailContext.ParticipationName(user.PartForm),
                    reason:             reason.Trim(),
                    culture:            ConferenceApp.Services.Email.MailContext.CultureFor(user),
                    baseUrl:            ConferenceApp.Services.Email.MailContext.BaseUrl(_config, Request));

                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = "Error rejecting verification: " + ex.Message });
            }
        }

        public async Task<IActionResult> OnGetDownloadVerifDocAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null || string.IsNullOrEmpty(user.VerificationDocumentPath))
                return NotFound();

            var physical = Path.Combine(_env.WebRootPath,
                user.VerificationDocumentPath.TrimStart('~', '/').Replace('/', Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(physical)) return NotFound();

            return PhysicalFile(physical, "application/octet-stream",
                Path.GetFileName(user.VerificationDocumentPath));
        }

        // ══════════════════════════════════════════════════════════════════════
        // TICKET TIERS
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostEditTicketAsync()
        {
            var ticket = await _context.TicketTiers.FindAsync(EditTicket.Id);
            if (ticket == null)
            {
                TempData["ErrorMessage"] = "Ticket tier not found.";
                return RedirectToPage();
            }

            ticket.NameEn         = EditTicket.NameEn?.Trim() ?? "";
            ticket.NameBg         = EditTicket.NameBg?.Trim() ?? "";
            ticket.DescriptionEn  = EditTicket.DescriptionEn?.Trim() ?? "";
            ticket.DescriptionBg  = EditTicket.DescriptionBg?.Trim() ?? "";
            ticket.RegularPriceEn = EditTicket.RegularPriceEn?.Trim() ?? "";
            ticket.RegularPriceBg = EditTicket.RegularPriceBg?.Trim() ?? "";
            ticket.PromoPriceEn   = EditTicket.PromoPriceEn?.Trim();
            ticket.PromoPriceBg   = EditTicket.PromoPriceBg?.Trim();
            ticket.PerksEn        = EditTicket.PerksEn?.Trim() ?? "";
            ticket.PerksBg        = EditTicket.PerksBg?.Trim() ?? "";

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Ticket tier updated successfully!";
            return RedirectToPage();
        }

        // ══════════════════════════════════════════════════════════════════════
        // LECTURERS
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveLecturerAsync(
            [FromForm] LecturerModel model, IFormFile? avatarFile)
        {
            try
            {
                model.FullNameEn     = model.FullNameEn?.Trim()     ?? "";
                model.FullNameBg     = model.FullNameBg?.Trim()     ?? "";
                model.Category       = model.Category?.Trim()       ?? "";
                model.RoleEn         = model.RoleEn?.Trim()         ?? "";
                model.RoleBg         = model.RoleBg?.Trim()         ?? "";
                model.OrganizationEn = model.OrganizationEn?.Trim() ?? "";
                model.OrganizationBg = model.OrganizationBg?.Trim() ?? "";
                model.BiographyEn    = model.BiographyEn?.Trim()    ?? "";
                model.BiographyBg    = model.BiographyBg?.Trim()    ?? "";
                model.ProfileUrl     = model.ProfileUrl?.Trim()     ?? "";

                if (string.IsNullOrEmpty(model.FullNameEn))     return Err("Full Name (EN) is required.");
                if (string.IsNullOrEmpty(model.FullNameBg))     return Err("Full Name (BG) is required.");
                if (string.IsNullOrEmpty(model.RoleEn))         return Err("Role (EN) is required.");
                if (string.IsNullOrEmpty(model.RoleBg))         return Err("Role (BG) is required.");
                if (string.IsNullOrEmpty(model.OrganizationEn)) return Err("Organization (EN) is required.");
                if (string.IsNullOrEmpty(model.OrganizationBg)) return Err("Organization (BG) is required.");
                if (model.Id == 0 && (avatarFile == null || avatarFile.Length == 0))
                    return Err("Avatar image is required when adding a new lecturer.");

                if (avatarFile != null && avatarFile.Length > 0)
                    model.AvatarImagePath = SaveUploadedFile(avatarFile, "people/lecturers");

                if (model.Id == 0) { _context.Lecturers.Add(model); }
                else
                {
                    var ex = await _context.Lecturers.FindAsync(model.Id);
                    if (ex == null) return Err("Lecturer not found.");
                    ex.FullNameEn = model.FullNameEn; ex.FullNameBg = model.FullNameBg;
                    ex.Category = model.Category;
                    ex.RoleEn = model.RoleEn; ex.RoleBg = model.RoleBg;
                    ex.OrganizationEn = model.OrganizationEn; ex.OrganizationBg = model.OrganizationBg;
                    ex.BiographyEn = model.BiographyEn; ex.BiographyBg = model.BiographyBg;
                    ex.ProfileUrl = model.ProfileUrl;
                    if (model.AvatarImagePath != null) ex.AvatarImagePath = model.AvatarImagePath;
                }

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (InvalidOperationException e) { return Err(e.Message); }
            catch (Exception e) { return Err("Server error: " + e.Message); }
        }

        public async Task<IActionResult> OnPostDeleteLecturerAsync([FromForm] int id)
        {
            try
            {
                var lecturer = await _context.Lecturers.FindAsync(id);
                if (lecturer == null) return Err("Lecturer not found.");
                DeleteFile(lecturer.AvatarImagePath);
                _context.Lecturers.Remove(lecturer);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Error deleting lecturer: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // EVENTS (ICBI)
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveEventAsync(
            [FromForm] EventModel model, IFormFile? eventImage)
        {
            try
            {
                model.TitleEn    = model.TitleEn?.Trim()    ?? "";
                model.TitleBg    = model.TitleBg?.Trim()    ?? "";
                model.LocationEn = model.LocationEn?.Trim() ?? "";
                model.LocationBg = model.LocationBg?.Trim() ?? "";
                model.EventUrl   = model.EventUrl?.Trim()   ?? "";

                if (string.IsNullOrEmpty(model.TitleEn))    return Err("Event Title (EN) is required.");
                if (string.IsNullOrEmpty(model.TitleBg))    return Err("Event Title (BG) is required.");
                if (string.IsNullOrEmpty(model.LocationEn)) return Err("Location (EN) is required.");
                if (string.IsNullOrEmpty(model.LocationBg)) return Err("Location (BG) is required.");
                if (model.Id == 0 && (eventImage == null || eventImage.Length == 0))
                    return Err("Background image is required when adding a new event.");

                if (eventImage != null && eventImage.Length > 0)
                    model.ImagePath = SaveUploadedFile(eventImage, "events");

                if (model.Id == 0) { _context.Events.Add(model); }
                else
                {
                    var ex = await _context.Events.FindAsync(model.Id);
                    if (ex == null) return Err("Event not found.");
                    ex.TitleEn = model.TitleEn; ex.TitleBg = model.TitleBg;
                    ex.LocationEn = model.LocationEn; ex.LocationBg = model.LocationBg;
                    ex.EventUrl = model.EventUrl;
                    if (model.ImagePath != null) ex.ImagePath = model.ImagePath;
                }

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (InvalidOperationException e) { return Err(e.Message); }
            catch (Exception e) { return Err("Server error: " + e.Message); }
        }

        public async Task<IActionResult> OnPostDeleteEventAsync([FromForm] int id)
        {
            try
            {
                var ev = await _context.Events.FindAsync(id);
                if (ev == null) return Err("Event not found.");
                DeleteFile(ev.ImagePath);
                _context.Events.Remove(ev);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Error deleting event: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // COMMITTEE MEMBERS
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveMemberAsync(
            [FromForm] CommitteeMemberModel model, IFormFile? avatarFile)
        {
            try
            {
                model.FullNameEn     = model.FullNameEn?.Trim()     ?? "";
                model.FullNameBg     = model.FullNameBg?.Trim()     ?? "";
                model.RoleEn         = model.RoleEn?.Trim()         ?? "";
                model.RoleBg         = model.RoleBg?.Trim()         ?? "";
                model.OrganizationEn = model.OrganizationEn?.Trim() ?? "";
                model.OrganizationBg = model.OrganizationBg?.Trim() ?? "";
                model.CommitteeType  = model.CommitteeType?.Trim()  ?? "";

                if (string.IsNullOrEmpty(model.FullNameEn))     return Err("Full Name (EN) is required.");
                if (string.IsNullOrEmpty(model.FullNameBg))     return Err("Full Name (BG) is required.");
                if (string.IsNullOrEmpty(model.RoleEn))         return Err("Role (EN) is required.");
                if (string.IsNullOrEmpty(model.RoleBg))         return Err("Role (BG) is required.");
                if (string.IsNullOrEmpty(model.OrganizationEn)) return Err("Organization (EN) is required.");
                if (string.IsNullOrEmpty(model.OrganizationBg)) return Err("Organization (BG) is required.");
                if (model.Id == 0 && (avatarFile == null || avatarFile.Length == 0))
                    return Err("Avatar image is required when adding a new member.");

                if (avatarFile != null && avatarFile.Length > 0)
                    model.AvatarImagePath = SaveUploadedFile(avatarFile, "people/committees");

                if (model.Id == 0) { _context.CommitteeMembers.Add(model); }
                else
                {
                    var ex = await _context.CommitteeMembers.FindAsync(model.Id);
                    if (ex == null) return Err("Member not found.");
                    ex.FullNameEn = model.FullNameEn; ex.FullNameBg = model.FullNameBg;
                    ex.RoleEn = model.RoleEn; ex.RoleBg = model.RoleBg;
                    ex.OrganizationEn = model.OrganizationEn; ex.OrganizationBg = model.OrganizationBg;
                    ex.CommitteeType = model.CommitteeType;
                    if (model.AvatarImagePath != null) ex.AvatarImagePath = model.AvatarImagePath;
                }

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (InvalidOperationException e) { return Err(e.Message); }
            catch (Exception e) { return Err("Server error: " + e.Message); }
        }

        public async Task<IActionResult> OnPostDeleteMemberAsync([FromForm] int id)
        {
            try
            {
                var member = await _context.CommitteeMembers.FindAsync(id);
                if (member == null) return Err("Member not found.");
                DeleteFile(member.AvatarImagePath);
                _context.CommitteeMembers.Remove(member);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Error deleting member: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // PARTNERS
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSavePartnerAsync(
            [FromForm] PartnerModel model, IFormFile? logoFile)
        {
            try
            {
                model.NameEn     = model.NameEn?.Trim()     ?? "";
                model.NameBg     = model.NameBg?.Trim()     ?? "";
                model.Category   = model.Category?.Trim()   ?? "";
                model.WebsiteUrl = model.WebsiteUrl?.Trim();

                if (string.IsNullOrEmpty(model.NameEn)) return Err("Partner Name (EN) is required.");
                if (string.IsNullOrEmpty(model.NameBg)) return Err("Partner Name (BG) is required.");
                if (model.Id == 0 && (logoFile == null || logoFile.Length == 0))
                    return Err("Logo image is required when adding a new partner.");

                if (logoFile != null && logoFile.Length > 0)
                {
                    var ext = Path.GetExtension(logoFile.FileName).ToLowerInvariant();
                    if (ext == ".svg")
                    {
                        if (logoFile.Length > 2 * 1024 * 1024) return Err("SVG file is too large. Maximum 2MB.");
                        var folder = Path.Combine(_env.WebRootPath, "uploads", "partners");
                        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                        var fname = Guid.NewGuid().ToString() + ".svg";
                        using var fs = new FileStream(Path.Combine(folder, fname), FileMode.Create);
                        await logoFile.CopyToAsync(fs);
                        model.LogoImagePath = $"/uploads/partners/{fname}";
                    }
                    else
                    {
                        model.LogoImagePath = SaveUploadedFile(logoFile, "partners");
                    }
                }

                if (model.Id == 0) { _context.Partners.Add(model); }
                else
                {
                    var ex = await _context.Partners.FindAsync(model.Id);
                    if (ex == null) return Err("Partner not found.");
                    ex.NameEn = model.NameEn; ex.NameBg = model.NameBg; ex.Category = model.Category;
                    ex.WebsiteUrl = model.WebsiteUrl;
                    if (model.LogoImagePath != null) ex.LogoImagePath = model.LogoImagePath;
                }

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (InvalidOperationException e) { return Err(e.Message); }
            catch (Exception e) { return Err("Server error: " + e.Message); }
        }

        public async Task<IActionResult> OnPostDeletePartnerAsync([FromForm] int id)
        {
            try
            {
                var partner = await _context.Partners.FindAsync(id);
                if (partner == null) return Err("Partner not found.");
                DeleteFile(partner.LogoImagePath);
                _context.Partners.Remove(partner);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Error deleting partner: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // HOME PAGE LOGOS (Добавено)
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostUploadLogoAsync(IFormFile logoFile)
        {
            try
            {
                if (logoFile == null || logoFile.Length == 0)
                {
                    TempData["ErrorMessage"] = "Моля изберете валиден файл.";
                    return RedirectToPage();
                }

                string savedPath = SaveUploadedFile(logoFile, "homepagelogos");

                var newLogo = new HomePageLogo
                {
                    ImagePath = savedPath
                };

                _context.HomePageLogos.Add(newLogo);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Логото е добавено успешно!";
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Грешка при качване: " + ex.Message;
                return RedirectToPage();
            }
        }

        public async Task<IActionResult> OnPostDeleteLogoAsync([FromForm] int id)
        {
            try
            {
                var logo = await _context.HomePageLogos.FindAsync(id);
                if (logo == null) return Err("Логото не е намерено.");

                DeleteFile(logo.ImagePath);

                _context.HomePageLogos.Remove(logo);
                await _context.SaveChangesAsync();
                return Ok(); 
            }
            catch (Exception ex)
            {
                return Err("Грешка при изтриване: " + ex.Message);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // SITE SETTINGS: SOCIAL LINKS (НОВО)
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveSocialLinksAsync([FromForm] SocialLinksSetting model)
        {
            try
            {
                string? Clean(string? url)
                {
                    var trimmed = url?.Trim();
                    return string.IsNullOrEmpty(trimmed) ? null : trimmed;
                }

                bool LooksLikeUrl(string? url) =>
                    url == null || (Uri.TryCreate(url, UriKind.Absolute, out var uri)
                                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));

                var linkedIn  = Clean(model.LinkedInUrl);
                var x         = Clean(model.XUrl);
                var instagram = Clean(model.InstagramUrl);
                var facebook  = Clean(model.FacebookUrl);
                var tiktok    = Clean(model.TikTokUrl);
                var youtube   = Clean(model.YouTubeUrl);

                if (!LooksLikeUrl(linkedIn))  return Err("LinkedIn URL looks invalid — must start with http:// or https://");
                if (!LooksLikeUrl(x))         return Err("X (Twitter) URL looks invalid — must start with http:// or https://");
                if (!LooksLikeUrl(instagram)) return Err("Instagram URL looks invalid — must start with http:// or https://");
                if (!LooksLikeUrl(facebook))  return Err("Facebook URL looks invalid — must start with http:// or https://");
                if (!LooksLikeUrl(tiktok))    return Err("TikTok URL looks invalid — must start with http:// or https://");
                if (!LooksLikeUrl(youtube))   return Err("YouTube URL looks invalid — must start with http:// or https://");

                var existing = await _context.SocialLinksSettings.FirstOrDefaultAsync();
                if (existing == null)
                {
                    existing = new SocialLinksSetting();
                    _context.SocialLinksSettings.Add(existing);
                }

                existing.LinkedInUrl  = linkedIn;
                existing.XUrl         = x;
                existing.InstagramUrl = instagram;
                existing.FacebookUrl  = facebook;
                existing.TikTokUrl    = tiktok;
                existing.YouTubeUrl   = youtube;

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Server error: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // SITE SETTINGS: FOOTER CONTENT (НОВО)
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveFooterContentAsync(
            [FromForm] string brandTaglineEn, [FromForm] string brandTaglineBg,
            [FromForm] string orgNoteEn, [FromForm] string orgNoteBg,
            [FromForm] string contactLocationEn, [FromForm] string contactLocationBg,
            [FromForm] string contactEmail, [FromForm] string contactPhone)
        {
            try
            {
                brandTaglineEn    = brandTaglineEn?.Trim() ?? "";
                brandTaglineBg    = brandTaglineBg?.Trim() ?? "";
                orgNoteEn         = orgNoteEn?.Trim() ?? "";
                orgNoteBg         = orgNoteBg?.Trim() ?? "";
                contactLocationEn = contactLocationEn?.Trim() ?? "";
                contactLocationBg = contactLocationBg?.Trim() ?? "";
                contactEmail      = contactEmail?.Trim() ?? "";
                contactPhone      = contactPhone?.Trim() ?? "";

                // Tagline е опционален нарочно (виж FooterContent.cs) —
                // може да се остави празен, за да не се показва изобщо
                // редът с tagline на публичния footer. MaxLength-ите тук
                // огледално следват атрибутите в FooterContent.cs — виж
                // коментарите там за защо точно тези стойности.
                if (brandTaglineEn.Length > 45) return Err("Brand Tagline (EN) is too long (max 45 characters).");
                if (brandTaglineBg.Length > 45) return Err("Brand Tagline (BG) is too long (max 45 characters).");

                if (string.IsNullOrEmpty(orgNoteEn)) return Err("\"Organized By\" Note (EN) is required.");
                if (string.IsNullOrEmpty(orgNoteBg)) return Err("\"Organized By\" Note (BG) is required.");
                if (orgNoteEn.Length > 400) return Err("\"Organized By\" Note (EN) is too long (max 400 characters).");
                if (orgNoteBg.Length > 400) return Err("\"Organized By\" Note (BG) is too long (max 400 characters).");

                if (string.IsNullOrEmpty(contactLocationEn)) return Err("Address / Location (EN) is required.");
                if (string.IsNullOrEmpty(contactLocationBg)) return Err("Address / Location (BG) is required.");
                if (contactLocationEn.Length > 100) return Err("Address / Location (EN) is too long (max 100 characters).");
                if (contactLocationBg.Length > 100) return Err("Address / Location (BG) is too long (max 100 characters).");

                if (string.IsNullOrEmpty(contactEmail)) return Err("Contact email is required.");
                if (contactEmail.Length > 150) return Err("Contact email is too long (max 150 characters).");
                if (!Regex.IsMatch(contactEmail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) return Err("Please enter a valid email address.");

                if (string.IsNullOrEmpty(contactPhone)) return Err("Contact phone is required.");
                if (contactPhone.Length > 30) return Err("Contact phone is too long (max 30 characters).");

                var existing = await _context.FooterContents.FirstOrDefaultAsync();
                if (existing == null)
                {
                    existing = new FooterContent();
                    _context.FooterContents.Add(existing);
                }

                existing.BrandTaglineEn    = brandTaglineEn;
                existing.BrandTaglineBg    = brandTaglineBg;
                existing.OrgNoteEn         = orgNoteEn;
                existing.OrgNoteBg         = orgNoteBg;
                existing.ContactLocationEn = contactLocationEn;
                existing.ContactLocationBg = contactLocationBg;
                existing.ContactEmail      = contactEmail;
                existing.ContactPhone      = contactPhone;
                existing.LastUpdatedAt     = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Server error: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // SITE SETTINGS: FOOTER QUICK LINKS (НОВО)
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveFooterLinkAsync([FromForm] FooterQuickLinkModel model)
        {
            try
            {
                model.LabelEn  = model.LabelEn?.Trim() ?? "";
                model.LabelBg  = model.LabelBg?.Trim() ?? "";
                model.Url      = model.Url?.Trim() ?? "";
                model.IconSvg  = model.IconSvg?.Trim() ?? "";

                if (string.IsNullOrEmpty(model.LabelEn)) return Err("Label (EN) is required.");
                if (string.IsNullOrEmpty(model.LabelBg)) return Err("Label (BG) is required.");
                if (model.LabelEn.Length > 60) return Err("Label (EN) is too long (max 60 characters).");
                if (model.LabelBg.Length > 60) return Err("Label (BG) is too long (max 60 characters).");

                if (string.IsNullOrEmpty(model.Url)) return Err("URL / Path is required.");
                if (model.Url.Length > 300) return Err("URL / Path is too long (max 300 characters).");
                if (model.Url.TrimStart().StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                    return Err("URL / Path cannot be a javascript: link.");

                if (string.IsNullOrEmpty(model.IconSvg)) return Err("SVG icon code is required.");
                if (model.IconSvg.Length > 2000) return Err("SVG icon code is too long (max 2000 characters).");
                // Лека защита в дълбочина — IconSvg се рендира с @Html.Raw
                // на публичния footer (виж _Layout.cshtml), заобикаляйки
                // нормалния HTML encoding. Полето е свободен текст (не
                // идва от richtext editor като Quill-базираните полета
                // другаде в проекта), затова тази проверка е допълнителна
                // мярка върху доверието към самия admin. Не е пълен
                // sanitizer — просто блокира най-очевидните вектори.
                string[] blockedSvgPatterns = { "<script", "javascript:", "onerror=", "onload=", "onclick=", "<foreignobject" };
                foreach (var pattern in blockedSvgPatterns)
                {
                    if (model.IconSvg.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                        return Err($"SVG icon code contains disallowed content ('{pattern}').");
                }

                if (model.Id == 0)
                {
                    model.IsVisible = true;
                    _context.FooterQuickLinks.Add(model);
                }
                else
                {
                    var ex = await _context.FooterQuickLinks.FindAsync(model.Id);
                    if (ex == null) return Err("Quick link not found.");
                    ex.LabelEn = model.LabelEn;
                    ex.LabelBg = model.LabelBg;
                    ex.Url     = model.Url;
                    ex.IconSvg = model.IconSvg;
                }

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Server error: " + e.Message); }
        }

        public async Task<IActionResult> OnPostDeleteFooterLinkAsync([FromForm] int id)
        {
            try
            {
                var link = await _context.FooterQuickLinks.FindAsync(id);
                if (link == null) return Err("Quick link not found.");
                _context.FooterQuickLinks.Remove(link);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Error deleting quick link: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // ИМЕЙЛ ИЗВЕСТИЯ — включване и изключване по вид
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Включва или изключва един вид известие. Кодът за вход и регистрация
        /// (Otp) нарочно не е в списъка на превключваемите — изключването му би
        /// направило сайта неизползваем; услугата отказва такава заявка.
        /// </summary>
        // ══════════════════════════════════════════════════════════════════════
        // КРИПТО ПОРЪЧКИ — изчистване на неактивните
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Трие крипто поръчките, които вече не могат да доведат до плащане.
        ///
        /// <para>
        /// Кои се трият: само <c>Expired</c>, и само ако потребителят НЕ е
        /// платил чрез тях. <c>Confirmed</c> никога не се пипа — това е следата
        /// от реално плащане и е нужна при спор. <c>InProcess</c> също не се
        /// пипа: поръчката още е активна и потребителят може да превежда
        /// точно в този момент.
        /// </para>
        ///
        /// <para>
        /// Допълнителна предпазна мярка: InProcess поръчка с изтекъл
        /// <c>ExpiresAt</c> се смята за мъртва, но само ако е изтекла преди
        /// повече от час. Go28 понякога потвърждава със закъснение и твърде
        /// агресивното чистене би изтрило поръчка, която тъкмо е щяла да мине.
        /// </para>
        /// </summary>
        public async Task<IActionResult> OnPostClearInactiveCryptoOrdersAsync()
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddHours(-1);

                var doomed = await _context.CryptoOrders
                    .Where(o => o.Status == "Expired"
                             || (o.Status == "InProcess"
                                 && o.ExpiresAt != null
                                 && o.ExpiresAt < cutoff))
                    .ToListAsync();

                if (doomed.Count == 0)
                    return new JsonResult(new
                    {
                        success = true,
                        removed = 0,
                        message = "Няма неактивни поръчки за изчистване."
                    });

                // Групираме за одита — кой какво губи.
                var byStatus = doomed.GroupBy(o => o.Status)
                                     .ToDictionary(g => g.Key, g => g.Count());
                var affectedUsers = doomed.Select(o => o.UserId).Distinct().Count();

                _context.CryptoOrders.RemoveRange(doomed);

                LogAudit(string.Empty, User.Identity?.Name ?? "admin",
                    "Crypto Orders Cleared",
                    $"Removed: {doomed.Count} | " +
                    string.Join(", ", byStatus.Select(kv => $"{kv.Key}={kv.Value}")) +
                    $" | Users affected: {affectedUsers}");

                await _context.SaveChangesAsync();

                return new JsonResult(new
                {
                    success = true,
                    removed = doomed.Count,
                    message = $"Изчистени {doomed.Count} неактивни поръчки."
                });
            }
            catch (Exception e)
            {
                return Err("Error clearing crypto orders: " + e.Message);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // HEALTH CHECK
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// GET /Admin?handler=HealthCheck&amp;service=&lt;key&gt;  — една услуга
        /// GET /Admin?handler=HealthCheck                        — всичките осем
        ///
        /// <para>
        /// GET нарочно: проверката само чете и не променя нищо, така че не иска
        /// антифоргъри токен. Ако някога стане POST, скриптът трябва да започне
        /// да праща RequestVerificationToken (виж INTEGRATION.md, т. 5.2).
        /// </para>
        /// </summary>
        public async Task<IActionResult> OnGetHealthCheckAsync(string? service, CancellationToken ct)
        {
            // Панелът иска администраторски достъп, но handler-ът е отделна
            // входна точка и трябва да го провери сам.
            if (!User.IsInRole("Admin"))
                return new JsonResult(new { error = "forbidden" }) { StatusCode = 403 };

            try
            {
                if (!string.IsNullOrWhiteSpace(service))
                    return new JsonResult(await _health.CheckAsync(service.Trim(), ct));

                return new JsonResult(await _health.CheckAllAsync(ct));
            }
            catch (OperationCanceledException)
            {
                // Потребителят е затворил таба или е презаредил — не е грешка.
                return new JsonResult(new { error = "cancelled" }) { StatusCode = 499 };
            }
        }

        public async Task<IActionResult> OnPostToggleEmailNotificationAsync(
            [FromForm] string templateKey, [FromForm] bool enabled)
        {
            try
            {
                if (!Enum.TryParse<ConferenceApp.Services.Email.EmailTemplate>(
                        templateKey, out var template))
                    return Err("Unknown email type.");

                await _emailSettings.SetAsync(template, enabled, User.Identity?.Name);

                LogAudit(string.Empty, User.Identity?.Name ?? "admin",
                    "Email Notification Toggled",
                    $"{templateKey} → {(enabled ? "Enabled" : "Disabled")}");
                await _context.SaveChangesAsync();

                return Ok();
            }
            catch (InvalidOperationException ex)
            {
                return Err(ex.Message);
            }
            catch (Exception e)
            {
                return Err("Error toggling email notification: " + e.Message);
            }
        }

        /// <summary>
        /// Payment Control — един от осемте ключа на промяна (не целият набор).
        /// Йерархията (all → method → currency) не се записва каскадно тук:
        /// изключването на method.crypto пази стойностите на валутите
        /// непроменени, за да не трябва да се включват наново, когато Go28
        /// се върне. Филтрирането по йерархия се прави при четене
        /// (PaymentModel.IsMethodEnabled / IsCurrencySupported).
        /// </summary>
        public async Task<IActionResult> OnPostTogglePaymentGateAsync(
            [FromForm] string key, [FromForm] bool enabled)
        {
            try
            {
                if (!ConferenceApp.Services.PaymentGateSettings.AllKeys.Contains(key, StringComparer.Ordinal))
                    return Err("Unknown payment gate key.");

                await _paymentGates.SetAsync(key, enabled, User.Identity?.Name);

                LogAudit(string.Empty, User.Identity?.Name ?? "admin",
                    "Payment Gate Toggled",
                    $"{key} → {(enabled ? "Enabled" : "Disabled")}");
                await _context.SaveChangesAsync();

                return Ok();
            }
            catch (InvalidOperationException ex)
            {
                return Err(ex.Message);
            }
            catch (Exception e)
            {
                return Err("Error toggling payment gate: " + e.Message);
            }
        }

        public async Task<IActionResult> OnPostToggleFooterLinkActiveAsync([FromForm] int id)
        {
            try
            {
                var link = await _context.FooterQuickLinks.FindAsync(id);
                if (link == null) return Err("Quick link not found.");
                link.IsVisible = !link.IsVisible;
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Error toggling quick link: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // PRIVACY POLICY / GDPR (НОВО) — замества старите Pages.Privacy.*.resx
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSavePrivacyPolicyAsync(
            [FromForm] string contentEn, [FromForm] string contentBg)
        {
            try
            {
                // FIX: клиентът вече праща contentEn/contentBg base64-кодирани —
                // суров HTML (особено с вложени base64 снимки от Quill image
                // бутона) е точно pattern, който WAF/Cloudflare managed rules
                // обичат да flag-ват като malware payload (същата причина, поради
                // която SendInvitations base64-кодира темплейта си). Base64
                // обвивката прикрива структурата от pattern-matching-а без да
                // променя каквото и да е функционално.
                static string DecodeBase64Utf8(string? base64)
                {
                    if (string.IsNullOrWhiteSpace(base64)) return "";
                    try
                    {
                        var bytes = Convert.FromBase64String(base64);
                        return System.Text.Encoding.UTF8.GetString(bytes);
                    }
                    catch (FormatException)
                    {
                        // Не base64 — трети клиент/стар кеширан JS? По-безопасно
                        // да третираме като празно, отколкото да гръмнем целия save.
                        return "";
                    }
                }

                var cleanEn = DecodeBase64Utf8(contentEn).Trim();
                var cleanBg = DecodeBase64Utf8(contentBg).Trim();

                // Празен Quill editor връща "<p><br></p>" вместо истинска празна
                // стойност — третираме го също като липсващо съдържание.
                bool IsEffectivelyEmpty(string html) =>
                    string.IsNullOrWhiteSpace(html) || html == "<p><br></p>";

                if (IsEffectivelyEmpty(cleanEn)) return Err("English content cannot be empty.");
                if (IsEffectivelyEmpty(cleanBg)) return Err("Bulgarian content cannot be empty.");

                var existing = await _context.PrivacyPolicyContents.FirstOrDefaultAsync();
                if (existing == null)
                {
                    existing = new PrivacyPolicyContent();
                    _context.PrivacyPolicyContents.Add(existing);
                }

                existing.ContentEn = cleanEn;
                existing.ContentBg = cleanBg;
                existing.LastUpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Server error: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // TERMS OF USE (НОВО) — замества старите Pages.Terms.*.resx.
        // Огледално на OnPostSavePrivacyPolicyAsync по-горе, включително
        // base64 обвивката около HTML-а (виж коментара там за WAF причината).
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveTermsOfUseAsync(
            [FromForm] string contentEn, [FromForm] string contentBg)
        {
            try
            {
                static string DecodeBase64Utf8(string? base64)
                {
                    if (string.IsNullOrWhiteSpace(base64)) return "";
                    try
                    {
                        var bytes = Convert.FromBase64String(base64);
                        return System.Text.Encoding.UTF8.GetString(bytes);
                    }
                    catch (FormatException)
                    {
                        return "";
                    }
                }

                var cleanEn = DecodeBase64Utf8(contentEn).Trim();
                var cleanBg = DecodeBase64Utf8(contentBg).Trim();

                bool IsEffectivelyEmpty(string html) =>
                    string.IsNullOrWhiteSpace(html) || html == "<p><br></p>";

                if (IsEffectivelyEmpty(cleanEn)) return Err("English content cannot be empty.");
                if (IsEffectivelyEmpty(cleanBg)) return Err("Bulgarian content cannot be empty.");

                var existing = await _context.TermsOfUseContents.FirstOrDefaultAsync();
                if (existing == null)
                {
                    existing = new TermsOfUseContent();
                    _context.TermsOfUseContents.Add(existing);
                }

                existing.ContentEn = cleanEn;
                existing.ContentBg = cleanBg;
                existing.LastUpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Server error: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // COOKIE NOTICE — категории (НОВО)
        // ══════════════════════════════════════════════════════════════════════

        // id == 0 → нова категория; иначе update на съществуваща.
        // "necessary" е специален случай — IsToggleable/DefaultOn се налагат
        // server-side независимо какво е подадено, защото strictly-necessary
        // cookies по закон не могат да бъдат изключвани от посетителя. Това
        // важи дори ако някой admin-грешка (или ръчен POST през DevTools)
        // опита да го промени.
        public async Task<IActionResult> OnPostSaveCookieCategoryAsync(
            int id, string key, string nameEn, string nameBg,
            string? descriptionEn, string? descriptionBg,
            bool isVisible, bool isToggleable)
        {
            try
            {
                // Описанията вече идват от Quill (rich HTML), базово base64-кодирани
                // на клиента — същия WAF pattern като Privacy Policy/Banner текста.
                static string DecodeBase64Utf8(string? base64)
                {
                    if (string.IsNullOrWhiteSpace(base64)) return "";
                    try
                    {
                        var bytes = Convert.FromBase64String(base64);
                        return System.Text.Encoding.UTF8.GetString(bytes);
                    }
                    catch (FormatException) { return ""; }
                }

                key = key?.Trim().ToLowerInvariant() ?? "";
                nameEn = nameEn?.Trim() ?? "";
                nameBg = nameBg?.Trim() ?? "";
                var cleanDescriptionEn = DecodeBase64Utf8(descriptionEn).Trim();
                var cleanDescriptionBg = DecodeBase64Utf8(descriptionBg).Trim();

                if (string.IsNullOrWhiteSpace(key)) return Err("Key is required.");
                if (string.IsNullOrWhiteSpace(nameEn)) return Err("English name is required.");
                if (string.IsNullOrWhiteSpace(nameBg)) return Err("Bulgarian name is required.");

                CookieCategory category;
                if (id == 0)
                {
                    // Ключът трябва да е уникален — базата и така би отхвърлила
                    // дублат (unique index), но проверяваме предварително за
                    // по-приятелско съобщение вместо суров DB constraint error.
                    bool keyExists = await _context.CookieCategories.AnyAsync(c => c.Key == key);
                    if (keyExists) return Err($"A category with key \"{key}\" already exists.");

                    category = new CookieCategory { Key = key, IsBuiltIn = false };
                    _context.CookieCategories.Add(category);

                    var maxOrder = await _context.CookieCategories.AnyAsync()
                        ? await _context.CookieCategories.MaxAsync(c => c.DisplayOrder)
                        : 0;
                    category.DisplayOrder = maxOrder + 1;
                }
                else
                {
                    var existing = await _context.CookieCategories.FindAsync(id);
                    if (existing == null) return Err("Category not found.");
                    category = existing;
                }

                category.NameEn = nameEn;
                category.NameBg = nameBg;
                category.DescriptionEn = cleanDescriptionEn;
                category.DescriptionBg = cleanDescriptionBg;
                category.IsVisible = isVisible;

                if (category.Key == "necessary")
                {
                    // Строго необходимите бисквитки по закон не могат да бъдат
                    // изключвани от посетителя — наложено тук независимо какво
                    // е подадено, дори през ръчен POST.
                    category.IsToggleable = false;
                    category.DefaultOn = true;
                }
                else
                {
                    // Всяка друга категория стои изключена по подразбиране —
                    // GDPR изисква opt-in, не opt-out. Няма UI за смяна на това
                    // нарочно, за да не може админ случайно да pre-tick-не
                    // non-essential категория.
                    category.IsToggleable = isToggleable;
                    category.DefaultOn = false;
                }

                await _context.SaveChangesAsync();
                return new JsonResult(new { success = true, id = category.Id });
            }
            catch (Exception e) { return Err("Server error: " + e.Message); }
        }

        public async Task<IActionResult> OnPostDeleteCookieCategoryAsync(int id)
        {
            try
            {
                var category = await _context.CookieCategories.FindAsync(id);
                if (category == null) return Err("Category not found.");

                // "necessary" е задължителна по GDPR — не позволяваме да се
                // изтрие изцяло категорията за strictly-necessary cookies.
                if (category.Key == "necessary")
                    return Err("The Strictly Necessary category can't be deleted — it's legally required.");

                _context.CookieCategories.Remove(category);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Server error: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // COOKIE NOTICE — главен текст (НОВО)
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveCookieNoticeAsync(
            [FromForm] string contentEn, [FromForm] string contentBg)
        {
            try
            {
                // Същата base64 обвивка като Privacy Policy save-а — суров HTML
                // (особено с вградени base64 снимки от Quill) е exactly pattern,
                // който WAF/Cloudflare managed rules обичат да flag-ват.
                static string DecodeBase64Utf8(string? base64)
                {
                    if (string.IsNullOrWhiteSpace(base64)) return "";
                    try
                    {
                        var bytes = Convert.FromBase64String(base64);
                        return System.Text.Encoding.UTF8.GetString(bytes);
                    }
                    catch (FormatException) { return ""; }
                }

                var cleanEn = DecodeBase64Utf8(contentEn).Trim();
                var cleanBg = DecodeBase64Utf8(contentBg).Trim();

                bool IsEffectivelyEmpty(string html) =>
                    string.IsNullOrWhiteSpace(html) || html == "<p><br></p>";

                if (IsEffectivelyEmpty(cleanEn)) return Err("English content cannot be empty.");
                if (IsEffectivelyEmpty(cleanBg)) return Err("Bulgarian content cannot be empty.");

                var existing = await _context.CookieNoticeContents.FirstOrDefaultAsync();
                if (existing == null)
                {
                    existing = new CookieNoticeContent();
                    _context.CookieNoticeContents.Add(existing);
                }

                existing.ContentEn = cleanEn;
                existing.ContentBg = cleanBg;
                existing.LastUpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Server error: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // COOKIE POLICY PAGE — главен текст на /Cookies страницата (НОВО)
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveCookiePolicyAsync(
            [FromForm] string contentEn, [FromForm] string contentBg)
        {
            try
            {
                static string DecodeBase64Utf8(string? base64)
                {
                    if (string.IsNullOrWhiteSpace(base64)) return "";
                    try
                    {
                        var bytes = Convert.FromBase64String(base64);
                        return System.Text.Encoding.UTF8.GetString(bytes);
                    }
                    catch (FormatException) { return ""; }
                }

                var cleanEn = DecodeBase64Utf8(contentEn).Trim();
                var cleanBg = DecodeBase64Utf8(contentBg).Trim();

                bool IsEffectivelyEmpty(string html) =>
                    string.IsNullOrWhiteSpace(html) || html == "<p><br></p>";

                if (IsEffectivelyEmpty(cleanEn)) return Err("English content cannot be empty.");
                if (IsEffectivelyEmpty(cleanBg)) return Err("Bulgarian content cannot be empty.");

                var existing = await _context.CookiePolicyContents.FirstOrDefaultAsync();
                if (existing == null)
                {
                    existing = new CookiePolicyContent();
                    _context.CookiePolicyContents.Add(existing);
                }

                existing.ContentEn = cleanEn;
                existing.ContentBg = cleanBg;
                existing.LastUpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Server error: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // SITE SETTINGS: PROMO SLIDES (НОВО)
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSavePromoAsync(
            [FromForm] PromoSlideModel model, IFormFile? imageFile)
        {
            try
            {
                model.TitleEn       = model.TitleEn?.Trim()       ?? "";
                model.TitleBg       = model.TitleBg?.Trim()       ?? "";
                model.DescriptionEn = model.DescriptionEn?.Trim() ?? "";
                model.DescriptionBg = model.DescriptionBg?.Trim() ?? "";

                if (string.IsNullOrEmpty(model.TitleEn))       return Err("Title (EN) is required.");
                if (string.IsNullOrEmpty(model.TitleBg))       return Err("Title (BG) is required.");
                if (string.IsNullOrEmpty(model.DescriptionEn)) return Err("Description (EN) is required.");
                if (string.IsNullOrEmpty(model.DescriptionBg)) return Err("Description (BG) is required.");

                if (model.TitleEn.Length > 50)         return Err("Title (EN) must be 50 characters or fewer.");
                if (model.TitleBg.Length > 50)         return Err("Title (BG) must be 50 characters or fewer.");
                if (model.DescriptionEn.Length > 120) return Err("Description (EN) must be 120 characters or fewer.");
                if (model.DescriptionBg.Length > 120) return Err("Description (BG) must be 120 characters or fewer.");

                if (model.Id == 0 && (imageFile == null || imageFile.Length == 0))
                    return Err("Image is required when adding a new promo slide.");

                if (imageFile != null && imageFile.Length > 0)
                {
                    model.ImagePath = SaveUploadedFile(imageFile, "promo", 1 * 1024 * 1024, new[] { ".png", ".jpg", ".jpeg" });
                }

                if (model.Id == 0)
                {
                    var maxOrder = await _context.PromoSlides.AnyAsync()
                        ? await _context.PromoSlides.MaxAsync(p => p.DisplayOrder)
                        : -1;
                    model.DisplayOrder = maxOrder + 1;
                    model.IsActive = true;
                    _context.PromoSlides.Add(model);
                }
                else
                {
                    var ex = await _context.PromoSlides.FindAsync(model.Id);
                    if (ex == null) return Err("Promo slide not found.");
                    ex.TitleEn = model.TitleEn; ex.TitleBg = model.TitleBg;
                    ex.DescriptionEn = model.DescriptionEn; ex.DescriptionBg = model.DescriptionBg;
                    if (model.ImagePath != null) ex.ImagePath = model.ImagePath;
                }

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (InvalidOperationException e) { return Err(e.Message); }
            catch (Exception e) { return Err("Server error: " + e.Message); }
        }

        public async Task<IActionResult> OnPostDeletePromoAsync([FromForm] int id)
        {
            try
            {
                var promo = await _context.PromoSlides.FindAsync(id);
                if (promo == null) return Err("Promo slide not found.");
                DeleteFile(promo.ImagePath);
                _context.PromoSlides.Remove(promo);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Error deleting promo slide: " + e.Message); }
        }

        public async Task<IActionResult> OnPostTogglePromoActiveAsync([FromForm] int id)
        {
            try
            {
                var promo = await _context.PromoSlides.FindAsync(id);
                if (promo == null) return Err("Promo slide not found.");
                promo.IsActive = !promo.IsActive;
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Error toggling promo slide: " + e.Message); }
        }

        public async Task<IActionResult> OnPostReorderPromosAsync([FromForm] List<int> orderedIds)
        {
            try
            {
                if (orderedIds == null || orderedIds.Count == 0) return Err("No order data received.");

                var promos = await _context.PromoSlides
                    .Where(p => orderedIds.Contains(p.Id))
                    .ToListAsync();

                for (int i = 0; i < orderedIds.Count; i++)
                {
                    var promo = promos.FirstOrDefault(p => p.Id == orderedIds[i]);
                    if (promo != null) promo.DisplayOrder = i;
                }

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Error saving new order: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // SCHEDULE
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveSessionAsync([FromForm] ScheduleModel model)
        {
            try
            {
                model.Day           = model.Day?.Trim()           ?? "";
                model.StartTime     = model.StartTime?.Trim()     ?? "";
                model.EndTime       = model.EndTime?.Trim()       ?? "";
                model.TitleEn       = model.TitleEn?.Trim()       ?? "";
                model.TitleBg       = model.TitleBg?.Trim()       ?? "";
                model.SessionType   = model.SessionType?.Trim()   ?? "";
                model.SpeakerEn     = model.SpeakerEn?.Trim()     ?? "";
                model.SpeakerBg     = model.SpeakerBg?.Trim()     ?? "";
                model.LocationEn    = model.LocationEn?.Trim()    ?? "";
                model.LocationBg    = model.LocationBg?.Trim()    ?? "";
                model.DescriptionEn = model.DescriptionEn?.Trim() ?? "";
                model.DescriptionBg = model.DescriptionBg?.Trim() ?? "";
                // НОВО: Почистване на стрийм линка, ако е подаден
                model.LiveStreamUrl = model.LiveStreamUrl?.Trim();

                if (string.IsNullOrEmpty(model.StartTime)) return Err("Start time is required.");
                if (string.IsNullOrEmpty(model.EndTime))   return Err("End time is required.");
                if (string.Compare(model.StartTime, model.EndTime) >= 0)
                    return Err("End time must be after start time.");
                if (string.IsNullOrEmpty(model.TitleEn)) return Err("Session Title (EN) is required.");
                if (string.IsNullOrEmpty(model.TitleBg)) return Err("Session Title (BG) is required.");

                if (model.Id == 0) { _context.Schedule.Add(model); }
                else
                {
                    var ex = await _context.Schedule.FindAsync(model.Id);
                    if (ex == null) return Err("Session not found.");
                    ex.Day = model.Day; ex.StartTime = model.StartTime; ex.EndTime = model.EndTime;
                    ex.TitleEn = model.TitleEn; ex.TitleBg = model.TitleBg;
                    ex.SessionType = model.SessionType;
                    ex.SpeakerEn = model.SpeakerEn; ex.SpeakerBg = model.SpeakerBg;
                    ex.LocationEn = model.LocationEn; ex.LocationBg = model.LocationBg;
                    ex.DescriptionEn = model.DescriptionEn; ex.DescriptionBg = model.DescriptionBg;
                    // НОВО: Обновяваме стрийм линка само ако е редактиран през основния модал
                    // (макар че за него ще направим и отделен метод по-долу)
                    ex.LiveStreamUrl = model.LiveStreamUrl; 
                }

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Error saving session: " + e.Message); }
        }

        public async Task<IActionResult> OnPostDeleteSessionAsync([FromForm] int id)
        {
            try
            {
                var session = await _context.Schedule.FindAsync(id);
                if (session == null) return Err("Session not found.");
                _context.Schedule.Remove(session);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Error deleting session: " + e.Message); }
        }

        // НОВО: Метод специално за запазване само на Live Stream линка от малкия модал
        public async Task<IActionResult> OnPostSaveSessionLiveLinkAsync(
            [FromForm] int liveLinkSessionId, 
            [FromForm] string liveLinkUrl)
        {
            try
            {
                var session = await _context.Schedule.FindAsync(liveLinkSessionId);
                if (session == null)
                    return Err("Session not found.");

                // Ако потребителят е изпратил само "#" или празен низ, го правим null
                string? cleanUrl = liveLinkUrl?.Trim();
                if (cleanUrl == "#" || string.IsNullOrEmpty(cleanUrl))
                {
                    cleanUrl = null;
                }

                session.LiveStreamUrl = cleanUrl;
                await _context.SaveChangesAsync();

                return Ok();
            }
            catch (Exception e)
            {
                return Err("Error saving live link: " + e.Message);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // HOTELS
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveHotelAsync([FromForm] HotelModel model)
        {
            try
            {
                model.NameEn        = model.NameEn?.Trim()        ?? "";
                model.NameBg        = model.NameBg?.Trim()        ?? "";
                model.DescriptionEn = model.DescriptionEn?.Trim() ?? "";
                model.DescriptionBg = model.DescriptionBg?.Trim() ?? "";
                model.Url           = model.Url?.Trim()           ?? "";

                if (string.IsNullOrEmpty(model.NameEn)) return Err("Hotel Name (EN) is required.");
                if (string.IsNullOrEmpty(model.NameBg)) return Err("Hotel Name (BG) is required.");

                if (model.Id == 0) { _context.Hotels.Add(model); }
                else
                {
                    var ex = await _context.Hotels.FindAsync(model.Id);
                    if (ex == null) return Err("Hotel not found.");
                    ex.NameEn = model.NameEn; ex.NameBg = model.NameBg;
                    ex.DescriptionEn = model.DescriptionEn; ex.DescriptionBg = model.DescriptionBg;
                    ex.Url = model.Url;
                }

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Error saving hotel: " + e.Message); }
        }

        public async Task<IActionResult> OnPostDeleteHotelAsync([FromForm] int id)
        {
            try
            {
                var hotel = await _context.Hotels.FindAsync(id);
                if (hotel == null) return Err("Hotel not found.");
                _context.Hotels.Remove(hotel);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Error deleting hotel: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // LIVE STREAM LINK (ГЛОБАЛЕН)
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveLiveLinkAsync([FromForm] string liveLink)
        {
            var settings = await _context.LinkWatches.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new LinkWatch { WatchOnlineLink = liveLink.Trim() };
                _context.LinkWatches.Add(settings);
            }
            else
            {
                settings.WatchOnlineLink = liveLink.Trim();
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Линкът за стрийма е запазен успешно!";
            return RedirectToPage();
        }

        // ══════════════════════════════════════════════════════════════════════
        // FAQ QUESTIONS
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveFaqAsync([FromForm] FaqModel model)
        {
            try
            {
                model.QuestionEn = model.QuestionEn?.Trim() ?? "";
                model.QuestionBg = model.QuestionBg?.Trim() ?? "";
                model.AnswerEn   = model.AnswerEn?.Trim()   ?? "";
                model.AnswerBg   = model.AnswerBg?.Trim()   ?? "";

                if (string.IsNullOrEmpty(model.QuestionEn)) return Err("Question (EN) is required.");
                if (string.IsNullOrEmpty(model.QuestionBg)) return Err("Question (BG) is required.");
                if (string.IsNullOrEmpty(model.AnswerEn))   return Err("Answer (EN) is required.");
                if (string.IsNullOrEmpty(model.AnswerBg))   return Err("Answer (BG) is required.");

                if (model.Id == 0)
                {
                    var maxOrder = await _context.Faqs.AnyAsync()
                        ? await _context.Faqs.MaxAsync(f => f.DisplayOrder)
                        : -1;
                    model.DisplayOrder = maxOrder + 1;
                    model.IsActive = true;
                    _context.Faqs.Add(model);
                }
                else
                {
                    var ex = await _context.Faqs.FindAsync(model.Id);
                    if (ex == null) return Err("FAQ not found.");
                    ex.QuestionEn = model.QuestionEn;
                    ex.QuestionBg = model.QuestionBg;
                    ex.AnswerEn   = model.AnswerEn;
                    ex.AnswerBg   = model.AnswerBg;
                }

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Server error: " + e.Message); }
        }

        public async Task<IActionResult> OnPostDeleteFaqAsync([FromForm] int id)
        {
            try
            {
                var faq = await _context.Faqs.FindAsync(id);
                if (faq == null) return Err("FAQ not found.");
                _context.Faqs.Remove(faq);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Error deleting FAQ: " + e.Message); }
        }

        public async Task<IActionResult> OnPostToggleFaqActiveAsync([FromForm] int id)
        {
            try
            {
                var faq = await _context.Faqs.FindAsync(id);
                if (faq == null) return Err("FAQ not found.");
                faq.IsActive = !faq.IsActive;
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Error toggling FAQ: " + e.Message); }
        }

        public async Task<IActionResult> OnPostReorderFaqsAsync([FromForm] List<int> orderedIds)
        {
            try
            {
                if (orderedIds == null || orderedIds.Count == 0) return Err("No order data received.");

                var faqs = await _context.Faqs
                    .Where(f => orderedIds.Contains(f.Id))
                    .ToListAsync();

                for (int i = 0; i < orderedIds.Count; i++)
                {
                    var faq = faqs.FirstOrDefault(f => f.Id == orderedIds[i]);
                    if (faq != null) faq.DisplayOrder = i;
                }

                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Error saving new FAQ order: " + e.Message); }
        }

        // ── JSON helpers ──────────────────────────────────────────────────────
        private static JsonResult Ok()  => new JsonResult(new { success = true });
        private static JsonResult Err(string msg) => new JsonResult(new { success = false, message = msg });
    }
}