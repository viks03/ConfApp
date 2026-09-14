// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;
using ConferenceApp.Data;
using ConferenceApp.Helpers;
using ConferenceApp.Models;
using ConferenceApp.Services;
using ConferenceApp.Services.Files;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Pages
{
    // Registration, as a three-phase wizard on one page: contact details,
    // participation and paper, consent. Each POST validates only the phase it
    // is on and comes back as a rendered page — there is no redirect between
    // phases, so the answers live in the form itself.
    //
    // Two consequences run through the whole file. The phase number comes from
    // the client, so it is clamped and the real registration happens only in
    // phase 3. And the paper cannot travel in a hidden field, so it is written
    // to disk as soon as phase 2 is left; what travels afterwards is its path,
    // sealed with the Data Protection key so that the server accepts back only
    // what it issued itself.
    public class RegisterModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<RegisterModel> _logger;
        private readonly ConferenceApp.Services.AuditService _audit;
        private readonly ConferenceApp.Services.Email.IMailComposer _mail;
        private readonly IConfiguration _config;
        private readonly IStringLocalizer _localizer;
        private readonly IUploadPaths _uploadPaths;
        private readonly IDataProtector _pathProtector;

        public RegisterModel(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            ILogger<RegisterModel> logger,
            ConferenceApp.Services.Email.IMailComposer mail,
            IConfiguration config,
            IStringLocalizerFactory localizerFactory,
            IUploadPaths uploadPaths,
            IDataProtectionProvider dataProtection,
            ConferenceApp.Services.AuditService audit)
        {
            _uploadPaths   = uploadPaths;
            _pathProtector = dataProtection.CreateProtector("ConferenceApp.Register.SavedFilePath");
            _userManager = userManager;
            _context = context;
            _environment = environment;
            _logger = logger;
            _mail = mail;
            _config = config;

            _localizer = localizerFactory.Create("Pages.Register", Assembly.GetExecutingAssembly().GetName().Name!);
            _audit = audit;
        }

        // 1, 2 or 3. It arrives from a hidden input, so every handler clamps it
        // before using it.
        [BindProperty]
        public int Phase { get; set; } = 1;

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public class InputModel
        {
            // This class used to carry NO validation attribute at all, so
            // `ModelState.IsValid` was always true and the only real check was
            // validation.js in the browser — trivially bypassed with JavaScript
            // off, or with curl. The messages are keys from
            // Pages.Register.*.resx, translated in OnPostAsync through
            // ModelState.LocalizeErrors.
            [Required(ErrorMessage = "Error_Required")]
            [StringLength(100, MinimumLength = 2, ErrorMessage = "Error_NameLength")]
            [RegularExpression(@"^[A-Za-z\s\-']+$", ErrorMessage = "Error_NameLatinOnly")]
            public string FirstName { get; set; } = string.Empty;

            [Required(ErrorMessage = "Error_Required")]
            [StringLength(100, MinimumLength = 2, ErrorMessage = "Error_NameLength")]
            [RegularExpression(@"^[A-Za-z\s\-']+$", ErrorMessage = "Error_NameLatinOnly")]
            public string LastName { get; set; } = string.Empty;

            // Age is a string here because it arrives from a text input: as an
            // int, a non-numeric value would fail model binding with a framework
            // message nobody can translate. The regex checks the shape, and
            // ValidateExtras checks the range.
            [Required(ErrorMessage = "Error_Required")]
            [RegularExpression(@"^\d{1,3}$", ErrorMessage = "Error_AgeRange")]
            public string Age { get; set; } = string.Empty;

            [Required(ErrorMessage = "Error_Required")]
            [StringLength(100, MinimumLength = 2, ErrorMessage = "Error_NameLength")]
            public string AcademicTitle { get; set; } = string.Empty;

            [Required(ErrorMessage = "Error_Required")]
            [EmailAddress(ErrorMessage = "Error_InvalidEmail")]
            [StringLength(256)]
            public string Email { get; set; } = string.Empty;

            [Required(ErrorMessage = "Error_Required")]
            [RegularExpression(@"^[\d\s\+\-\(\)]{8,20}$", ErrorMessage = "Error_PhoneFormat")]
            public string Phone { get; set; } = string.Empty;

            [Required(ErrorMessage = "Error_Required")]
            [StringLength(200, MinimumLength = 2, ErrorMessage = "Error_NameLength")]
            public string Workplace { get; set; } = string.Empty;

            [Required(ErrorMessage = "Error_Required")]
            [RegularExpression(@"^[1-4]$", ErrorMessage = "Error_InvalidPartForm")]
            public string PartForm { get; set; } = string.Empty;

            public bool IsForeigner { get; set; }
            public bool IsGDPR { get; set; }
            public bool IsMarketing { get; set; }
            public bool ConsentToPublishPaper { get; set; }
            public IFormFile? UploadedFile { get; set; }

            // The path of the already-uploaded paper, carried from phase 2 to
            // phase 3. An IFormFile cannot survive in a hidden input, so the file
            // is written on leaving phase 2 and only its path travels — sealed,
            // never as plain text. See SealPath.
            public string? SavedFilePath { get; set; }
        }

        // ── The participation type preset from the URL (?type=…) ──
        // The links on /Attend carry it, so that someone who chose a tier there
        // does not have to choose again. The values are the PartForm column.
        private static readonly Dictionary<string, string> PartFormTypeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "lector",     "1" },
            { "student",    "2" },
            { "online",     "3" },
            { "journalist", "4" }
        };

        // A signed-in person has nothing to register: they are sent to their
        // own page instead of to an empty form.
        public IActionResult OnGet([FromQuery] string? type)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole("Admin")) return LocalRedirect("/Admin");
                return LocalRedirect("/Profile");
            }

            if (!string.IsNullOrWhiteSpace(type) && PartFormTypeMap.TryGetValue(type.Trim(), out var partValue))
            {
                Input.PartForm = partValue;
            }

            Phase = 1;
            return Page();
        }

        // ── The "Back" button: lowers the phase, validates nothing ───────────
        // ModelState is cleared first, or the errors from the phase being left
        // would be rendered on the phase being returned to.
        public IActionResult OnPostBack()
        {
            ModelState.Clear();
            // The same clamp as in OnPostAsync: Phase comes from the client.
            if (Phase < 1 || Phase > 3) Phase = 1;
            Phase = Math.Max(1, Phase - 1);
            return Page();
        }

        // ── Is this address free (called from the form as it is typed) ───────
        // A convenience only. The real check is in phase 3, where a second
        // registration with the same address is refused — between this call and
        // the submission somebody else can take it.
        public async Task<JsonResult> OnGetCheckEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return new JsonResult(new { isAvailable = true });

            var cleanedEmail = email.Trim().ToLower();
            var user = await _userManager.FindByEmailAsync(cleanedEmail);

            if (user != null)
                return new JsonResult(new { isAvailable = false, message = _localizer["Error_EmailTaken"].Value });

            return new JsonResult(new { isAvailable = true });
        }

        // BCE2026-XXXXXYYY, five digits and three letters. This is the number
        // both payment gateways send back, and the one a participant quotes when
        // something goes wrong, so it has to be unique and short enough to read
        // out. The unique index on the column ([D-03]) is what guarantees the
        // first of those; this loop only avoids losing a registration to an
        // unlucky draw.
        private async Task<string> GenerateUniqueReferenceNumberAsync()
        {
            const string chars      = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            const int    maxAttempts = 10;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var digits = System.Security.Cryptography.RandomNumberGenerator
                    .GetInt32(10000, 100000)
                    .ToString();

                var suffix = new string(Enumerable
                    .Range(0, 3)
                    .Select(_ => chars[System.Security.Cryptography.RandomNumberGenerator.GetInt32(chars.Length)])
                    .ToArray());

                var candidate = $"BCE2026-{digits}{suffix}";

                var exists = await _userManager.Users
                    .AnyAsync(u => u.ReferenceNumber == candidate);

                if (!exists)
                    return candidate;

                _logger.LogWarning(
                    "ReferenceNumber collision on attempt {Attempt}: {Candidate}. Retrying...",
                    attempt + 1, candidate);
            }

            throw new InvalidOperationException(
                $"Could not generate a unique ReferenceNumber after {maxAttempts} attempts.");
        }

        // ── Which field belongs to which phase ───────────────────────────────
        // Used by KeepOnlyPhaseErrors: on phase 1 nothing about phase 2 and 3 is
        // an error yet.
        private static readonly string[] Phase1Fields =
            { "Input.FirstName", "Input.LastName", "Input.Age", "Input.AcademicTitle", "Input.Email", "Input.Phone" };
        private static readonly string[] Phase2Fields =
            { "Input.Workplace", "Input.PartForm", "Input.UploadedFile" };
        private static readonly string[] Phase3Fields =
            { "Input.IsGDPR" };

        private static readonly string[] AllowedFileExtensions = { ".pdf", ".doc", ".docx" };

        /// <summary>The same ceiling as in <c>/Profile</c> — see [T-16]. Both
        /// sit well under the request limit in Program.cs, so an oversized file
        /// gets this message rather than an empty response.</summary>
        private const long MaxPaperBytes = 10 * 1024 * 1024;

        /// <summary>The size for the message, rounded the way validation.js
        /// rounds it: the same file must not be reported as 10.4 MB by the
        /// browser and 10.44 MB by the server.</summary>
        private static string MegabytesOf(long bytes) =>
            (bytes / 1024d / 1024d).ToString("0.0",
                System.Globalization.CultureInfo.InvariantCulture);


        /// <summary>
        /// Keeps in ModelState only the errors for the fields of the current
        /// phase — otherwise phase 1 would show errors for phase 2 and 3 fields
        /// the person has not even seen yet.
        /// </summary>
        private void KeepOnlyPhaseErrors(int phase)
        {
            var keep = phase switch
            {
                1 => Phase1Fields,
                2 => Phase2Fields,
                _ => Phase1Fields.Concat(Phase2Fields).Concat(Phase3Fields).ToArray()
            };

            foreach (var key in ModelState.Keys.ToList())
            {
                if (!keep.Contains(key)) ModelState.Remove(key);
            }
        }

        /// <summary>
        /// The checks an attribute cannot express: the numeric range of the age,
        /// and the extension and size of the file.
        /// </summary>
        private void ValidateExtras(int phase)
        {
            if (Phase1Fields.Contains("Input.Age") && (phase == 1 || phase == 3))
            {
                if (int.TryParse(Input.Age, out int parsedAge) && (parsedAge < 18 || parsedAge > 100))
                    ModelState.AddModelError("Input.Age", _localizer["Error_AgeRange"].Value);
            }

            if ((phase == 2 || phase == 3) && Input.UploadedFile != null)
            {
                var ext = Path.GetExtension(Input.UploadedFile.FileName).ToLowerInvariant();
                if (!AllowedFileExtensions.Contains(ext))
                    ModelState.AddModelError("Input.UploadedFile", _localizer["Error_InvalidFileType"].Value);

                // [T-16] The server used to cut off at 25 MB while the hint under
                // the field, the error text, validation.js and the profile page
                // all said 10 — so a paper between 10 and 25 MB was accepted at
                // registration and could then never be replaced from the profile.
                // [T-15] The size is passed into the message: the {0} placeholder
                // had always been there, but only the script filled it in.
                if (Input.UploadedFile.Length > MaxPaperBytes)
                    ModelState.AddModelError("Input.UploadedFile",
                        _localizer["Error_FileTooLarge", MegabytesOf(Input.UploadedFile.Length)].Value);
            }
        }

        /// <summary>
        /// Writes the uploaded file at once (on leaving phase 2) and returns its
        /// relative path. An IFormFile cannot travel in a hidden input between
        /// phases, so the file is materialised here and only the path goes on.
        ///
        /// The name is built from the person's own name plus eight characters of
        /// a GUID: recognisable in the folder, and impossible to guess from
        /// outside.
        /// </summary>
        private async Task<string?> PersistUploadedFileAsync()
        {
            if (Input.UploadedFile == null || Input.UploadedFile.Length == 0) return null;

            string uploadsFolder = _uploadPaths.EnsureDirectory("uploads", "papers26");

            string safeFirst = new string((Input.FirstName ?? "").Where(char.IsLetterOrDigit).ToArray());
            string safeLast = new string((Input.LastName ?? "").Where(char.IsLetterOrDigit).ToArray());
            string ext = Path.GetExtension(Input.UploadedFile.FileName).ToLowerInvariant();
            string fileName = $"{safeFirst}{safeLast}_{Guid.NewGuid().ToString()[..8]}{ext}";
            string fullPath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
                await Input.UploadedFile.CopyToAsync(stream);

            return _uploadPaths.ToRelative("uploads", "papers26", fileName);
        }

        /// <summary>
        /// The path of the stored paper travels between phases through a hidden
        /// input, that is, through the client. It is therefore not sent as text
        /// but sealed with the Data Protection key: the server accepts back only
        /// what it issued itself ([A-01]). A forged or borrowed value does not
        /// unseal and is treated as "no file".
        /// </summary>
        private string? SealPath(string? relativePath) =>
            string.IsNullOrEmpty(relativePath) ? null : _pathProtector.Protect(relativePath);

        private string? UnsealPath(string? sealedPath)
        {
            if (string.IsNullOrEmpty(sealedPath)) return null;
            try
            {
                return _pathProtector.Unprotect(sealedPath);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                _logger.LogWarning("Register: невалиден SavedFilePath токен — игнориран.");
                return null;
            }
        }

        /// <summary>
        /// Deletes an already-stored file. Used when the registration fails
        /// after the file has been written, and when a second upload replaces the
        /// first, so that no orphans are left on disk.
        /// </summary>
        private void DeleteSavedFileIfAny(string? relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return;
            try
            {
                var full = _uploadPaths.ToPhysical(relativePath);
                if (full != null && System.IO.File.Exists(full)) System.IO.File.Delete(full);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete orphaned upload {Path}.", relativePath);
            }
        }

        // ── The wizard: validate, advance, and on phase 3 register ────────────
        public async Task<IActionResult> OnPostAsync()
        {
            // Phase comes from a hidden input, that is, from the client. Without
            // this clamp a direct POST with Phase=99 — or Phase=3 with empty
            // fields — skipped the whole wizard. The validation below is real
            // now, but the value itself is bounded as well.
            if (Phase < 1 || Phase > 3) Phase = 1;

            ModelState.LocalizeErrors(_localizer, "Error_");
            ValidateExtras(Phase);
            KeepOnlyPhaseErrors(Phase);

            // [T-09] A request with NO Input.* field at all never creates the
            // Input object, so ModelState holds nothing about it and IsValid is
            // true for lack of questions — the wizard advanced empty-handed. The
            // address travels through all three phases (as a hidden field on 2
            // and 3), so its absence means "this request did not come from the
            // form". The check sits after KeepOnlyPhaseErrors so that it applies
            // on all three phases.
            if (string.IsNullOrWhiteSpace(Input.Email))
                ModelState.AddModelError("Input.Email", _localizer["Error_Required"].Value);

            if (!ModelState.IsValid) return Page();

            // ── Phases 1 and 2: advance once the current phase is valid ──────
            if (Phase < 3)
            {
                // The paper used to be lost silently between phases 2 and 3, an
                // IFormFile not surviving a hidden input. It is written here and
                // only its path goes on, in Input.SavedFilePath.
                if (Phase == 2 && Input.UploadedFile != null && Input.UploadedFile.Length > 0)
                {
                    try
                    {
                        DeleteSavedFileIfAny(UnsealPath(Input.SavedFilePath)); // a previous choice, now replaced
                        Input.SavedFilePath = SealPath(await PersistUploadedFileAsync());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error saving uploaded file during phase 2.");
                        ModelState.AddModelError("Input.UploadedFile", _localizer["Error_FileUploadFailed"].Value);
                        return Page();
                    }
                }

                Phase++;
                ModelState.Clear();
                return Page();
            }

            // ── Phase 3: the registration itself ──────────────────────────────
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            if (!Input.IsGDPR)
            {
                ModelState.AddModelError("Input.IsGDPR", _localizer["Error_GDPR"].Value);
                return Page();
            }

            // Twenty completed registrations an hour from one address. This is
            // not the rate limiter in Program.cs, which counts POSTs: that one
            // protects the file write in phase 2, this one the account creation.
            var registrationsFromIp = await _context.Set<AuditLog>()
                .CountAsync(a => a.IpAddress == clientIp
                              && a.Action == "User Registered"
                              && a.Timestamp >= DateTime.UtcNow.AddHours(-1));

            if (registrationsFromIp >= 20)
            {
                ModelState.AddModelError(string.Empty, _localizer["Error_TooManyRegistrations"].Value);
                return Page();
            }

            var cleanedEmail = Input.Email.Trim().ToLower();
            var existingUser = await _userManager.FindByEmailAsync(cleanedEmail);

            if (existingUser != null)
            {
                ModelState.AddModelError("Input.Email", _localizer["Error_EmailTaken"].Value);
                return Page();
            }

            // The file is usually written in phase 2; if it was chosen only now,
            // or the choice was changed, it is written here.
            string? savedFilePath = UnsealPath(Input.SavedFilePath);
            if (Input.UploadedFile != null && Input.UploadedFile.Length > 0)
            {
                try
                {
                    DeleteSavedFileIfAny(savedFilePath);
                    savedFilePath = await PersistUploadedFileAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error uploading file.");
                    ModelState.AddModelError(string.Empty, _localizer["Error_FileUploadFailed"].Value);
                    return Page();
                }
            }

            var user = new ApplicationUser
            {
                UserName = cleanedEmail,
                Email = cleanedEmail,
                FirstName = Input.FirstName,
                LastName = Input.LastName,
                Age = int.TryParse(Input.Age, out int age) ? age : 0,
                AcademicTitle = Input.AcademicTitle,
                PhoneNumber = Input.Phone,
                Workplace = Input.Workplace,
                PartForm = Input.PartForm,
                IsForeigner = Input.IsForeigner,
                HasAcceptedGdpr = Input.IsGDPR,
                WantsMarketing = Input.IsMarketing,
                GdprConsentDate = DateTime.UtcNow,
                MarketingConsentDate = Input.IsMarketing ? DateTime.UtcNow : null,
                ConsentToPublishPaper = Input.ConsentToPublishPaper,
                // The date is stored only when consent was actually given: a date
                // next to a "no" would be a record of something that did not
                // happen.
                PublishConsentDate = Input.ConsentToPublishPaper ? DateTime.UtcNow : null,
                CreatedAt = DateTime.UtcNow,
                EmailConfirmed = false,
                PaperFilePath = savedFilePath,

                // [T-29] The language the form was filled in is the best guess we
                // have at this moment — and the only thing we know later, when
                // the mail is triggered by an administrator or by a webhook. If
                // they switch language from the bar, LanguageController updates
                // it.
                PreferredLanguage = ConferenceApp.Services.Email.MailContext.Normalize(
                    System.Globalization.CultureInfo.CurrentUICulture.Name)
            };

            var result = await _userManager.CreateAsync(user);

            if (result.Succeeded)
            {
                try
                {
                    user.ReferenceNumber = await GenerateUniqueReferenceNumberAsync();
                    await _userManager.UpdateAsync(user);

                    _logger.LogInformation(
                        "ReferenceNumber {RefNum} assigned to user {Email}.",
                        user.ReferenceNumber, cleanedEmail);
                }
                catch (InvalidOperationException ex)
                {
                    // The account is created and must not be thrown away over
                    // this: the person can register only once. The row is left
                    // with an empty ReferenceNumber — see [A-09], and the
                    // filtered unique index in ApplicationDbContext that allows
                    // exactly this case.
                    _logger.LogError(ex, "Failed to generate ReferenceNumber for user {Email}.", cleanedEmail);
                }

                string otpCode = System.Security.Cryptography.RandomNumberGenerator
                    .GetInt32(100000, 999999).ToString();

                _context.Set<OtpCode>().Add(new OtpCode
                {
                    Email = cleanedEmail,
                    Code = otpCode,
                    ExpirationTime = DateTime.UtcNow.AddMinutes(15),
                    Purpose = "Registration"
                });

                // The audit row spells the participation form out: the numbers
                // mean nothing to whoever reads the log a year later.
                string partFormDisplay = Input.PartForm switch
                {
                    "1" => "Lector / Academic",
                    "2" => "Student / PhD Candidate",
                    "3" => "Online Participant",
                    "4" => "Journalist / Media",
                    _ => Input.PartForm
                };

                string hasFile = savedFilePath != null ? "Yes" : "No";
                string isMarketing = Input.IsMarketing ? "Yes" : "No";
                string consentPublish = Input.ConsentToPublishPaper ? "Yes" : "No";

                // Add, not LogAsync: the SaveChangesAsync just below is the one
                // for this row and for the OTP code together.
                _audit.Add(user.Id, cleanedEmail, "User Registered",
                    $"Ref: {user.ReferenceNumber} | Participation: {partFormDisplay} | File: {hasFile} | PublishConsent: {consentPublish} | GDPR: Yes | Marketing: {isMarketing}",
                    clientIp);

                await _context.SaveChangesAsync();

                // Sent after the save: a code in somebody's inbox that is not in
                // the database would never be accepted. The whole block that
                // assembled the mail — read the file, seven .Replace() calls,
                // localization, try/catch — used to live here, in Login and in
                // Verification, three copies of the same code.
                await _mail.SendOtpAsync(
                    toEmail:   cleanedEmail,
                    firstName: Input.FirstName,
                    code:      otpCode,
                    purpose:   ConferenceApp.Services.Email.OtpPurpose.Registration,
                    culture:   System.Globalization.CultureInfo.CurrentUICulture,
                    baseUrl:   ConferenceApp.Services.Email.MailContext.BaseUrl(_config, Request));

                TempData["VerifyEmail"] = cleanedEmail;
                TempData["VerifyPurpose"] = "Registration";
                return RedirectToPage("/Verification");
            }

            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            // The registration failed, so the stored paper has no owner: it is
            // deleted rather than left as an orphan on disk.
            DeleteSavedFileIfAny(savedFilePath);
            Input.SavedFilePath = null;

            return Page();
        }
    }
}