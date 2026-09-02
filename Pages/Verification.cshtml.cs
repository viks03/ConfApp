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
    public class VerificationModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ConferenceApp.Services.Email.IMailComposer _mail;
        private readonly IConfiguration _config;
        private readonly IStringLocalizer _localizer;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<VerificationModel> _logger;

        public VerificationModel(
            ApplicationDbContext context, 
            UserManager<ApplicationUser> userManager, 
            SignInManager<ApplicationUser> signInManager,
            ConferenceApp.Services.Email.IMailComposer mail,
            IConfiguration config,
            IStringLocalizerFactory localizerFactory,
            IWebHostEnvironment environment,
            ILogger<VerificationModel> logger)
        {
            _context = context;
            _userManager = userManager;
            _signInManager = signInManager;
            _mail = mail;
            _config = config;
            _localizer = localizerFactory.Create("Pages.Verification", Assembly.GetExecutingAssembly().GetName().Name!);
            _environment = environment;
            _logger = logger;
        }

        [BindProperty]
        public string VerificationCode { get; set; } = string.Empty;

        public int TimeLeftSeconds { get; set; } = 0;
        public int ResendCooldownSeconds { get; set; } = 0;

        // НОВО: реалният времеви прозорец на кода (ExpirationTime - CreatedAt
        // на конкретния OtpCode ред), за да смятаме % на progress bar-а точно
        // спрямо истинската валидност, не спрямо hardcode-нато число в JS,
        // което би се разминало ако някога сменим прозореца от 15 мин.
        public int TotalWindowSeconds { get; set; } = 900;
        public string DisplayEmail { get; set; } = string.Empty;
        
        // НОВО: Пазим причината (Registration или Login), за да скриваме линковете в UI
        public string Purpose { get; set; } = string.Empty;

        public async Task<IActionResult> OnGetAsync()
        {
            // БЪГ ФИКС: без това, browser back-button (или bfcache) може да
            // покаже кеширана версия на страницата с отдавна остарял таймер
            // и заредени стари цифри от кода — особено подвеждащо точно в
            // сценария "напуснах страницата и се върнах по-късно".
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";

            var email = TempData["VerifyEmail"] as string;
            var purpose = TempData["VerifyPurpose"] as string ?? "Registration";
            Purpose = purpose; // Записваме го в модела

            if (string.IsNullOrEmpty(email)) return RedirectToPage("/Login");

            TempData.Keep("VerifyEmail");
            TempData.Keep("VerifyPurpose");
            
            DisplayEmail = HideEmail(email);
            await LoadTimers(email, purpose);
            
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var email = TempData["VerifyEmail"] as string;
            var purpose = TempData["VerifyPurpose"] as string ?? "Registration";
            Purpose = purpose;

            if (string.IsNullOrEmpty(email)) return RedirectToPage("/Login");

            TempData.Keep("VerifyEmail");
            TempData.Keep("VerifyPurpose");
            
            DisplayEmail = HideEmail(email);

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) return RedirectToPage("/Login");

            if (await _userManager.IsLockedOutAsync(user))
            {
                ModelState.AddModelError(string.Empty, _localizer["Error_AccountLocked"].Value);
                return Page();
            }

            if (string.IsNullOrEmpty(VerificationCode) || VerificationCode.Length != 6)
            {
                ModelState.AddModelError(string.Empty, _localizer["Error_CodeRequired"].Value);
                await LoadTimers(email, purpose);
                return Page();
            }

            var otpEntry = await _context.Set<OtpCode>()
                .Where(o => o.Email == email && o.Purpose == purpose && !o.IsUsed)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

            // ВАЖНО: проверката за "изтекъл/несъществуващ код" трябва да е ПРЕДИ
            // проверката за грешни цифри. Иначе потребител, чийто код просто е
            // изтекъл (напр. се е разсеял >15 мин и се е върнал на страницата),
            // получава lockout strike за нещо, което изобщо не е "грешен опит" —
            // а при MaxFailedAccessAttempts=3 това означава 12ч самоблокировка
            // само защото кодът е остарял, не защото е познавал грешно.
            if (otpEntry == null || DateTime.UtcNow > otpEntry.ExpirationTime)
            {
                ModelState.AddModelError(string.Empty, _localizer["Error_CodeExpired"].Value);
                await LoadTimers(email, purpose);
                return Page();
            }

            if (otpEntry.Code != VerificationCode)
            {
                await _userManager.AccessFailedAsync(user); 
                int failedAttempts = await _userManager.GetAccessFailedCountAsync(user); 

                _context.Set<AuditLog>().Add(new AuditLog { 
                    UserId = user.Id, 
                    UserEmail = email, 
                    Action = "Verification Failed", 
                    Details = $"Invalid code entered ({purpose}). Attempt {failedAttempts} of {_userManager.Options.Lockout.MaxFailedAccessAttempts}.",
                    IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown" 
                });
                await _context.SaveChangesAsync();

                // НОВО: показваме колко опита остават, преди 12ч lockout — същия
                // принцип, който вече ползва admin login flow-a в Login.cshtml.cs.
                int attemptsLeft = _userManager.Options.Lockout.MaxFailedAccessAttempts - failedAttempts;
                if (attemptsLeft > 0)
                {
                    ModelState.AddModelError(string.Empty,
                        string.Format(_localizer["Error_InvalidCodeWithAttempts"].Value, attemptsLeft));
                }
                else
                {
                    ModelState.AddModelError(string.Empty, _localizer["Error_AccountLocked"].Value);
                }

                await LoadTimers(email, purpose);
                return Page();
            }

            user.EmailConfirmed = true;
            await _userManager.ResetAccessFailedCountAsync(user); 
            await _userManager.UpdateAsync(user);

            otpEntry.IsUsed = true;

            _context.Set<AuditLog>().Add(new AuditLog { 
                UserId = user.Id, 
                UserEmail = email, 
                Action = purpose == "Login" ? "Login" : "Email Verified", 
                Details = purpose == "Login" ? "Successful login with OTP code." : "User successfully verified email address.",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown" 
            });

            await _context.SaveChangesAsync(); 

            TempData.Remove("VerifyEmail");
            TempData.Remove("VerifyPurpose");

            await _signInManager.SignInAsync(user, isPersistent: true);
            
            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains("Admin")) return LocalRedirect("/Admin");

            // НОВО: /Done е междинна "добре дошъл" стъпка само за завършена
            // регистрация. Login (потребител, който вече си има профил и просто
            // влиза през OTP) отива директно в профила — няма "Done" стъпка,
            // защото не се "завършва" нищо ново в този момент.
            // НОВО: флаг, който /Done чете само веднъж (виж OnGetAsync там) —
            // без него всеки логнат потребител можеше да отвори /Done директно
            // от адресната лента по всяко време, не само веднага след успешна
            // регистрация.
            TempData["JustRegistered"] = true;

            return purpose == "Registration"
                ? LocalRedirect("/Done")
                : LocalRedirect("/Profile");
        }

        public async Task<IActionResult> OnPostResendAsync()
        {
            var email = TempData["VerifyEmail"] as string;
            var purpose = TempData["VerifyPurpose"] as string ?? "Registration";
            Purpose = purpose;

            if (string.IsNullOrEmpty(email)) return RedirectToPage("/Login");

            TempData.Keep("VerifyEmail");
            TempData.Keep("VerifyPurpose");

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) return RedirectToPage("/Login");

            // БЪГ ФИКС: заключен акаунт (след 3 грешни опита на кода) преди
            // изобщо не се проверяваше тук — заявка за нов код спокойно
            // минаваше и изпращаше имейл, докато акаунтът е в 12ч lockout.
            if (await _userManager.IsLockedOutAsync(user))
            {
                TempData["ErrorMessage"] = _localizer["Error_AccountLocked"].Value;
                return RedirectToPage();
            }

            var recentOtpsCount = await _context.Set<OtpCode>()
                .CountAsync(o => o.Email == email && o.Purpose == purpose && o.CreatedAt >= DateTime.UtcNow.AddMinutes(-30));

            if (recentOtpsCount >= 3)
            {
                TempData["ErrorMessage"] = _localizer["Error_TooManyEmails"].Value;
                return RedirectToPage();
            }

            var oldCodes = await _context.Set<OtpCode>().Where(o => o.Email == email && !o.IsUsed).ToListAsync();
            foreach (var c in oldCodes) c.IsUsed = true;

            string newOtp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            _context.Set<OtpCode>().Add(new OtpCode { Email = email, Code = newOtp, ExpirationTime = DateTime.UtcNow.AddMinutes(15), Purpose = purpose });

            _context.Set<AuditLog>().Add(new AuditLog { 
                UserEmail = email, 
                UserId = user?.Id, 
                Action = "Resend OTP", 
                Details = $"New verification code generated for: {purpose}.",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown" 
            });
            
            await _context.SaveChangesAsync();

            // Повторното изпращане минава през същия път като първото —
            // виж Services/Email/. Purpose определя кой текст се ползва.
            await _mail.SendOtpAsync(
                toEmail:   email,
                firstName: user?.FirstName ?? string.Empty,
                code:      newOtp,
                purpose:   purpose == "Login"
                               ? ConferenceApp.Services.Email.OtpPurpose.Login
                               : ConferenceApp.Services.Email.OtpPurpose.Registration,
                culture:   System.Globalization.CultureInfo.CurrentUICulture,
                baseUrl:   ConferenceApp.Services.Email.MailContext.BaseUrl(_config, Request));

            TempData["SuccessMessage"] = _localizer["Success_CodeResent"].Value;

            return RedirectToPage(); 
        }

        private async Task LoadTimers(string email, string purpose)
        {
            var lastOtp = await _context.Set<OtpCode>()
                .Where(o => o.Email == email && o.Purpose == purpose && !o.IsUsed)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

            if (lastOtp != null)
            {
                var diffMain = (lastOtp.ExpirationTime - DateTime.UtcNow).TotalSeconds;
                TimeLeftSeconds = diffMain > 0 ? (int)diffMain : 0;

                var diffResend = (DateTime.UtcNow - lastOtp.CreatedAt).TotalSeconds;
                ResendCooldownSeconds = diffResend < 60 ? 60 - (int)diffResend : 0;

                var window = (lastOtp.ExpirationTime - lastOtp.CreatedAt).TotalSeconds;
                TotalWindowSeconds = window > 0 ? (int)window : 900;
            }
            else
            {
                TimeLeftSeconds = 0;
                ResendCooldownSeconds = 0;
            }
        }

        private string HideEmail(string email) => ConferenceApp.Helpers.EmailMaskHelper.Mask(email);
    }
}