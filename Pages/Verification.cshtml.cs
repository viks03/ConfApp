// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
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
    // The one-time code step, shared by registration and sign-in. Which of the
    // two it is comes from TempData["VerifyPurpose"], set by the page that
    // redirected here, and decides three things: which resx text the mail uses,
    // which OtpCode rows count, and where a successful check leads.
    //
    // The address is carried in TempData rather than in the URL or a hidden
    // field: it must not be something the person on this page can change.
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
        private readonly ConferenceApp.Services.AuditService _audit;

        public VerificationModel(
            ApplicationDbContext context, 
            UserManager<ApplicationUser> userManager, 
            SignInManager<ApplicationUser> signInManager,
            ConferenceApp.Services.Email.IMailComposer mail,
            IConfiguration config,
            IStringLocalizerFactory localizerFactory,
            IWebHostEnvironment environment,
            ILogger<VerificationModel> logger,
            ConferenceApp.Services.AuditService audit)
        {
            _context = context;
            _userManager = userManager;
            _signInManager = signInManager;
            _mail = mail;
            _config = config;
            _localizer = localizerFactory.Create("Pages.Verification", Assembly.GetExecutingAssembly().GetName().Name!);
            _environment = environment;
            _logger = logger;
            _audit = audit;
        }

        [BindProperty]
        public string VerificationCode { get; set; } = string.Empty;

        public int TimeLeftSeconds { get; set; } = 0;
        public int ResendCooldownSeconds { get; set; } = 0;

        // The real lifetime of this code (ExpirationTime - CreatedAt of the
        // row), so that the progress bar is drawn against the validity that
        // actually applies rather than against a number hard-coded in the
        // JavaScript, which would drift if the 15-minute window ever changed.
        public int TotalWindowSeconds { get; set; } = 900;
        public string DisplayEmail { get; set; } = string.Empty;
        
        // "Registration" or "Login" — the view hides the links that do not
        // apply to the current one.
        public string Purpose { get; set; } = string.Empty;

        public async Task<IActionResult> OnGetAsync()
        {
            // Without this the back button (or the bfcache) can redisplay a
            // cached copy of the page with a long-expired timer and the old
            // digits still in the field — exactly in the case that matters: "I
            // left the page and came back later".
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";

            var email = TempData["VerifyEmail"] as string;
            var purpose = TempData["VerifyPurpose"] as string ?? "Registration";
            Purpose = purpose;

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

            // Order matters: "expired or missing code" is checked BEFORE the
            // digits are compared. The other way round, somebody whose code has
            // simply expired — they were away for more than 15 minutes and came
            // back — takes a lockout strike for something that is not a wrong
            // guess at all. With MaxFailedAccessAttempts at 3 that is a 12-hour
            // lockout earned by being slow.
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

                await _audit.LogAsync(user.Id, email, "Verification Failed",
                    $"Invalid code entered ({purpose}). Attempt {failedAttempts} of {_userManager.Options.Lockout.MaxFailedAccessAttempts}.");

                // The number of attempts left is shown, as in the administrator
                // sign-in flow (Login.cshtml.cs): a lockout that arrives without
                // warning reads as a broken site.
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

            // Add, not LogAsync: otpEntry.IsUsed is saved by the same
            // SaveChanges, so the code is marked used and the audit row written
            // together or not at all.
            _audit.Add(user.Id, email,
                purpose == "Login" ? "Login" : "Email Verified",
                purpose == "Login" ? "Successful login with OTP code." : "User successfully verified email address.");

            await _context.SaveChangesAsync(); 

            TempData.Remove("VerifyEmail");
            TempData.Remove("VerifyPurpose");

            await _signInManager.SignInAsync(user, isPersistent: true);
            
            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains("Admin")) return LocalRedirect("/Admin");

            // /Done is a welcome step for a completed registration only.
            // Signing in with a code finishes nothing new, so it goes straight
            // to the profile.
            //
            // [T-04] The flag is set ONLY for registration. It used to be set on
            // an ordinary sign-in too, which meant a returning user could open
            // /Done and be congratulated on registering — the very thing the
            // flag exists to prevent. /Done reads it exactly once (see
            // OnGetAsync there).
            if (purpose != "Registration") return LocalRedirect("/Profile");

            TempData["JustRegistered"] = true;
            return LocalRedirect("/Done");
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

            // A locked account — three wrong codes — used not to be checked
            // here at all: asking for a new code went through and sent mail
            // while the account was in its 12-hour lockout.
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

            // [D-09] One UPDATE instead of N rows loaded into memory, as in
            // Login. ExecuteUpdateAsync runs immediately, outside the
            // SaveChangesAsync below, so the two are wrapped in an explicit
            // transaction — otherwise a failed save leaves the user with neither
            // a new code nor the old one.
            await using var tx = await _context.Database.BeginTransactionAsync();

            await _context.Set<OtpCode>()
                .Where(o => o.Email == email && !o.IsUsed)
                .ExecuteUpdateAsync(setters => setters.SetProperty(o => o.IsUsed, true));

            string newOtp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999).ToString();
            _context.Set<OtpCode>().Add(new OtpCode { Email = email, Code = newOtp, ExpirationTime = DateTime.UtcNow.AddMinutes(15), Purpose = purpose });

            // Add, not LogAsync: the new OtpCode is saved by the same
            // SaveChanges, inside the same transaction.
            _audit.Add(user?.Id, email, "Resend OTP",
                $"New verification code generated for: {purpose}.");
            
            await _context.SaveChangesAsync();

            await tx.CommitAsync();

            // Sent after the transaction commits: a mail cannot be taken back,
            // so the code it carries has to be in the database first.
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

        // Both timers on the page come from the same row: how long the code is
        // still valid, and how long before "resend" may be pressed again. They
        // are computed on the server, so a reload cannot reset them.
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

                // One minute between codes. The real limit is the three per 30
                // minutes enforced in OnPostResendAsync; this only stops the
                // button being pressed twice in a row.
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