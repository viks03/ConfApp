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
    // Sign-in, in two flows that meet at OnPostAsync.
    //
    // Whether an account has a password decides which one runs: administrators
    // have one and sign in with it, participants do not and receive a one-time
    // code. The password flow is the one worth attacking, so it carries two
    // separate defences — the account lockout that Identity keeps
    // (MaxFailedAccessAttempts, 12 hours) and a block on the IP address, counted
    // from the audit rows. The code flow has neither: there is no password to
    // guess, only a code to wait for.
    [ValidateAntiForgeryToken]
    public class LoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly ConferenceApp.Services.Email.IMailComposer _mail;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<LoginModel> _logger;
        private readonly ConferenceApp.Services.AuditService _audit;
        private readonly IStringLocalizer _localizer;

        public LoginModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            ConferenceApp.Services.Email.IMailComposer mail,
            IConfiguration config,
            IWebHostEnvironment environment,
            ILogger<LoginModel> logger,
            IStringLocalizerFactory localizerFactory,
            ConferenceApp.Services.AuditService audit)
        {
            _signInManager  = signInManager;
            _userManager    = userManager;
            _context        = context;
            _mail = mail;
            _config = config;
            _environment    = environment;
            _logger         = logger;
            _localizer      = localizerFactory.Create("Pages.Login", Assembly.GetExecutingAssembly().GetName().Name!);
            _audit          = audit;
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
        // The state of the form — which address, whether a password is asked for,
        // which message is shown — travels in TempData, because every POST ends
        // in a redirect. That way a reload never resubmits the form.
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
                // The address goes into the audit row but not into the log line:
                // the log is read by whoever runs the server, the audit by whoever
                // investigates an account.
                _logger.LogWarning("Login attempt with non-existent email from {Ip}", clientIp);
                await QueueAuditAsync("Login Failed", $"Attempt with non-existent email: {cleanedEmail}", clientIp);
                await _context.SaveChangesAsync();

                TempData["StatusErr"] = _localizer["Error_UserNotFound"].Value;
                return RedirectToPage();
            }

            // Having a password is what marks an administrator: participants are
            // created without one and can only sign in with a code.
            bool isPasswordUser = await _userManager.HasPasswordAsync(user);

            if (isPasswordUser)
                return await HandleAdminLoginAsync(user, cleanedEmail, clientIp);
            else
                return await HandleOtpLoginAsync(user, cleanedEmail, clientIp);
        }

        // ── ADMINISTRATOR SIGN-IN, WITH A PASSWORD ────────────────────────────────
        private async Task<IActionResult> HandleAdminLoginAsync(ApplicationUser user, string email, string ip)
        {
            // [T-08] A locked account is recognised BEFORE anything else. This
            // path used not to ask about the lockout at all: the owner, typing
            // their correct password, was told it was wrong, went looking for a
            // problem in the password, and every further attempt added another
            // failure until their own address was blocked as well. The request
            // now stops here — neither AccessFailedCount nor the per-address
            // counter moves, because PasswordSignInAsync is never called.
            //
            // The attempt is still recorded, under a different action:
            // "Admin Login Failed" counts towards the block, this one does
            // not.
            if (await _userManager.IsLockedOutAsync(user))
            {
                QueueAudit("Admin Login Refused — Account Locked",
                    "Login attempt while the account is locked.", ip, user.Id, user.Email);
                await _context.SaveChangesAsync();

                TempData["LoginEmail"] = email;
                TempData["ReqPass"]    = true;
                TempData["StatusErr"]  = await LockoutMessageAsync(user);
                return RedirectToPage();
            }

            // Counted from the database rather than from a value carried along:
            // two concurrent attempts would otherwise each see the other's
            // starting point and neither would reach the limit.
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
                QueueAudit("Admin Login", "Successful password login.", ip, user.Id, user.Email);
                await _context.SaveChangesAsync();

                var roles = await _userManager.GetRolesAsync(user);
                return LocalRedirect(roles.Contains("Admin") ? "/Admin" : "/Profile");
            }

            QueueAudit("Admin Login Failed", "Invalid password attempt.", ip, user.Id, user.Email);
            await _context.SaveChangesAsync();

            // Re-counted after the row above is saved, so this attempt is
            // included.
            int newFailedCount = await _context.Set<AuditLog>()
                .CountAsync(a =>
                    a.IpAddress == ip    &&
                    a.UserEmail == email &&
                    a.Action    == "Admin Login Failed" &&
                    a.Timestamp > DateTime.UtcNow.AddHours(-12));

            // Blocking the address comes before the message about the locked
            // account: the block is the defence, the message the courtesy. From a
            // blocked address OnGetAsync shows Error_LoginRestricted anyway.
            if (newFailedCount >= 3)
            {
                await HandlePermanentBlockAsync(user, ip);
                return RedirectToPage();
            }

            // [T-08] This attempt locked the account without this address
            // reaching three failures — the attempts are spread across addresses.
            // The message says the account is locked rather than that the
            // password is wrong; otherwise the person keeps trying against a
            // locked account.
            if (result.IsLockedOut)
            {
                TempData["LoginEmail"] = email;
                TempData["ReqPass"]    = true;
                TempData["StatusErr"]  = await LockoutMessageAsync(user);
                return RedirectToPage();
            }

            TempData["LoginEmail"] = email;
            TempData["ReqPass"]    = true;
            TempData["StatusErr"]  = _localizer["Error_InvalidPassword"].Value;
            TempData["AdminMsg"]   = string.Format(_localizer["Error_AdminLoginDetected"].Value, 3 - newFailedCount);
            return RedirectToPage();
        }

        // ── PARTICIPANT SIGN-IN, WITH A ONE-TIME CODE ────────────────────────────
        private async Task<IActionResult> HandleOtpLoginAsync(ApplicationUser user, string email, string ip)
        {
            // A locked account — the 12-hour lockout after three wrong codes on
            // the Verification page — used not to be checked here at all: the
            // person simply entered their address again and got a new code, as
            // if the lockout did not exist.
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

            // Every unused code for this address is invalidated, so that only
            // the newest one works.
            //
            // [D-09] One UPDATE rather than loading every unused row into memory
            // and walking it with foreach. ExecuteUpdateAsync runs immediately,
            // outside the SaveChangesAsync below, so the two are wrapped in an
            // explicit transaction: without it a failed save would leave the
            // person with no valid code at all, the old one in their inbox
            // included.
            await using var tx = await _context.Database.BeginTransactionAsync();

            await _context.Set<OtpCode>()
                .Where(o => o.Email == email && !o.IsUsed)
                .ExecuteUpdateAsync(setters => setters.SetProperty(o => o.IsUsed, true));

            // RandomNumberGenerator, not Random: a code that can be predicted
            // from the previous one is not a second factor.
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

            // One SaveChangesAsync: the code and the audit row are written
            // together or not at all.
            await _context.SaveChangesAsync();

            await tx.CommitAsync();

            // The mail goes out after the commit: a code that reached somebody's
            // inbox but not the database would never be accepted.
            //
            // The send itself is queued in the background, so that the browser
            // does not wait through an SMTP handshake of up to 15 seconds. The
            // deliberate trade-off: this page can no longer tell the person that
            // the mail failed. A failure is logged and recorded in the queue
            // state (the Health tab shows it), and what the person has is the
            // "send a new code" button on /Verification, which has a cooldown of
            // its own.
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

        // ── LOCKOUT ──────────────────────────────────────────────────────────────

        /// <summary>
        /// "The account is locked. Try again in 11 h 42 min." — somebody who is
        /// not told how long simply tries again and adds more failures.
        /// </summary>
        private async Task<string> LockoutMessageAsync(ApplicationUser user)
        {
            var until = await _userManager.GetLockoutEndDateAsync(user);
            var left  = until.HasValue ? until.Value - DateTimeOffset.UtcNow : TimeSpan.Zero;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;

            return string.Format(_localizer["Error_AccountLockedFor"].Value, Remaining(left));
        }

        /// <summary>Hours and minutes, in the language of the page. The three
        /// forms exist because "0 h 42 min" reads as a fault.</summary>
        private string Remaining(TimeSpan left)
        {
            int hours   = (int)left.TotalHours;
            int minutes = left.Minutes;

            string Hours()   => string.Format(_localizer["Lockout_Hours"].Value, hours);
            string Minutes() => string.Format(_localizer["Lockout_Minutes"].Value, minutes);

            if (hours > 0 && minutes > 0) return Hours() + " " + Minutes();
            if (hours > 0)                return Hours();
            if (minutes > 0)              return Minutes();

            return _localizer["Lockout_LessThanMinute"].Value;
        }

        // ── ADDRESS BLOCK ────────────────────────────────────────────────────────
        // Two records of the same block: a cookie, which answers without a query,
        // and the audit row, which is the one that actually holds. Clearing the
        // cookie therefore does not lift the block.
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
        // QueueAudit only adds the row to the change tracker; it is written by
        // whichever SaveChangesAsync the caller reaches next, together with the
        // rest of that request's changes.
        private void QueueAudit(string action, string details, string ip,
                                string? userId = null, string? email = null)
        {
            _audit.Add(userId, email ?? "Unknown", action, details, ip);
        }

        // Identical to QueueAudit apart from being awaitable; it does NOT save.
        // Every caller still has to reach a SaveChangesAsync of its own.
        private async Task QueueAuditAsync(string action, string details, string ip,
                                           string? userId = null, string? email = null)
        {
            QueueAudit(action, details, ip, userId, email);
            await Task.CompletedTask;
        }
    }
}