using ConferenceApp.Data;
using ConferenceApp.Models;
using ConferenceApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using System.Reflection;

namespace ConferenceApp.Pages
{
    [ValidateAntiForgeryToken] // FIX: задължителна CSRF защита за POST
    public class LoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly ConferenceApp.Services.Email.IMailComposer _mail;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<LoginModel> _logger;
        private readonly IStringLocalizer _localizer;

        public LoginModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            ConferenceApp.Services.Email.IMailComposer mail,
            IConfiguration config,
            IWebHostEnvironment environment,
            ILogger<LoginModel> logger,
            IStringLocalizerFactory localizerFactory)
        {
            _signInManager  = signInManager;
            _userManager    = userManager;
            _context        = context;
            _mail = mail;
            _config = config;
            _environment    = environment;
            _logger         = logger;
            _localizer      = localizerFactory.Create("Pages.Login", Assembly.GetExecutingAssembly().GetName().Name!);
        }

        [BindProperty]
        public string Email { get; set; } = string.Empty;

        [BindProperty]
        public string Password { get; set; } = string.Empty;

        public bool RequirePassword { get; set; } = false;
        public bool IsBlocked { get; set; } = false;
        public string? AdminWarningMessage { get; set; }
        public string? ErrorMessage { get; set; }

        // ── GET ──────────────────────────────────────────────────────────────────
        public async Task<IActionResult> OnGetAsync()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole("Admin")) return LocalRedirect("/Admin");
                return LocalRedirect("/Profile");
            }

            IsBlocked = await CheckBlockStatusAsync();

            if (TempData.ContainsKey("LoginEmail"))
            {
                Email           = TempData["LoginEmail"]?.ToString() ?? string.Empty;
                RequirePassword = TempData.ContainsKey("ReqPass") && (bool)TempData["ReqPass"]!;
            }

            if (IsBlocked)
            {
                ErrorMessage    = _localizer["Error_LoginRestricted"].Value;
                RequirePassword = true;
            }
            else
            {
                if (TempData.ContainsKey("StatusErr")) ErrorMessage        = TempData["StatusErr"]?.ToString();
                if (TempData.ContainsKey("AdminMsg"))  AdminWarningMessage = TempData["AdminMsg"]?.ToString();
            }

            return Page();
        }

        // ── POST ─────────────────────────────────────────────────────────────────
        public async Task<IActionResult> OnPostAsync()
        {
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            if (await CheckBlockStatusAsync())
                return RedirectToPage();

            if (string.IsNullOrWhiteSpace(Email))
            {
                TempData["StatusErr"] = _localizer["Error_EmailRequired"].Value;
                return RedirectToPage();
            }

            var cleanedEmail = Email.Trim().ToLower();
            var user         = await _userManager.FindByEmailAsync(cleanedEmail);

            if (user == null)
            {
                // FIX: Не разкриваме дали имейлът съществува в лога видим за потребителя
                _logger.LogWarning("Login attempt with non-existent email from {Ip}", clientIp);
                await QueueAuditAsync("Login Failed", $"Attempt with non-existent email: {cleanedEmail}", clientIp);
                await _context.SaveChangesAsync();

                TempData["StatusErr"] = _localizer["Error_UserNotFound"].Value;
                return RedirectToPage();
            }

            bool isPasswordUser = await _userManager.HasPasswordAsync(user);

            if (isPasswordUser)
                return await HandleAdminLoginAsync(user, cleanedEmail, clientIp);
            else
                return await HandleOtpLoginAsync(user, cleanedEmail, clientIp);
        }

        // ── ADMIN (PASSWORD) LOGIN ────────────────────────────────────────────────
        private async Task<IActionResult> HandleAdminLoginAsync(ApplicationUser user, string email, string ip)
        {
            // FIX: Броим директно от DB — не инкрементираме локална променлива,
            //      за да избегнем race condition при конкурентни заявки
            int failedCount = await _context.Set<AuditLog>()
                .CountAsync(a =>
                    a.IpAddress  == ip     &&
                    a.UserEmail  == email  &&
                    a.Action     == "Admin Login Failed" &&
                    a.Timestamp  > DateTime.UtcNow.AddHours(-12));

            if (failedCount >= 3)
            {
                await HandlePermanentBlockAsync(user, ip);
                return RedirectToPage();
            }

            if (string.IsNullOrEmpty(Password))
            {
                TempData["LoginEmail"] = email;
                TempData["ReqPass"]    = true;
                TempData["AdminMsg"]   = string.Format(_localizer["Error_AdminLoginDetected"].Value, 3 - failedCount);
                return RedirectToPage();
            }

            var result = await _signInManager.PasswordSignInAsync(
                user.UserName!, Password, isPersistent: true, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                // FIX: Единичен SaveChangesAsync накрая
                QueueAudit("Admin Login", "Successful password login.", ip, user.Id, user.Email);
                await _context.SaveChangesAsync();

                var roles = await _userManager.GetRolesAsync(user);
                return LocalRedirect(roles.Contains("Admin") ? "/Admin" : "/Profile");
            }

            // Failed login
            QueueAudit("Admin Login Failed", "Invalid password attempt.", ip, user.Id, user.Email);
            await _context.SaveChangesAsync();

            // Вземи актуалния брой след записа
            int newFailedCount = await _context.Set<AuditLog>()
                .CountAsync(a =>
                    a.IpAddress == ip    &&
                    a.UserEmail == email &&
                    a.Action    == "Admin Login Failed" &&
                    a.Timestamp > DateTime.UtcNow.AddHours(-12));

            if (newFailedCount >= 3)
            {
                await HandlePermanentBlockAsync(user, ip);
                return RedirectToPage();
            }

            TempData["LoginEmail"] = email;
            TempData["ReqPass"]    = true;
            TempData["StatusErr"]  = _localizer["Error_InvalidPassword"].Value;
            TempData["AdminMsg"]   = string.Format(_localizer["Error_AdminLoginDetected"].Value, 3 - newFailedCount);
            return RedirectToPage();
        }

        // ── OTP LOGIN ────────────────────────────────────────────────────────────
        private async Task<IActionResult> HandleOtpLoginAsync(ApplicationUser user, string email, string ip)
        {
            // БЪГ ФИКС: заключен акаунт (12ч lockout след 3 грешни кода на
            // Verification страницата) преди изобщо не се проверяваше тук —
            // потребителят просто влизаше отново с имейла си и получаваше
            // нов код, все едно lockout-ът не съществува.
            if (await _userManager.IsLockedOutAsync(user))
            {
                TempData["StatusErr"] = _localizer["Error_AccountLocked"].Value;
                return RedirectToPage();
            }

            var recentOtpsCount = await _context.Set<OtpCode>()
                .CountAsync(o =>
                    o.Email    == email   &&
                    o.Purpose  == "Login" &&
                    o.CreatedAt >= DateTime.UtcNow.AddMinutes(-30));

            if (recentOtpsCount >= 3)
            {
                TempData["StatusErr"] = _localizer["Error_TooManyEmails"].Value;
                return RedirectToPage();
            }

            // Инвалидирай стари кодове
            var oldCodes = await _context.Set<OtpCode>()
                .Where(o => o.Email == email && !o.IsUsed)
                .ToListAsync();
            foreach (var c in oldCodes) c.IsUsed = true;

            // Създай нов код
            string otpCode = System.Security.Cryptography.RandomNumberGenerator
                .GetInt32(100000, 999999).ToString();

            _context.Set<OtpCode>().Add(new OtpCode
            {
                Email          = email,
                Code           = otpCode,
                ExpirationTime = DateTime.UtcNow.AddMinutes(15),
                Purpose        = "Login"
            });

            QueueAudit("Login OTP Sent", "OTP code sent for login.", ip, user.Id, user.Email);

            // FIX: Единичен SaveChangesAsync — OTP записът и audit логът
            //      се записват заедно. Ако единият фейлне, двата се отменят.
            await _context.SaveChangesAsync();

            // FIX: Изпращаме имейла СЛЕД успешния SaveChanges.
            //      При грешка не пренасочваме към Verification — информираме потребителя.
            // БЪГ ФИКС (лагът): изпращането вече е в background опашка, за да
            // не чака браузърът SMTP handshake-а (до 15с timeout).
            //
            // КОМПРОМИС, съзнателен: преди тук се чакаше резултат и при
            // неуспех потребителят получаваше "Error_EmailSendFailed" вместо
            // да го пращаме към екрана за код. Сега това вече не е възможно —
            // грешката се логва, а потребителят разчита на бутона "Изпрати
            // нов код" на /Verification (който има собствен cooldown). Ако
            // предпочиташ старото поведение за вход, кажи и връщаме
            // синхронното изпращане само тук.
            await _mail.SendOtpAsync(
                toEmail:   email,
                firstName: user.FirstName ?? string.Empty,
                code:      otpCode,
                purpose:   ConferenceApp.Services.Email.OtpPurpose.Login,
                culture:   System.Globalization.CultureInfo.CurrentUICulture,
                baseUrl:   ConferenceApp.Services.Email.MailContext.BaseUrl(_config, Request));

            TempData["VerifyEmail"]   = email;
            TempData["VerifyPurpose"] = "Login";
            return RedirectToPage("/Verification");
        }

        // ── BLOCK ────────────────────────────────────────────────────────────────
        private async Task<bool> CheckBlockStatusAsync()
        {
            if (Request.Cookies.ContainsKey("LoginBlocked")) return true;

            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            return await _context.Set<AuditLog>()
                .AnyAsync(a =>
                    a.IpAddress == clientIp  &&
                    a.Action    == "IP Blocked" &&
                    a.Timestamp > DateTime.UtcNow.AddHours(-12));
        }

        private async Task HandlePermanentBlockAsync(ApplicationUser user, string ip)
        {
            QueueAudit("IP Blocked", $"IP {ip} blocked after failed admin attempts (12h).", ip, user.Id, user.Email);
            await _context.SaveChangesAsync();

            Response.Cookies.Append("LoginBlocked", "true", new CookieOptions
            {
                Expires  = DateTimeOffset.UtcNow.AddHours(12),
                HttpOnly = true,
                Secure   = true,
                SameSite = SameSiteMode.Strict
            });
        }

        // ── AUDIT HELPERS ─────────────────────────────────────────────────────────
        // FIX: QueueAudit само добавя към context — без SaveChangesAsync.
        //      Записва се заедно с останалите промени в един SaveChangesAsync.
        private void QueueAudit(string action, string details, string ip,
                                string? userId = null, string? email = null)
        {
            _context.Set<AuditLog>().Add(new AuditLog
            {
                UserId    = userId,
                UserEmail = email ?? "Unknown",
                Action    = action,
                Details   = details,
                IpAddress = ip,
                Timestamp = DateTime.UtcNow
            });
        }

        // Запазен за случаи където наистина трябва незабавен запис
        private async Task QueueAuditAsync(string action, string details, string ip,
                                           string? userId = null, string? email = null)
        {
            QueueAudit(action, details, ip, userId, email);
            await Task.CompletedTask; // placeholder — извикващият контролира SaveChanges
        }
    }
}