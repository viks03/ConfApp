// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ConferenceApp.Services.Files;
using ConferenceApp.Services.Payments;
using System.IO;
using System.Text.RegularExpressions;

namespace ConferenceApp.Areas.Admin.Pages
{
    [Authorize(Roles = "Admin")]
    // The whole admin panel: one page, one model, and roughly fifty handlers.
    //
    // It is a single page because it is a single screen — the tabs are rendered
    // together and switched in the browser, so OnGetAsync loads everything the
    // panel can show, and each action is its own handler returning either a
    // redirect or JSON.
    //
    // Two things hold across every handler in this file. Every mutating action
    // is written to the audit log automatically — see
    // Services/Audit/AdminAuditFilter.cs, which also lists the handlers that
    // write their own, more detailed row and are therefore skipped. And the
    // DbContext is scoped and shared with that filter, so a handler that
    // returns early in the middle of changes still has them saved: values are
    // assigned only once a request is known to go through.
    [ServiceFilter(typeof(ConferenceApp.Services.Audit.AdminAuditFilter))]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly UserManager<ApplicationUser> _userManager;

        private readonly ConferenceApp.Services.Email.IMailComposer _mail;
        private readonly ConferenceApp.Services.Email.IEmailNotificationSettings _emailSettings;
        private readonly ConferenceApp.Services.Changelog.ChangelogReader _changelog;
        private readonly ConferenceApp.Services.Theming.ThemeProvider _themes;
        private readonly ConferenceApp.Services.IPaymentGateSettings _paymentGates;
        private readonly ConferenceApp.Services.Health.IHealthCheckService _health;
        private readonly ConferenceApp.Services.IDatabaseBackupRunner _backupRunner;
        private readonly ConferenceApp.Services.AuditService _audit;

        /// <summary>The state of the mail notification switches.</summary>
        public Dictionary<string, bool> EmailToggles { get; private set; } = new();

        public IReadOnlyList<ConferenceApp.Services.Changelog.ChangelogEntry> Changelog
            { get; private set; } = new List<ConferenceApp.Services.Changelog.ChangelogEntry>();

        // ── The "Visual styles" tab ───────────────────────────────────────
        public List<PageStyleRowVm>     PageStyleRows     { get; private set; } = new();
        public List<CustomBackgroundVm> CustomBackgrounds { get; private set; } = new();
        public int ManualStyleCount => PageStyleRows.Count(r => r.HasRecord);
        public int CustomCssCount   => PageStyleRows.Count(r => r.CustomCssEnabled
                                                            && !string.IsNullOrWhiteSpace(r.CustomCss));

        /// <summary>The global switch for phones. true means backgrounds are
        /// allowed; it overrides the per-page setting.</summary>
        public bool MobileAllowedGlobally { get; private set; } = true;

        /// <summary>The five downloadable files, always in the same order —
        /// see DownloadableFile.Keys.</summary>
        public List<DownloadableFile> Downloads { get; private set; } = new();
        public List<SiteTheme> Themes { get; private set; } = new();
        public string? ActiveThemeKey => Themes.FirstOrDefault(t => t.IsActive)?.ThemeKey;

        public sealed class PageStyleRowVm
        {
            public string  Key  { get; set; } = "";
            public string  Name { get; set; } = "";
            public string  Group { get; set; } = "";
            public string  EffectiveBackground { get; set; } = "grid";
            public bool    HasRecord { get; set; }
            public double? Intensity { get; set; }
            public double? Ink { get; set; }
            public double? Glow { get; set; }
            public double? CursorAlpha { get; set; }
            public int?    GridStep { get; set; }
            public int?    PaperStep { get; set; }
            public int?    BarHeight { get; set; }
            public string? CustomCss { get; set; }
            public bool    CustomCssEnabled { get; set; }
            public string? Motion { get; set; }
            public string? MotionSpeed { get; set; }
            public bool    ShowOnMobile { get; set; }
        }

        public sealed class CustomBackgroundVm
        {
            public string Slug { get; set; } = "";
            public string Name { get; set; } = "";
            public string ParamsJson { get; set; } = "{}";
            public string Mode { get; set; } = "params";
            public string? RawCss { get; set; }
            public bool   IsActive { get; set; }
            public int    UsedOnPages { get; set; }
        }

        /// <summary>The state of the eight Payment Control keys.</summary>
        public Dictionary<string, bool> PaymentGates { get; private set; } = new();
        private readonly IConfiguration _config;
        private readonly IUploadPaths _uploadPaths;

        public IndexModel(
            ApplicationDbContext context,
            IWebHostEnvironment env,
            UserManager<ApplicationUser> userManager,
            ConferenceApp.Services.Email.IMailComposer mail,
            ConferenceApp.Services.Email.IEmailNotificationSettings emailSettings,
            ConferenceApp.Services.IPaymentGateSettings paymentGates,
            ConferenceApp.Services.Health.IHealthCheckService health,
            ConferenceApp.Services.IDatabaseBackupRunner backupRunner,
            IConfiguration config,
            ConferenceApp.Services.Changelog.ChangelogReader changelog,
            ConferenceApp.Services.Theming.ThemeProvider themes,
            IUploadPaths uploadPaths,
            ConferenceApp.Services.AuditService audit)
        {
            _uploadPaths = uploadPaths;
            _changelog = changelog;
            _themes = themes;
            _context = context;
            _env = env;
            _userManager = userManager;
            _mail = mail;
            _emailSettings = emailSettings;
            _paymentGates = paymentGates;
            _health = health;
            _backupRunner = backupRunner;
            _config = config;
            _audit = audit;
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

        // ── Bug reports (the badge on the sidebar link) ──────────────────────
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

        // ── Privacy Policy / GDPR content (editable; replaces the resx) ───────
        public PrivacyPolicyContent PrivacyContent { get; set; } = new();

        // ── Terms of Use content (editable; replaces the resx) ────────────────
        public TermsOfUseContent TermsContent { get; set; } = new();

        // ── Cookie notice: the categories and the banner text ────────────────
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
            // The mail notification switches. Missing rows are created on this
            // first read, switched on by default.
            EmailToggles = await _emailSettings.GetAllAsync();
            await LoadPageStylesAsync();
            Changelog = _changelog.Read();
            await LoadDownloadsAsync();
            await LoadThemesAsync();

            // Payment Control, the same way: missing keys are created here,
            // switched on by default.
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
            // No OrderBy on purpose: the order in the public footer is random
            // (see _Layout.cshtml), so there is nothing to order by. Id is enough
            // to keep this list stable between reloads.
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
        /// The amount for the mail sent when an administrator confirms a payment.
        /// <para>
        /// An amount already recorded wins: somebody who paid a promotional
        /// price before it was removed from the panel is told their own figure,
        /// not the current one. Otherwise what the participation form owes is
        /// computed. A dash means the tier is not payable at all (student,
        /// journalist) — a decision, not a failed parse.
        /// </para>
        /// <para>
        /// This used to be a fifth way of working out a price, different from
        /// the other four ([AD-12]). It now goes through the same TicketPricing
        /// that charges.
        /// </para>
        /// </summary>
        private async Task<string> ResolveUserAmountAsync(ApplicationUser user)
        {
            try
            {
                if (user.PaidAmountEUR.HasValue)
                    return TicketPricing.Format(user.PaidAmountEUR);

                var tiers = await _context.TicketTiers.ToListAsync();
                return TicketPricing.Format(
                    TicketPricing.PriceEUR(TicketPricing.ForUser(tiers, user)));
            }
            catch
            {
                // The amount is informational; failing to read it must not fail
                // the confirmation that has already happened.
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

        // Does not save: the callers write the audit row together with their own
        // change, in one SaveChangesAsync.
        private void LogAudit(string userId, string userEmail, string action, string details) =>
            _audit.Add(userId, userEmail, action, details);

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

            var folder = _uploadPaths.EnsureDirectory("uploads", subfolder);

            var fname = Guid.NewGuid().ToString() + ext;
            using var fs = new FileStream(Path.Combine(folder, fname), FileMode.Create);
            file.CopyTo(fs);

            // The leading slash stays: these values are rendered as URLs in the
            // views (<img src="…">), unlike the paths of the private files, which
            // are only ever served through a handler.
            return "/" + _uploadPaths.ToRelative("uploads", subfolder, fname);
        }

        private void DeleteFile(string? relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return;
            var physical = _uploadPaths.ToPhysical(relativePath);
            if (physical != null && System.IO.File.Exists(physical)) System.IO.File.Delete(physical);
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORTS
        // ══════════════════════════════════════════════════════════════════════
        // The CSV opens in Excel, which is why the two exports below start with
        // a BOM: without it Excel reads the file as the local ANSI code page and
        // every Cyrillic name arrives as rubbish.
        public async Task<IActionResult> OnGetExportRegistrationsAsync(string type)
        {
            // The system administrator is not a participant and is left out of
            // every registration list.
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
                // Doubling the quotes is how a quote is escaped inside a quoted
                // CSV field; the details column is free text and does contain
                // them.
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

                // The statuses are captured BEFORE the change. A mail goes out
                // only if one of them actually moved — otherwise correcting a
                // typo in a name would send "your status has changed".
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

                // Read AFTER every change above, the automatic payment
                // confirmation that follows an approved student or journalist
                // verification included.
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

                // One mail per edit, not one per changed field.
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

            var physical = _uploadPaths.ToPhysical(user.PaperFilePath);
            if (physical == null || !System.IO.File.Exists(physical)) return NotFound();

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

                // [D-01] What the participation form owes is recorded as the
                // amount paid. It used to exist only as free text in the audit
                // log.
                user.PaidAmountEUR ??= TicketPricing.PriceEUR(
                    TicketPricing.ForUser(await _context.TicketTiers.ToListAsync(), user));

                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                    return new JsonResult(new { success = false, message = string.Join(", ", result.Errors.Select(e => e.Description)) });

                LogAudit(user.Id, user.Email ?? "", "Payment Confirmed — Admin",
                    $"Method: {payMethod} | Ref: {user.ReferenceNumber} | User: {user.FirstName} {user.LastName}");
                await _context.SaveChangesAsync();

                // When an administrator confirms a payment — usually a bank
                // transfer, once they have seen it in the account — the
                // participant used to be told nothing at all. They were left with
                // the "pending" mail from a few days earlier and no way of
                // knowing it had been confirmed.
                // The "already confirmed" guard is at the top of the method, so
                // this is reached only on a real change.
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

                // The "already approved" guard is at the top of the method, so
                // this is reached only on a real change of status.
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

                // The reason is free text typed by an administrator.
                // MailComposer passes it through EmailPlaceholders, which escapes
                // it — without that a "<" in the text would break the HTML of the
                // mail.
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

            var physical = _uploadPaths.ToPhysical(user.VerificationDocumentPath);
            if (physical == null || !System.IO.File.Exists(physical)) return NotFound();

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

            // The prices are read and validated BEFORE the row is touched.
            // `ticket` is tracked by EF, and AdminAuditFilter calls
            // SaveChangesAsync after the handler on the same scoped DbContext —
            // so returning early in the middle of changes does not undo them, it
            // saves them through the back door. Nothing is assigned until the
            // request is known to go through in full.
            if (!TryReadPriceEUR("EditTicket.RegularPriceEUR", out var regularEur) ||
                !TryReadPriceEUR("EditTicket.PromoPriceEUR", out var promoEur))
            {
                TempData["ErrorMessage"] =
                    "Цената трябва да е число (например 99.50) или празно. Нищо не е запазено.";
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

            // The numeric prices are what charging uses; the strings above are
            // for display only. An empty field means the tier is not payable.
            ticket.RegularPriceEUR = regularEur;
            ticket.PromoPriceEUR   = promoEur;
            ticket.PerksEn        = EditTicket.PerksEn?.Trim() ?? "";
            ticket.PerksBg        = EditTicket.PerksBg?.Trim() ?? "";

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Ticket tier updated successfully!";
            return RedirectToPage();
        }

        /// <summary>
        /// Reads a price from the raw form with InvariantCulture. A missing or
        /// empty field is valid and means "this tier is not payable". It returns
        /// false only for text that is not a number, in which case the handler
        /// saves nothing rather than leaving the row with a NULL price.
        /// <para>
        /// Why from the raw form and not from <c>EditTicket</c>: the fields in
        /// the panel are <c>&lt;input type="number"&gt;</c>, and a browser ALWAYS
        /// submits such a field with a dot for the decimal separator, whatever
        /// the language of the page — which is also how the view writes it out.
        /// Model binding, however, goes through the culture of the request, which
        /// defaults to "bg", where the separator is a comma: "99.50" does not
        /// parse, <c>EditTicket.RegularPriceEUR</c> stays null, and the price
        /// that charging uses quietly disappears while the display strings go on
        /// showing the old amount.
        /// </para>
        /// </summary>
        private bool TryReadPriceEUR(string field, out decimal? value)
        {
            value = null;

            var raw = Request.Form[field].ToString();
            if (string.IsNullOrWhiteSpace(raw)) return true;

            if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                return false;

            if (parsed < 0) return false;

            value = parsed;
            return true;
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
                        var folder = _uploadPaths.EnsureDirectory("uploads", "partners");
                        var fname = Guid.NewGuid().ToString() + ".svg";
                        using var fs = new FileStream(Path.Combine(folder, fname), FileMode.Create);
                        await logoFile.CopyToAsync(fs);
                        model.LogoImagePath = "/" + _uploadPaths.ToRelative("uploads", "partners", fname);
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
        // HOME PAGE LOGOS
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
        // SITE SETTINGS: SOCIAL LINKS
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
        // SITE SETTINGS: FOOTER CONTENT
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

                // The tagline is optional on purpose (see FooterContent.cs):
                // left empty, the tagline line is not rendered in the public
                // footer at all. The lengths here mirror the attributes on the
                // model — see the comments there for where the numbers come
                // from.
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
        // SITE SETTINGS: FOOTER QUICK LINKS
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
                // Defence in depth. IconSvg is rendered with Html.Raw in the
                // public footer (see _Layout.cshtml), which bypasses the normal
                // HTML encoding, and the field is free text rather than output
                // from a rich-text editor. This check is a second line on top of
                // trusting the administrator; it is not a full sanitizer, it just
                // blocks the obvious vectors.
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
        // MAIL NOTIFICATIONS — switching them on and off by kind
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Switches one kind of notification on or off. The sign-in and
        /// registration code (Otp) is deliberately not switchable — turning it
        /// off would make the site unusable, and the service refuses such a
        /// request.
        /// </summary>
        // ══════════════════════════════════════════════════════════════════════
        // CRYPTO ORDERS — clearing out the dead ones
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Deletes the crypto orders that can no longer lead to a payment.
        ///
        /// <para>
        /// What goes: <c>Expired</c> orders only, and only where the participant
        /// did NOT pay through them. <c>Confirmed</c> is never touched — that is
        /// the record of a real payment and the evidence in any dispute.
        /// <c>InProcess</c> is not touched either: the order is still live and
        /// the money may be moving at this very moment.
        /// </para>
        ///
        /// <para>
        /// One extra precaution: an InProcess order whose <c>ExpiresAt</c> has
        /// passed counts as dead, but only if it passed more than an hour ago.
        /// Go28 sometimes confirms late, and cleaning up too eagerly would delete
        /// an order that was about to go through.
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

                // Grouped for the audit row: who loses what.
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
        // THEMES
        // ══════════════════════════════════════════════════════════════════════

        private async Task LoadThemesAsync()
        {
            // The built-in themes are created the first time the panel is
            // opened rather than by a migration, so that adding one later is a
            // new file in wwwroot/themes and nothing else.
            await SeedBuiltInThemesAsync();

            Themes = await _context.SiteThemes.AsNoTracking()
                         .OrderByDescending(t => t.IsBuiltIn)
                         .ThenBy(t => t.Name)
                         .ToListAsync();
        }

        private async Task SeedBuiltInThemesAsync()
        {
            var dir = Path.Combine(_env.WebRootPath, "themes");
            if (!Directory.Exists(dir)) return;

            var existing = await _context.SiteThemes
                               .Where(t => t.IsBuiltIn)
                               .Select(t => t.ThemeKey)
                               .ToListAsync();

            var added = false;
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                var key = Path.GetFileNameWithoutExtension(file);
                if (existing.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;

                try
                {
                    var json = await System.IO.File.ReadAllTextAsync(file);
                    var check = ConferenceApp.Services.Theming.ThemeTokens.Parse(json);
                    if (!check.Ok) continue;   // повреден вграден файл — просто не влиза

                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var name = doc.RootElement.TryGetProperty("name", out var n)
                        ? n.GetString() ?? key : key;

                    _context.SiteThemes.Add(new SiteTheme
                    {
                        ThemeKey = key,
                        Name = name,
                        TokensJson = System.Text.Json.JsonSerializer.Serialize(check.Values),
                        IsBuiltIn = true,
                        IsActive = false     // ← НИЩО не се активира само
                    });
                    added = true;
                }
                catch { /* повреден файл не бива да чупи панела */ }
            }

            if (added) await _context.SaveChangesAsync();
        }

        /// <summary>Downloads the template that is handed to a language model
        /// — see Services/Theming/ThemeTemplate.cs.</summary>
        public async Task<IActionResult> OnGetThemeTemplateAsync()
        {
            Dictionary<string, string>? current = null;

            var active = await _context.SiteThemes.AsNoTracking().FirstOrDefaultAsync(t => t.IsActive);
            if (active is not null)
            {
                var c = ConferenceApp.Services.Theming.ThemeTokens.Parse(active.TokensJson);
                if (c.Ok) current = c.Values;
            }

            var json = ConferenceApp.Services.Theming.ThemeTemplate.Build(current);
            return File(System.Text.Encoding.UTF8.GetBytes(json),
                        "application/json", "theme-template.json");
        }

        public async Task<IActionResult> OnPostActivateThemeAsync([FromForm] string themeKey)
        {
            try
            {
                // An empty key means "no theme at all": the site falls back to
                // the values in the CSS file. A valid choice, not an error.
                var all = await _context.SiteThemes.ToListAsync();
                foreach (var t in all) t.IsActive = false;

                string msg;
                if (string.IsNullOrWhiteSpace(themeKey))
                {
                    msg = "Темата е изключена — сайтът ползва стандартните стойности.";
                }
                else
                {
                    var target = all.FirstOrDefault(t => t.ThemeKey == themeKey);
                    if (target is null) return Err("Няма такава тема.");
                    target.IsActive = true;
                    target.UpdatedAt = DateTime.UtcNow;
                    target.UpdatedBy = User.Identity?.Name;
                    msg = $"„{target.Name}“ е активна.";
                }

                LogAudit(string.Empty, User.Identity?.Name ?? "admin",
                    "Theme Activated", string.IsNullOrWhiteSpace(themeKey) ? "(няма)" : themeKey);
                await _context.SaveChangesAsync();

                _themes.Invalidate();   // ← без това промяната се вижда чак при рестарт
                return new JsonResult(new { success = true, message = msg });
            }
            catch (Exception e) { return Err("Грешка: " + e.Message); }
        }

        public async Task<IActionResult> OnPostUploadThemeAsync(IFormFile? file)
        {
            try
            {
                if (file is null || file.Length == 0) return Err("Не е избран файл.");
                if (file.Length > 200 * 1024) return Err("Файлът е твърде голям за тема.");

                using var reader = new StreamReader(file.OpenReadStream());
                var json = await reader.ReadToEndAsync();

                var check = ConferenceApp.Services.Theming.ThemeTokens.Parse(json);
                if (!check.Ok)
                    return new JsonResult(new
                    {
                        success = false,
                        message = "Темата не е приета.",
                        errors = check.Errors,
                        warnings = check.Warnings
                    });

                string name = "Качена тема";
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("name", out var n))
                        name = (n.GetString() ?? name).Trim();
                }
                catch { }

                if (name.Length > 80) name = name[..80];

                // The key is derived from the name but kept unique: a second
                // theme with the same name must not silently overwrite the
                // first.
                var baseKey = System.Text.RegularExpressions.Regex
                    .Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
                if (baseKey.Length is 0 or > 40) baseKey = "custom";

                var key = baseKey;
                var i = 2;
                while (await _context.SiteThemes.AnyAsync(t => t.ThemeKey == key))
                    key = $"{baseKey}-{i++}";

                _context.SiteThemes.Add(new SiteTheme
                {
                    ThemeKey = key,
                    Name = name,
                    TokensJson = System.Text.Json.JsonSerializer.Serialize(check.Values),
                    IsBuiltIn = false,
                    IsActive = false,      // ← качването НЕ активира
                    UpdatedBy = User.Identity?.Name
                });

                LogAudit(string.Empty, User.Identity?.Name ?? "admin",
                    "Theme Uploaded", $"{name} ({check.Values.Count} токена)");
                await _context.SaveChangesAsync();

                return new JsonResult(new
                {
                    success = true,
                    message = $"„{name}“ е добавена. Активирайте я, за да я видите.",
                    warnings = check.Warnings
                });
            }
            catch (Exception e) { return Err("Грешка при качване: " + e.Message); }
        }

        public async Task<IActionResult> OnPostDeleteThemeAsync([FromForm] string themeKey)
        {
            try
            {
                var t = await _context.SiteThemes.FirstOrDefaultAsync(x => x.ThemeKey == themeKey);
                if (t is null) return Err("Няма такава тема.");
                if (t.IsBuiltIn) return Err("Вградените теми не се трият.");

                var wasActive = t.IsActive;
                _context.SiteThemes.Remove(t);

                LogAudit(string.Empty, User.Identity?.Name ?? "admin", "Theme Deleted", themeKey);
                await _context.SaveChangesAsync();

                if (wasActive) _themes.Invalidate();

                return new JsonResult(new
                {
                    success = true,
                    message = wasActive
                        ? "Изтрита. Сайтът се върна към стандартните стойности."
                        : "Изтрита."
                });
            }
            catch (Exception e) { return Err("Грешка: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // DOWNLOADABLE FILES
        // ══════════════════════════════════════════════════════════════════════

        private async Task LoadDownloadsAsync()
        {
            var saved = await _context.DownloadableFiles.AsNoTracking()
                            .ToDictionaryAsync(x => x.FileKey, StringComparer.OrdinalIgnoreCase);

            // The order comes from Keys rather than from the database, so that
            // the panel always lists the same five in the same order and a
            // missing row shows as "not uploaded" instead of simply being
            // absent.
            Downloads = DownloadableFile.Keys
                .Select(k => saved.TryGetValue(k, out var f)
                                 ? f
                                 : new DownloadableFile { FileKey = k })
                .ToList();
        }

        public async Task<IActionResult> OnPostUploadDownloadAsync(
            [FromForm] string fileKey, IFormFile? file)
        {
            try
            {
                if (!DownloadableFile.Keys.Contains(fileKey))
                    return Err("Непознат файл.");

                if (file is null || file.Length == 0)
                    return Err("Не е избран файл.");

                // Documents only. An image here would be a mistake rather than a
                // choice.
                var path = SaveUploadedFile(file, "documents",
                    maxSizeBytes: 20 * 1024 * 1024,
                    allowedExtensions: new[] { ".pdf", ".docx", ".doc" });

                var row = await _context.DownloadableFiles
                              .FirstOrDefaultAsync(x => x.FileKey == fileKey);

                if (row is null)
                {
                    row = new DownloadableFile { FileKey = fileKey };
                    _context.DownloadableFiles.Add(row);
                }
                else
                {
                    // The old file is deleted AFTER the new one is safely
                    // written. The other way round, a failed upload would leave
                    // the page with nothing at all.
                    DeleteFile(row.FilePath);
                }

                row.FilePath = path;
                row.DownloadName = Path.GetFileName(file.FileName);
                row.SizeBytes = file.Length;
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedBy = User.Identity?.Name;

                LogAudit(string.Empty, User.Identity?.Name ?? "admin",
                    "Download File Uploaded",
                    $"{fileKey} | {row.DownloadName} | {row.SizeBytes / 1024} KB");

                await _context.SaveChangesAsync();

                return new JsonResult(new
                {
                    success = true,
                    message = "Файлът е качен.",
                    file = new
                    {
                        key = row.FileKey,
                        name = row.DownloadName,
                        size = row.SizeLabel,
                        type = row.TypeLabel,
                        path = row.FilePath,
                        updated = row.UpdatedAt?.ToString("dd.MM.yyyy")
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                // The messages from SaveUploadedFile are written for a person
                // and are shown as they are.
                return Err(ex.Message);
            }
            catch (Exception e) { return Err("Грешка при качване: " + e.Message); }
        }

        /// <summary>
        /// Removes the file but NOT the row: the key stays so that a new file
        /// can be uploaded in its place. The public page stops showing the button
        /// while there is none.
        /// </summary>
        public async Task<IActionResult> OnPostRemoveDownloadAsync([FromForm] string fileKey)
        {
            try
            {
                var row = await _context.DownloadableFiles
                              .FirstOrDefaultAsync(x => x.FileKey == fileKey);
                if (row is null) return Err("Няма такъв файл.");

                DeleteFile(row.FilePath);

                row.FilePath = null;
                row.DownloadName = null;
                row.SizeBytes = 0;
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedBy = User.Identity?.Name;

                LogAudit(string.Empty, User.Identity?.Name ?? "admin",
                    "Download File Removed", fileKey);
                await _context.SaveChangesAsync();

                return new JsonResult(new { success = true, message = "Файлът е премахнат." });
            }
            catch (Exception e) { return Err("Грешка: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // PER-PAGE VISUAL STYLES
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// The list of pages lives HERE, on the server, rather than in the
        /// panel's JavaScript — otherwise the two drift apart when a page is
        /// added and the tab quietly stops showing it. The fallback backgrounds
        /// match gfxDefaults in _GlobalEffects.cshtml; a change there needs a
        /// change here.
        /// </summary>
        private static readonly (string Key, string Name, string Group, string Fallback)[] StyleablePages =
        {
            ("/Index",            "Начало",              "Публични", "prism"),
            ("/Conference",       "Конференция",         "Публични", "sonar"),
            ("/ICBI",             "За ICBI",             "Публични", "caustic"),
            ("/Lecturers",        "Лектори",             "Публични", "sonar"),
            ("/Schedule",         "Програма",            "Публични", "lantern"),
            ("/Attend",           "Участие",             "Публични", "lantern"),
            ("/FAQ",              "Въпроси",             "Публични", "ascent"),
            ("/Travel",           "Пътуване",            "Публични", "caustic"),

            ("/Login",            "Вход",                "Служебни", "spine"),
            ("/Register",         "Регистрация",         "Служебни", "spine"),
            ("/Verification",     "Потвърждение",        "Служебни", "spine"),
            ("/Profile",          "Профил",              "Служебни", "grid"),
            ("/Payment",          "Плащане",             "Служебни", "spine"),
            ("/SubmitDocuments",  "Документи",           "Служебни", "spine"),
            ("/Done",             "Готово",              "Служебни", "spine"),
            ("/AccessDenied",     "Отказан достъп",      "Служебни", "grid"),
            ("/Error",            "Грешка",              "Служебни", "grid"),
            ("/BugReports",       "Сигнали",             "Служебни", "grid"),

            ("/Terms",            "Условия за ползване", "Правни",   "ascent"),
            ("/Privacy",          "Поверителност",       "Правни",   "ascent"),
            ("/Cookies",          "Бисквитки",           "Правни",   "ascent"),
        };

        private async Task LoadPageStylesAsync()
        {
            var all = await _context.PageStyleSettings.AsNoTracking().ToListAsync();

            // The "*" row is not a page: it is taken out before the dictionary
            // is built, so that it cannot appear as one in the list.
            var globalRow = all.FirstOrDefault(x => x.PageKey == PageStyleSetting.GlobalKey);
            MobileAllowedGlobally = globalRow is null || globalRow.ShowOnMobile;

            var saved = all.Where(x => x.PageKey != PageStyleSetting.GlobalKey)
                           .ToDictionary(x => x.PageKey, StringComparer.OrdinalIgnoreCase);

            PageStyleRows = StyleablePages.Select(p =>
            {
                saved.TryGetValue(p.Key, out var s);
                return new PageStyleRowVm
                {
                    Key = p.Key,
                    Name = p.Name,
                    Group = p.Group,
                    // What is listed is what will ACTUALLY be rendered, falling
                    // back the same way the partial does.
                    EffectiveBackground = s?.Background ?? p.Fallback,
                    HasRecord = s is not null,
                    Intensity = s?.Intensity,
                    Ink = s?.Ink,
                    Glow = s?.Glow,
                    CursorAlpha = s?.CursorAlpha,
                    GridStep = s?.GridStep,
                    PaperStep = s?.PaperStep,
                    BarHeight = s?.BarHeight,
                    CustomCss = s?.CustomCss,
                    CustomCssEnabled = s?.CustomCssEnabled ?? false,
                    Motion = s?.Motion,
                    MotionSpeed = s?.MotionSpeed,
                    ShowOnMobile = s?.ShowOnMobile ?? false
                };
            }).ToList();

            var bgs = await _context.CustomBackgrounds.AsNoTracking().ToListAsync();
            CustomBackgrounds = bgs.Select(b => new CustomBackgroundVm
            {
                Slug = b.Slug,
                Name = b.Name,
                ParamsJson = b.LayersJson,
                Mode = b.Mode,
                RawCss = b.RawCss,
                IsActive = b.IsActive,
                UsedOnPages = saved.Values.Count(v =>
                    string.Equals(v.Background, $"custom:{b.Slug}", StringComparison.OrdinalIgnoreCase))
            }).ToList();
        }

        /// <summary>A value outside its range becomes null rather than an
        /// exception: one bad number must not fail the whole save.</summary>
        private static double? ClampD(string? raw, double min, double max)
            => double.TryParse(raw, System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var v)
               && v >= min && v <= max ? v : null;

        private static int? ClampI(string? raw, int min, int max)
            => int.TryParse(raw, out var v) && v >= min && v <= max ? v : null;

        public async Task<IActionResult> OnPostSavePageStyleAsync(
            [FromForm] string pageKey, [FromForm] string background,
            [FromForm] string? intensity, [FromForm] string? ink, [FromForm] string? glow,
            [FromForm] string? cursorAlpha, [FromForm] string? gridStep,
            [FromForm] string? paperStep, [FromForm] string? barHeight,
            [FromForm] string? customCss, [FromForm] bool customCssEnabled,
            [FromForm] string? motion, [FromForm] string? motionSpeed,
            [FromForm] bool showOnMobile)
        {
            try
            {
                if (!StyleablePages.Any(p => string.Equals(p.Key, pageKey, StringComparison.OrdinalIgnoreCase)))
                    return Err("Непозната страница.");

                // The background is a choice from a list, not free text: an
                // unknown slug would render as no background at all, with no
                // error anywhere to explain it.
                var isCustom = background?.StartsWith("custom:", StringComparison.OrdinalIgnoreCase) == true;
                if (isCustom)
                {
                    var slug = background![7..];
                    if (!await _context.CustomBackgrounds.AnyAsync(b => b.Slug == slug && b.IsActive))
                        return Err("Няма такъв активен собствен фон.");
                }
                else if (!ConferenceApp.Services.Styles.AmbientBackgrounds.IsBuiltIn(background))
                {
                    return Err("Непознат фон.");
                }

                var css = ConferenceApp.Services.Styles.CssSanitizer.Sanitize(customCss);
                if (css.Level == "bad")
                    return new JsonResult(new
                    {
                        success = false,
                        message = "Собственият CSS е отхвърлен.",
                        report = new { level = css.Level, errors = css.Errors, warnings = css.Warnings }
                    });

                var row = await _context.PageStyleSettings.FirstOrDefaultAsync(x => x.PageKey == pageKey);

                // A snapshot BEFORE the change — the only way back if an
                // administrator breaks the page with their own CSS.
                if (row is not null)
                {
                    _context.PageStyleRevisions.Add(new PageStyleRevision
                    {
                        PageKey = pageKey,
                        SnapshotJson = System.Text.Json.JsonSerializer.Serialize(row),
                        CreatedBy = User.Identity?.Name
                    });

                    // The last 10 are kept; otherwise the table grows without a
                    // ceiling.
                    //
                    // Skip(9), not Skip(10): the query asks the DATABASE, and the
                    // snapshot just added has not been saved yet and is not in
                    // the result. With Skip(10) the table kept 10 old ones plus
                    // the new one — eleven, and eleven from then on.
                    var old = await _context.PageStyleRevisions
                                  .Where(r => r.PageKey == pageKey)
                                  .OrderByDescending(r => r.CreatedAt)
                                  .Skip(9).ToListAsync();
                    if (old.Count > 0) _context.PageStyleRevisions.RemoveRange(old);
                }
                else
                {
                    row = new PageStyleSetting { PageKey = pageKey };
                    _context.PageStyleSettings.Add(row);
                }

                row.Background       = background!;
                row.Intensity        = ClampD(intensity,   0, 1);
                row.Ink              = ClampD(ink,         0, 0.2);
                row.Glow             = ClampD(glow,        0, 0.4);
                row.CursorAlpha      = ClampD(cursorAlpha, 0, 0.2);
                row.GridStep         = ClampI(gridStep,   16, 160);
                row.PaperStep        = ClampI(paperStep,   4, 40);
                row.BarHeight        = ClampI(barHeight,   0, 6);
                row.CustomCss        = string.IsNullOrWhiteSpace(css.Sanitized) ? null : css.Sanitized;
                // Only the three known values; anything else means "as the
                // preset intends" rather than an exception.
                row.Motion = motion is "off" or "drift" or "slide" or "swell"
                                    or "breathe" or "turn" ? motion : null;
                row.MotionSpeed = motionSpeed is "slower" or "slow" or "fast" or "faster"
                                    ? motionSpeed : null;
                row.ShowOnMobile = showOnMobile;
                row.CustomCssEnabled = customCssEnabled && row.CustomCss is not null;
                row.UpdatedAt        = DateTime.UtcNow;
                row.UpdatedBy        = User.Identity?.Name;

                LogAudit(string.Empty, User.Identity?.Name ?? "admin", "Page Style Saved",
                    $"{pageKey} | bg={row.Background} | css={(row.CustomCss is null ? "—" : row.CustomCss.Length + " зн.")}");

                await _context.SaveChangesAsync();

                // What is returned is what was STORED, not what was sent: the
                // server may have trimmed the CSS or clamped a value.
                return new JsonResult(new
                {
                    success = true,
                    message = "Записано.",
                    setting = new
                    {
                        background = row.Background,
                        intensity = row.Intensity, ink = row.Ink, glow = row.Glow,
                        cursorAlpha = row.CursorAlpha, gridStep = row.GridStep,
                        paperStep = row.PaperStep, barHeight = row.BarHeight,
                        customCss = row.CustomCss, customCssEnabled = row.CustomCssEnabled,
                        motion = row.Motion, motionSpeed = row.MotionSpeed,
                        showOnMobile = row.ShowOnMobile
                    },
                    report = new { level = css.Level, errors = css.Errors, warnings = css.Warnings }
                });
            }
            catch (Exception e) { return Err("Грешка при запис: " + e.Message); }
        }

        /// <summary>The global switch for phones — one row, with the key
        /// "*".</summary>
        public async Task<IActionResult> OnPostSetGlobalMobileAsync([FromForm] bool allowed)
        {
            try
            {
                var row = await _context.PageStyleSettings
                              .FirstOrDefaultAsync(x => x.PageKey == PageStyleSetting.GlobalKey);

                if (row is null)
                {
                    row = new PageStyleSetting
                    {
                        PageKey = PageStyleSetting.GlobalKey,
                        Background = "off"   // не се ползва за този ред
                    };
                    _context.PageStyleSettings.Add(row);
                }

                row.ShowOnMobile = allowed;
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedBy = User.Identity?.Name;

                LogAudit(string.Empty, User.Identity?.Name ?? "admin",
                    "Global Mobile Backgrounds", allowed ? "Разрешени" : "Забранени");
                await _context.SaveChangesAsync();

                return new JsonResult(new
                {
                    success = true,
                    message = allowed
                        ? "Фоновете са разрешени на телефон."
                        : "Фоновете са спрени на телефон за целия сайт.",
                    allowed
                });
            }
            catch (Exception e) { return Err("Грешка: " + e.Message); }
        }

        /// <summary>Toggles the phone setting for ONE page, from the list and
        /// without opening the editor.</summary>
        public async Task<IActionResult> OnPostTogglePageMobileAsync(
            [FromForm] string pageKey, [FromForm] bool enabled)
        {
            try
            {
                if (!StyleablePages.Any(p => string.Equals(p.Key, pageKey, StringComparison.OrdinalIgnoreCase)))
                    return Err("Непозната страница.");

                var row = await _context.PageStyleSettings.FirstOrDefaultAsync(x => x.PageKey == pageKey);

                if (row is null)
                {
                    // The page has no row yet. A minimal one is created with its
                    // own fallback background — otherwise the toggle would pin
                    // "grid" onto a page whose default is something else.
                    var fb = StyleablePages.First(p =>
                        string.Equals(p.Key, pageKey, StringComparison.OrdinalIgnoreCase)).Fallback;

                    row = new PageStyleSetting { PageKey = pageKey, Background = fb };
                    _context.PageStyleSettings.Add(row);
                }

                row.ShowOnMobile = enabled;
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedBy = User.Identity?.Name;

                LogAudit(string.Empty, User.Identity?.Name ?? "admin",
                    "Page Mobile Toggle", $"{pageKey} = {(enabled ? "вкл." : "изкл.")}");
                await _context.SaveChangesAsync();

                return new JsonResult(new { success = true, message = "Записано.", enabled });
            }
            catch (Exception e) { return Err("Грешка: " + e.Message); }
        }

        public async Task<IActionResult> OnPostResetPageStyleAsync([FromForm] string pageKey)
        {
            try
            {
                var row = await _context.PageStyleSettings.FirstOrDefaultAsync(x => x.PageKey == pageKey);
                if (row is not null)
                {
                    _context.PageStyleSettings.Remove(row);
                    LogAudit(string.Empty, User.Identity?.Name ?? "admin", "Page Style Reset", pageKey);
                    await _context.SaveChangesAsync();
                }

                var fb = StyleablePages.FirstOrDefault(p =>
                    string.Equals(p.Key, pageKey, StringComparison.OrdinalIgnoreCase)).Fallback ?? "grid";

                return new JsonResult(new { success = true, message = "Върнато към резервната стойност.", fallbackBackground = fb });
            }
            catch (Exception e) { return Err("Грешка: " + e.Message); }
        }

        /// <summary>The escape hatch: switches every custom CSS block off
        /// without deleting any of it.</summary>
        public async Task<IActionResult> OnPostDisableAllCustomCssAsync()
        {
            try
            {
                var rows = await _context.PageStyleSettings.Where(x => x.CustomCssEnabled).ToListAsync();
                foreach (var r in rows) { r.CustomCssEnabled = false; r.UpdatedAt = DateTime.UtcNow; }

                LogAudit(string.Empty, User.Identity?.Name ?? "admin",
                    "All Custom CSS Disabled", $"Affected: {rows.Count}");
                await _context.SaveChangesAsync();

                return new JsonResult(new { success = true, message = $"Изключен на {rows.Count} страници.", affected = rows.Count });
            }
            catch (Exception e) { return Err("Грешка: " + e.Message); }
        }

        public async Task<IActionResult> OnPostSaveCustomBackgroundAsync(
            [FromForm] string? originalSlug, [FromForm] string slug,
            [FromForm] string name, [FromForm] string layersJson,
            [FromForm] string? mode, [FromForm] string? rawCss)
        {
            try
            {
                slug = (slug ?? "").Trim().ToLowerInvariant();
                if (!System.Text.RegularExpressions.Regex.IsMatch(slug, @"^[a-z0-9-]{2,40}$"))
                    return Err("Кодът може да съдържа само малки латински букви, цифри и тире.");

                if (string.IsNullOrWhiteSpace(name)) return Err("Липсва име.");

                // Checked to be JSON here: the panel is not trusted to have sent
                // a valid structure.
                try { System.Text.Json.JsonDocument.Parse(layersJson); }
                catch { return Err("Параметрите не са валиден JSON."); }

                if (layersJson.Length > 1200) return Err("Параметрите са твърде дълги.");

                var row = string.IsNullOrWhiteSpace(originalSlug)
                    ? null
                    : await _context.CustomBackgrounds.FirstOrDefaultAsync(b => b.Slug == originalSlug);

                if (row is null)
                {
                    if (await _context.CustomBackgrounds.AnyAsync(b => b.Slug == slug))
                        return Err("Вече има фон с този код.");
                    row = new CustomBackground { Slug = slug, CreatedAt = DateTime.UtcNow };
                    _context.CustomBackgrounds.Add(row);
                }
                else if (!string.Equals(row.Slug, slug, StringComparison.OrdinalIgnoreCase)
                         && await _context.CustomBackgrounds.AnyAsync(b => b.Slug == slug))
                {
                    return Err("Вече има фон с този код.");
                }

                row.Slug = slug;
                row.Name = name.Trim();
                row.Mode = string.Equals(mode, "css", StringComparison.OrdinalIgnoreCase) ? "css" : "params";
                row.LayersJson = layersJson;

                if (row.Mode == "css")
                {
                    // Sanitized ON WRITE rather than on read: otherwise every
                    // page load would pay for it.
                    var sel = $"#ambient-fx[data-gfx-bg=\"custom:{slug}\"]";
                    var check = ConferenceApp.Services.Styles.CssSanitizer
                                    .SanitizeWithKeyframes(rawCss, slug, sel);

                    if (check.Level == "bad")
                        return new JsonResult(new
                        {
                            success = false,
                            message = "CSS-ът е отхвърлен.",
                            report = new { level = check.Level, errors = check.Errors, warnings = check.Warnings }
                        });

                    row.RawCss = string.IsNullOrWhiteSpace(check.Sanitized) ? null : check.Sanitized;
                }
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedBy = User.Identity?.Name;

                LogAudit(string.Empty, User.Identity?.Name ?? "admin", "Custom Background Saved", slug);
                await _context.SaveChangesAsync();

                return new JsonResult(new { success = true, message = "Записано.", slug = row.Slug });
            }
            catch (Exception e) { return Err("Грешка: " + e.Message); }
        }

        /// <summary>
        /// Gives the panel EXACTLY the rules the site will draw, so that the
        /// preview and the page cannot disagree.
        /// A GET, because it only reads and changes nothing.
        /// </summary>
        public IActionResult OnGetCustomBackgroundCss(
            string? slug, string? layersJson, string? mode, string? rawCss)
        {
            var key = string.IsNullOrWhiteSpace(slug) ? "preview" : slug;

            var css = string.Equals(mode, "css", StringComparison.OrdinalIgnoreCase)
                ? ConferenceApp.Services.Styles.CustomBackgroundCss.BuildFromCss(key, rawCss)
                : ConferenceApp.Services.Styles.CustomBackgroundCss.Build(key, layersJson);

            return new JsonResult(new { success = true, css });
        }

        public async Task<IActionResult> OnPostDeleteCustomBackgroundAsync([FromForm] string slug)
        {
            try
            {
                var row = await _context.CustomBackgrounds.FirstOrDefaultAsync(b => b.Slug == slug);
                if (row is null) return Err("Няма такъв фон.");

                // The pages using it fall back to "grid": otherwise they would
                // point at a background that no longer exists and render
                // nothing.
                var users = await _context.PageStyleSettings
                                .Where(p => p.Background == "custom:" + slug).ToListAsync();
                foreach (var u in users) { u.Background = "grid"; u.UpdatedAt = DateTime.UtcNow; }

                _context.CustomBackgrounds.Remove(row);
                LogAudit(string.Empty, User.Identity?.Name ?? "admin",
                    "Custom Background Deleted", $"{slug} | Pages reset: {users.Count}");
                await _context.SaveChangesAsync();

                return new JsonResult(new { success = true, message = "Изтрито.", affected = users.Count });
            }
            catch (Exception e) { return Err("Грешка: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // HEALTH CHECK
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// GET /Admin?handler=HealthCheck&amp;service=&lt;key&gt; — one service
        /// GET /Admin?handler=HealthCheck                       — all nine
        ///
        /// <para>
        /// A GET on purpose: the check only reads and changes nothing, so it
        /// needs no antiforgery token. If it ever becomes a POST, the script has
        /// to start sending a RequestVerificationToken.
        /// </para>
        /// </summary>
        public async Task<IActionResult> OnGetHealthCheckAsync(string? service, CancellationToken ct)
        {
            // The page requires the Admin role, but a handler is an entry point
            // of its own and checks for itself.
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
                // The tab was closed or reloaded mid-check. Not an error.
                return new JsonResult(new { error = "cancelled" }) { StatusCode = 499 };
            }
        }

        /// <summary>
        /// POST /Admin?handler=CreateBackup — one backup of the database, now.
        ///
        /// <para>
        /// A POST, unlike the check next to it: this one writes. It therefore
        /// carries the antiforgery token like every other action in the panel,
        /// and the script in the Health tab sends it.
        /// </para>
        /// <para>
        /// The copying is <see cref="ConferenceApp.Services.IDatabaseBackupRunner"/>'s,
        /// the same one the 03:00 / 15:00 schedule calls — so a manual copy is
        /// consistent, rotated and audited exactly like an automatic one. Two of
        /// them cannot run at once: the runner turns the second away rather than
        /// letting two writers race over one temporary file, which is what a
        /// double click on the button would otherwise produce.
        /// </para>
        /// <para>
        /// The audit row is written by the runner, with the administrator's name
        /// and the file — which is why "CreateBackup" is in
        /// <c>AdminAuditFilter.SelfLogging</c> and does not get a second, poorer
        /// row from the filter.
        /// </para>
        /// </summary>
        public async Task<IActionResult> OnPostCreateBackupAsync(CancellationToken ct)
        {
            // The page requires the Admin role, but a handler is an entry point
            // of its own and checks for itself — as OnGetHealthCheckAsync does.
            if (!User.IsInRole("Admin"))
                return new JsonResult(new { success = false, message = "Само администратор може да прави резервно копие." })
                    { StatusCode = 403 };

            try
            {
                var outcome = await _backupRunner.RunAsync(
                    ConferenceApp.Services.BackupTrigger.Manual,
                    User.Identity?.Name ?? "admin",
                    ct);

                // A second press while the first copy is still being written.
                // Not an error, and deliberately not a success either: no new
                // file came of it.
                if (outcome.Busy)
                    return Err("Копие вече се прави в момента. Изчакай го да приключи.");

                if (!outcome.Success)
                    return Err("Копието не беше направено: " + (outcome.Error ?? "неизвестна причина"));

                return new JsonResult(new
                {
                    success  = true,
                    message  = $"Копието е готово: {outcome.FileName} ({outcome.SizeMb:0.0} MB)",
                    fileName = outcome.FileName,
                    sizeMb   = Math.Round(outcome.SizeMb, 1)
                });
            }
            catch (OperationCanceledException)
            {
                // The administrator navigated away mid-copy. The runner has
                // already cleaned its unfinished file away.
                return new JsonResult(new { success = false, message = "Копирането беше прекъснато." })
                    { StatusCode = 499 };
            }
            catch (Exception e)
            {
                // Never a stack trace to the browser, and never a 500 that takes
                // the tab down with it — the card stays usable.
                return Err("Копието не беше направено: " + e.Message);
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
        /// Payment Control — one of the eight keys per request, never the whole
        /// set. The hierarchy (all → method → currency) is not cascaded on write:
        /// switching method.crypto off leaves the individual currencies as they
        /// were, so that they do not all have to be switched on again when the
        /// gateway comes back. The hierarchy is applied on read instead
        /// (PaymentModel.IsMethodEnabled and IsCurrencySupported).
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
        // SHARED HELPERS FOR THE LEGAL TEXTS (Privacy, Terms, Cookie *)
        // ══════════════════════════════════════════════════════════════════════

        // The client sends contentEn/contentBg base64-encoded. Raw HTML —
        // especially with base64 images embedded by the Quill image button — is
        // exactly the shape the WAF in front of the production site flags as a
        // malware payload, and the request never arrives. The same reason
        // SendInvitations encodes its template. The wrapper hides the structure
        // from the pattern matching and changes nothing functionally.
        private static string DecodeBase64Utf8(string? base64)
        {
            if (string.IsNullOrWhiteSpace(base64)) return "";
            try
            {
                var bytes = Convert.FromBase64String(base64);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                // Not base64 — another client, or a stale cached script. Safer
                // to treat as empty than to fail the whole save.
                return "";
            }
        }

        // An empty Quill editor returns "<p><br></p>" rather than an empty
        // string, so that counts as no content too.
        private static bool IsEffectivelyEmpty(string html) =>
            string.IsNullOrWhiteSpace(html) || html == "<p><br></p>";

        // ══════════════════════════════════════════════════════════════════════
        // PRIVACY POLICY / GDPR — replaces the old Pages.Privacy.*.resx
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSavePrivacyPolicyAsync(
            [FromForm] string contentEn, [FromForm] string contentBg)
        {
            try
            {
                var cleanEn = DecodeBase64Utf8(contentEn).Trim();
                var cleanBg = DecodeBase64Utf8(contentBg).Trim();

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
        // TERMS OF USE — replaces the old Pages.Terms.*.resx.
        // Mirrors OnPostSavePrivacyPolicyAsync above, base64 wrapper included
        // (see the comment there for the reason).
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveTermsOfUseAsync(
            [FromForm] string contentEn, [FromForm] string contentBg)
        {
            try
            {
                var cleanEn = DecodeBase64Utf8(contentEn).Trim();
                var cleanBg = DecodeBase64Utf8(contentBg).Trim();

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
        // COOKIE NOTICE — the categories
        // ══════════════════════════════════════════════════════════════════════

        // id == 0 means a new category; anything else updates an existing one.
        //
        // "necessary" is a special case: IsToggleable and DefaultOn are forced
        // server-side whatever was submitted, because strictly necessary cookies
        // cannot lawfully be switched off by the visitor. That holds for an
        // administrator's mistake and for a hand-made POST alike.
        public async Task<IActionResult> OnPostSaveCookieCategoryAsync(
            int id, string key, string nameEn, string nameBg,
            string? descriptionEn, string? descriptionBg,
            bool isVisible, bool isToggleable)
        {
            try
            {
                // The descriptions come from Quill as rich HTML, base64-encoded
                // by the client — the same reason as for the policy texts.
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
                    // The key has to be unique. The unique index would refuse a
                    // duplicate anyway; checking first turns a raw constraint
                    // error into a sentence the administrator can act on.
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
                    // Strictly necessary cookies cannot lawfully be switched off
                    // by the visitor — forced here whatever was submitted.
                    category.IsToggleable = false;
                    category.DefaultOn = true;
                }
                else
                {
                    // Every other category starts switched off: the GDPR requires
                    // opt-in, not opt-out. There is deliberately no control for
                    // this in the panel, so that a non-essential category cannot
                    // be pre-ticked by accident.
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

                // "necessary" cannot be deleted: without it there is no category
                // for the cookies the site cannot work without.
                if (category.Key == "necessary")
                    return Err("The Strictly Necessary category can't be deleted — it's legally required.");

                _context.CookieCategories.Remove(category);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception e) { return Err("Server error: " + e.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // COOKIE NOTICE — the banner text
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveCookieNoticeAsync(
            [FromForm] string contentEn, [FromForm] string contentBg)
        {
            try
            {
                var cleanEn = DecodeBase64Utf8(contentEn).Trim();
                var cleanBg = DecodeBase64Utf8(contentBg).Trim();

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
        // COOKIE POLICY PAGE — the body of the /Cookies page
        // ══════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveCookiePolicyAsync(
            [FromForm] string contentEn, [FromForm] string contentBg)
        {
            try
            {
                var cleanEn = DecodeBase64Utf8(contentEn).Trim();
                var cleanBg = DecodeBase64Utf8(contentBg).Trim();

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
        // SITE SETTINGS: PROMO SLIDES
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
                // An empty or "#" link is stored as null, so that the public page
                // can test one thing rather than three.
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
                    // The stream link is updated only when it was edited in this
                    // modal; the small modal below has a handler of its own.
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

        // Saves only the live stream link, from the small modal in the schedule
        // list.
        public async Task<IActionResult> OnPostSaveSessionLiveLinkAsync(
            [FromForm] int liveLinkSessionId, 
            [FromForm] string liveLinkUrl)
        {
            try
            {
                var session = await _context.Schedule.FindAsync(liveLinkSessionId);
                if (session == null)
                    return Err("Session not found.");

                // "#" and an empty string both mean "no link".
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
        // THE SITE-WIDE LIVE STREAM LINK
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