// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Data;
using ConferenceApp.Helpers;
using Microsoft.EntityFrameworkCore;
using ConferenceApp.Models;
using ConferenceApp.Services.Files;
using Microsoft.AspNetCore.Authorization;
using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ConferenceApp.Pages
{
    // The participant's own page: their details, their paper, and the status
    // panel that tells them what is still expected of them.
    //
    // The participation form is the pivot. Forms 1 and 3 pay; forms 2 and 4
    // prove who they are instead. Once either has happened — a payment
    // confirmed, a document submitted — the form is locked, because changing it
    // afterwards would mean a paid participant becoming one who owes nothing, or
    // the reverse.
    [Authorize]
    public class ProfileModel : PageModel
    {
        // 10 MB — the same limit as in Register and in the client-side
        // validation.
        private const long MaxPaperBytes = 10 * 1024 * 1024;

        /// <summary>The size for the message, rounded the way validation.js
        /// rounds it, so that the browser and the server do not report the same
        /// file differently.</summary>
        private static string MegabytesOf(long bytes) =>
            (bytes / 1024d / 1024d).ToString("0.0",
                System.Globalization.CultureInfo.InvariantCulture);

        private static readonly string[] AllowedPaperExtensions = { ".pdf", ".doc", ".docx" };

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IWebHostEnvironment _environment;
        private readonly IStringLocalizer _localizer;
        private readonly ILogger<ProfileModel> _logger;
        private readonly ApplicationDbContext _context;
        private readonly IUploadPaths _uploadPaths;
        private readonly ConferenceApp.Services.AuditService _audit;

        public ProfileModel(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IWebHostEnvironment environment,
            ILogger<ProfileModel> logger,
            IStringLocalizerFactory localizerFactory,
            ApplicationDbContext context,
            IUploadPaths uploadPaths,
            ConferenceApp.Services.AuditService audit)
        {
            _uploadPaths   = uploadPaths;
            _userManager   = userManager;
            _signInManager = signInManager;
            _environment   = environment;
            _logger        = logger;
            _localizer     = localizerFactory.Create("Pages.Profile", Assembly.GetExecutingAssembly().GetName().Name!);
            _context       = context;
            _audit         = audit;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string CurrentFileName { get; set; } = string.Empty;
        public string CurrentFilePath { get; set; } = string.Empty;

        // ── The status panel ─────────────────────────────────────────
        public string PaymentStatus      { get; set; } = "Pending";
        public string VerificationStatus { get; set; } = "None";
        public string ReferenceNumber    { get; set; } = string.Empty;
        public bool   IbanSubmitted      { get; set; } = false;
        public bool   HasVerifDocument   { get; set; } = false;

        // Filled in by an administrator when a verification is rejected; shown
        // to the participant so that they know what to send instead.
        public string? RejectionReason   { get; set; }

        // The /Payment/{slug} segment for this participant's own tier.
        public string PaymentSlug        { get; set; } = string.Empty;

        // The two groups: forms that pay, and forms that are verified.
        public bool IsPaymentGroup => Input.PartForm is "1" or "3";
        public bool IsVerifGroup   => Input.PartForm is "2" or "4";

        /// <summary>
        /// Whether the participation form may still be changed. It is locked
        /// once a verification has been submitted or approved, or once a payment
        /// has been confirmed — otherwise a paid participant could switch to a
        /// form that owes nothing, or a verified one to a form that pays.
        ///
        /// This property drives the form in the view; OnPostAsync repeats the
        /// same test on the server, because a disabled field is not a check.
        /// </summary>
        public bool IsPartFormLocked =>
            (IsVerifGroup && HasVerifDocument && VerificationStatus is "Pending" or "Approved") ||
            (IsPaymentGroup && PaymentStatus == "Confirmed");

        public class InputModel
        {
            // The messages are resx KEYS, translated by
            // ModelState.LocalizeErrors before the page is returned. They used
            // to be left untranslated, so the user saw a literal
            // "Error_NameLatinOnly" on screen.
            [Required(ErrorMessage = "Error_FirstNameRequired")]
            [StringLength(100, MinimumLength = 2, ErrorMessage = "Error_FirstNameLength")]
            [RegularExpression(@"^[A-Za-z\u00C0-\u024F\s\-']+$", ErrorMessage = "Error_FirstNameLatin")]
            public string FirstName { get; set; } = string.Empty;

            [Required(ErrorMessage = "Error_LastNameRequired")]
            [StringLength(100, MinimumLength = 2, ErrorMessage = "Error_LastNameLength")]
            [RegularExpression(@"^[A-Za-z\u00C0-\u024F\s\-']+$", ErrorMessage = "Error_LastNameLatin")]
            public string LastName { get; set; } = string.Empty;

            // [Required] on a non-nullable int does NOT work: an empty field
            // binds to 0, which counts as supplied and passes. [Range] is what
            // actually rejects 0 — and anything else outside 18..100.
            [Range(18, 100, ErrorMessage = "Error_AgeRange")]
            public int Age { get; set; }

            [Required(ErrorMessage = "Error_TitleRequired")]
            [StringLength(100, MinimumLength = 2, ErrorMessage = "Error_TitleLength")]
            public string AcademicTitle { get; set; } = string.Empty;

            [Required(ErrorMessage = "Error_PhoneRequired")]
            [RegularExpression(@"^\+?[\d\s\-()]{8,20}$", ErrorMessage = "Error_PhoneFormat")]
            public string Phone { get; set; } = string.Empty;

            [Required(ErrorMessage = "Error_WorkplaceRequired")]
            [StringLength(200, MinimumLength = 2, ErrorMessage = "Error_WorkplaceLength")]
            public string Workplace { get; set; } = string.Empty;

            [Required(ErrorMessage = "Error_PartFormRequired")]
            [RegularExpression(@"^[1-4]$", ErrorMessage = "Error_InvalidPartForm")]
            public string PartForm { get; set; } = string.Empty;

            public bool IsForeigner    { get; set; }
            public bool WantsMarketing { get; set; }
            public IFormFile? UploadedFile { get; set; }
        }

        // ════════════════════════════════════════════════════════════════════
        // GET
        // ════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetAsync()
        {
            // [T-11] LocalRedirect, not RedirectToPage: the panel is the /Index
            // page of the Admin area with the route "/Admin". There is no page
            // named "/Admin", so RedirectToPage threw InvalidOperationException
            // and an administrator got a 500 on their own profile. The other
            // three places (Login, Register, Verification) have always used
            // LocalRedirect.
            if (User.IsInRole("Admin")) return LocalRedirect("/Admin");

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                await _signInManager.SignOutAsync();
                HttpContext.Response.Cookies.Delete(".AspNetCore.Identity.Application");
                return RedirectToPage("/Login");
            }

            await LoadStatusPanelAsync(user);
            LoadForm(user);
            return Page();
        }

        // ════════════════════════════════════════════════════════════════════
        // GET Download
        // ════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetDownloadAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null || string.IsNullOrEmpty(user.PaperFilePath)) return NotFound();

            var fullPath = _uploadPaths.ToPhysical(user.PaperFilePath);
            if (fullPath == null || !System.IO.File.Exists(fullPath)) return NotFound();

            return PhysicalFile(fullPath, "application/octet-stream", Path.GetFileName(user.PaperFilePath));
        }

        // ════════════════════════════════════════════════════════════════════
        // POST
        // ════════════════════════════════════════════════════════════════════


        public async Task<IActionResult> OnPostAsync()
        {
            // [T-11] See OnGetAsync: the same route, the same reason.
            if (User.IsInRole("Admin")) return LocalRedirect("/Admin");

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                await _signInManager.SignOutAsync();
                HttpContext.Response.Cookies.Delete(".AspNetCore.Identity.Application");
                return RedirectToPage("/Login");
            }

            // ── The lock, enforced on the server ──────────────────────────────
            // IsPartFormLocked disables the field in the view; this repeats the
            // test against the stored values, because a form post can say
            // anything.
            bool serverLocked = 
                ((user.PartForm is "2" or "4") && !string.IsNullOrEmpty(user.VerificationDocumentPath) && (user.VerificationStatus is "Pending" or "Approved")) ||
                ((user.PartForm is "1" or "3") && user.PaymentStatus == "Confirmed");

            if (serverLocked && Input.PartForm != user.PartForm)
                Input.PartForm = user.PartForm; // silently keep the stored form

            ModelState.LocalizeErrors(_localizer, "Error_");

            if (!ModelState.IsValid)
            {
                await LoadStatusPanelAsync(user);
                if (!string.IsNullOrEmpty(user.PaperFilePath))
                {
                    CurrentFilePath = user.PaperFilePath;
                    CurrentFileName = Path.GetFileName(user.PaperFilePath);
                }
                return Page();
            }

            var changes = new List<string>();

            if (user.FirstName     != Input.FirstName)     changes.Add($"First Name: '{user.FirstName}' -> '{Input.FirstName}'");
            if (user.LastName      != Input.LastName)      changes.Add($"Last Name: '{user.LastName}' -> '{Input.LastName}'");
            if (user.Age           != Input.Age)           changes.Add($"Age: '{user.Age}' -> '{Input.Age}'");
            if (user.AcademicTitle != Input.AcademicTitle) changes.Add($"Academic Title: '{user.AcademicTitle}' -> '{Input.AcademicTitle}'");
            if (user.PhoneNumber   != Input.Phone)         changes.Add($"Phone: '{user.PhoneNumber}' -> '{Input.Phone}'");
            if (user.Workplace     != Input.Workplace)     changes.Add($"Organization: '{user.Workplace}' -> '{Input.Workplace}'");

            if (user.PartForm != Input.PartForm)
            {
                changes.Add($"Participation Form: '{GetPartFormName(user.PartForm)}' -> '{GetPartFormName(Input.PartForm)}'");

                // Moving to a form that pays instead of being verified: the
                // verification no longer applies to anything.
                if (Input.PartForm is "1" or "3")
                {
                    user.VerificationStatus = "None";

                    // [T-13] The scan goes with the status. Only the status used
                    // to be reset, leaving the photograph of a student or press
                    // card both on disk and in the row — personal data that no
                    // longer proves anything and that nobody sees, which means
                    // nobody will ever ask for it to be removed.
                    if (!string.IsNullOrEmpty(user.VerificationDocumentPath))
                    {
                        DeleteVerificationDocument(user.VerificationDocumentPath);
                        changes.Add($"Deleted Verification Document: {Path.GetFileName(user.VerificationDocumentPath)}");
                    }

                    user.VerificationDocumentPath    = null;
                    user.VerificationInstitution     = null;
                    user.VerificationSpecialty       = null;
                    user.VerificationYear            = null;
                    user.VerificationStudentId       = null;
                    user.VerificationSubmittedAt     = null;
                    user.VerificationRejectionReason = null;
                }
            }

            if (user.IsForeigner    != Input.IsForeigner)    changes.Add($"Foreigner: '{(user.IsForeigner ? "Yes" : "No")}' -> '{(Input.IsForeigner ? "Yes" : "No")}'");
            if (user.WantsMarketing != Input.WantsMarketing) changes.Add($"Marketing Consent: '{(user.WantsMarketing ? "Yes" : "No")}' -> '{(Input.WantsMarketing ? "Yes" : "No")}'");

            user.FirstName      = Input.FirstName;
            user.LastName       = Input.LastName;
            user.Age            = Input.Age;
            user.AcademicTitle  = Input.AcademicTitle;
            user.PhoneNumber    = Input.Phone;
            user.Workplace      = Input.Workplace;
            user.PartForm       = Input.PartForm;
            user.IsForeigner    = Input.IsForeigner;
            user.WantsMarketing = Input.WantsMarketing;

            if (Input.UploadedFile != null)
            {
                if (Input.UploadedFile.Length > MaxPaperBytes)
                {
                    // [T-15] The size is passed into the message: the {0}
                    // placeholder in the resx string had been waiting for it, but
                    // only validation.js filled it in.
                    ModelState.AddModelError("Input.UploadedFile",
                        _localizer["Error_FileTooLarge", MegabytesOf(Input.UploadedFile.Length)].Value);
                    await LoadStatusPanelAsync(user);
                    return Page();
                }

                // Only the size used to be checked here, not the type. At the
                // time the papers were written into wwwroot and served
                // statically, so an uploaded .aspx or .html file was reachable by
                // URL. They live outside wwwroot now ([F-02]) and are served only
                // through the handler above, but the allowlist stays: it is the
                // check that does not depend on where the file ends up.
                //
                // The extension is taken from the file name and matched against
                // an explicit allowlist rather than against Content-Type, which
                // the client sets to whatever it likes.
                var uploadExt = Path.GetExtension(Input.UploadedFile.FileName).ToLowerInvariant();
                if (!AllowedPaperExtensions.Contains(uploadExt))
                {
                    ModelState.AddModelError("Input.UploadedFile", _localizer["Error_InvalidFileType"].Value);
                    await LoadStatusPanelAsync(user);
                    return Page();
                }

                if (!string.IsNullOrEmpty(user.PaperFilePath))
                {
                    var oldPath = _uploadPaths.ToPhysical(user.PaperFilePath);
                    if (oldPath != null && System.IO.File.Exists(oldPath))
                    {
                        System.IO.File.Delete(oldPath);
                        changes.Add($"Deleted Old File: {Path.GetFileName(user.PaperFilePath)}");
                    }
                }

                try
                {
                    var uploadsFolder = _uploadPaths.EnsureDirectory("uploads", "papers26");

                    // uploadExt has already been checked against the allowlist
                    // above. The name keeps only letters and digits, so nothing
                    // from user input — a slash, a "..", a control character —
                    // can reach the path.
                    var safeFirst = new string((Input.FirstName ?? "").Where(char.IsLetterOrDigit).ToArray());
                    var safeLast  = new string((Input.LastName  ?? "").Where(char.IsLetterOrDigit).ToArray());
                    var fileName  = $"{safeFirst}{safeLast}_{Guid.NewGuid().ToString()[..8]}{uploadExt}";
                    var fullPath = Path.Combine(uploadsFolder, fileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                        await Input.UploadedFile.CopyToAsync(stream);

                    user.PaperFilePath = _uploadPaths.ToRelative("uploads", "papers26", fileName);
                    changes.Add($"Uploaded New File: {fileName}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error uploading file from profile.");
                    ModelState.AddModelError(string.Empty, _localizer["Error_FileUploadFailed"].Value);
                    await LoadStatusPanelAsync(user);
                    return Page();
                }
            }

            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                if (changes.Any())
                {
                    await _audit.LogAsync(user.Id, user.Email ?? string.Empty,
                        "Profile Update", string.Join(" | ", changes));
                }

                TempData["SuccessMessage"] = _localizer["Msg_UpdateSuccess"].Value;
                return RedirectToPage();
            }

            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            await LoadStatusPanelAsync(user);
            return Page();
        }

        // ── Helpers ───────────────────────────────────────────────────

        private async Task LoadStatusPanelAsync(ApplicationUser user)
        {
            PaymentStatus      = user.PaymentStatus      ?? "Pending";
            VerificationStatus = user.VerificationStatus ?? "None";
            ReferenceNumber    = user.ReferenceNumber;
            IbanSubmitted      = user.IbanTransferSubmittedAt.HasValue;
            HasVerifDocument   = !string.IsNullOrEmpty(user.VerificationDocumentPath);
            RejectionReason    = user.VerificationRejectionReason;

            // The tier this participant owes, by participation form rather than
            // by a hard-coded Id. The address uses the stable key ([D-02]). The
            // fallback to the regular tier is only so that the link points
            // somewhere for a form with no tier of its own.
            var tiers  = await _context.TicketTiers.ToListAsync();
            var ticket = ConferenceApp.Services.Payments.TicketPricing.ForUser(tiers, user)
                         ?? ConferenceApp.Services.Payments.TicketPricing.ByKey(
                                tiers, ConferenceApp.Services.Payments.TicketPricing.KeyEarlyBird);

            PaymentSlug = ticket != null
                ? ConferenceApp.Services.Payments.TicketPricing.SlugFor(ticket)
                : ConferenceApp.Services.Payments.TicketPricing.KeyEarlyBird;
        }

        private void LoadForm(ApplicationUser user)
        {
            Input.FirstName      = user.FirstName;
            Input.LastName       = user.LastName;
            Input.Age            = user.Age;
            Input.AcademicTitle  = user.AcademicTitle;
            Input.Phone          = user.PhoneNumber ?? string.Empty;
            Input.Workplace      = user.Workplace;
            Input.PartForm       = user.PartForm;
            Input.IsForeigner    = user.IsForeigner;
            Input.WantsMarketing = user.WantsMarketing;

            if (!string.IsNullOrEmpty(user.PaperFilePath))
            {
                CurrentFilePath = user.PaperFilePath;
                CurrentFileName = Path.GetFileName(user.PaperFilePath);
            }
        }

        /// <summary>
        /// Removes the verification document from disk. A failure is logged but
        /// does not stop the save: the row has to be cleared even if the file is
        /// already gone.
        /// </summary>
        private void DeleteVerificationDocument(string relativePath)
        {
            try
            {
                var fullPath = _uploadPaths.ToPhysical(relativePath);
                if (fullPath != null && System.IO.File.Exists(fullPath))
                    System.IO.File.Delete(fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not delete verification document {Path} after participation form change.",
                    relativePath);
            }
        }

        private string GetPartFormName(string id) => id switch
        {
            "1" => "Lector / Academic",
            "2" => "Student / PhD Candidate",
            "3" => "Online Participant",
            "4" => "Journalist / Media",
            _   => id
        };
    }
}