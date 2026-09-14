// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Linq;
using ConferenceApp.Data;
using ConferenceApp.Helpers;
using ConferenceApp.Models;
using ConferenceApp.Services.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace ConferenceApp.Pages
{
    [Authorize]
    public class SubmitDocumentsModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _environment;
        private readonly IStringLocalizer _localizer;
        private readonly ILogger<SubmitDocumentsModel> _logger;
        private readonly ApplicationDbContext _context;
        private readonly IUploadPaths _uploadPaths;
        private readonly ConferenceApp.Services.AuditService _audit;

        // Images only: this is a photograph of a card, and accepting documents
        // would mean accepting formats that can carry a payload. The extension
        // and the MIME type are both checked — the second is set by the client,
        // so it narrows the first rather than replacing it.
        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png" };
        private static readonly string[] AllowedMimeTypes  = { "image/jpeg", "image/jpg", "image/png" };
        private const long MaxFileSizeBytes = 3 * 1024 * 1024;

        public SubmitDocumentsModel(
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment environment,
            ILogger<SubmitDocumentsModel> logger,
            IStringLocalizerFactory localizerFactory,
            ApplicationDbContext context,
            IUploadPaths uploadPaths,
            ConferenceApp.Services.AuditService audit)
        {
            _uploadPaths = uploadPaths;
            _userManager = userManager;
            _environment = environment;
            _logger      = logger;
            _localizer   = localizerFactory.Create("Pages.SubmitDocuments", Assembly.GetExecutingAssembly().GetName().Name!);
            _context     = context;
            _audit       = audit;
        }

        public string PartForm { get; set; } = string.Empty;
        public bool HasExistingSubmission { get; set; } = false;
        public string VerificationStatus  { get; set; } = "None";
        public string? RejectionReason { get; set; } 
        public string? CurrentDocumentPath { get; set; }

        [BindProperty(Name = "StudentInput")]
        public StudentInputModel StudentInput { get; set; } = new();

        [BindProperty(Name = "JournalistInput")]
        public JournalistInputModel JournalistInput { get; set; } = new();

        public class StudentInputModel
        {
            public IFormFile? StudentCard { get; set; }

            // Every field has a message of its OWN: they used to share a single
            // "SD_Err_Required", and untranslated at that — the key itself
            // appeared on screen. ModelState.LocalizeErrors does the
            // translation.
            [Required(ErrorMessage = "SD_Err_UniversityRequired")]
            [StringLength(150, MinimumLength = 2, ErrorMessage = "SD_Err_UniversityLength")]
            public string University { get; set; } = string.Empty;

            [Required(ErrorMessage = "SD_Err_SpecialtyRequired")]
            [StringLength(150, MinimumLength = 2, ErrorMessage = "SD_Err_SpecialtyLength")]
            public string Specialty { get; set; } = string.Empty;

            [Required(ErrorMessage = "SD_Err_YearRequired")]
            [RegularExpression(@"^(PhD/Masters Degree|[1-6])$", ErrorMessage = "SD_Err_YearInvalid")]
            public string StudyYear { get; set; } = string.Empty;

            [Required(ErrorMessage = "SD_Err_StudentIdRequired")]
            [StringLength(30, MinimumLength = 2, ErrorMessage = "SD_Err_StudentIdLength")]
            public string StudentId { get; set; } = string.Empty;
        }

        public class JournalistInputModel
        {
            public IFormFile? PressCard { get; set; }

            [Required(ErrorMessage = "SD_Err_MediaRequired")]
            [StringLength(150, MinimumLength = 2, ErrorMessage = "SD_Err_MediaLength")]
            public string MediaOutlet { get; set; } = string.Empty;

            [Required(ErrorMessage = "SD_Err_PositionRequired")]
            [StringLength(100, MinimumLength = 2, ErrorMessage = "SD_Err_PositionLength")]
            public string Position { get; set; } = string.Empty;

            // Optional. There used to be a [RegularExpression] here demanding
            // that the address start with http(s)://, which broke the form for
            // journalists in two ways: somebody typing "media.bg" as they always
            // do, and a value already stored without a scheme. The whole form was
            // then refused over a field that is not even required. The value is
            // normalised in NormalizeWebsite instead of being rejected.
            [StringLength(200, ErrorMessage = "SD_Err_WebsiteLength")]
            public string? MediaWebsite { get; set; }
        }

        // ════════════════════════════════════════════════════════════════════
        // GET
        // ════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

            PartForm            = user.PartForm;
            VerificationStatus  = user.VerificationStatus ?? "None";
            RejectionReason     = user.VerificationRejectionReason;
            CurrentDocumentPath = user.VerificationDocumentPath; // <--- ЗАДАВАМЕ СТОЙНОСТТА ТУК

            if (PartForm is not ("2" or "4"))
                return Page();

            // Once the documents are submitted and waiting — or approved — this
            // page has nothing to offer and the profile is where the status is
            // followed. It used to render a form with disabled fields and no
            // button, which reads as a broken page.
            // "Rejected" is deliberately NOT blocked: that is exactly when a new
            // file has to be uploaded.
            if (VerificationStatus is "Pending" or "Approved")
                return RedirectToPage("/Profile");

            HasExistingSubmission = !string.IsNullOrEmpty(user.VerificationDocumentPath);
            if (HasExistingSubmission)
            {
                bool dataMatchesType = (PartForm == "2" && user.VerificationStudentId != null) ||
                                       (PartForm == "4" && user.VerificationStudentId == null);

                if (dataMatchesType)
                {
                    if (PartForm == "2")
                    {
                        StudentInput.University = user.VerificationInstitution ?? string.Empty;
                        StudentInput.Specialty  = user.VerificationSpecialty   ?? string.Empty;
                        StudentInput.StudyYear  = user.VerificationYear        ?? string.Empty;
                        StudentInput.StudentId  = user.VerificationStudentId   ?? string.Empty;
                    }
                    else
                    {
                        JournalistInput.MediaOutlet  = user.VerificationInstitution ?? string.Empty;
                        JournalistInput.Position     = user.VerificationSpecialty   ?? string.Empty;
                        JournalistInput.MediaWebsite = user.VerificationYear;
                    }
                }
            }

            return Page();
        }

        // ════════════════════════════════════════════════════════════════════
        // GET ViewDocument
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Shows the signed-in person their OWN document.
        /// <para>
        /// [T-14] The "View" link in the view used to point at
        /// <c>/Admin/Index?handler=DownloadVerifDoc&amp;userId=…</c> — an address
        /// that does not exist (the panel is <c>@page "/Admin"</c>), and at the
        /// correct address that handler is for the Admin role only. So a rejected
        /// student had no way of seeing which photograph they had uploaded.
        /// </para>
        /// <para>
        /// There is deliberately no user parameter: the file is taken from the
        /// signed-in account, exactly as the paper is in
        /// <c>/Profile?handler=Download</c>. A parameter would have to be
        /// authorised, and there is nothing here it could usefully say.
        /// </para>
        /// </summary>
        public async Task<IActionResult> OnGetViewDocumentAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null || string.IsNullOrEmpty(user.VerificationDocumentPath))
                return NotFound();

            var physical = _uploadPaths.ToPhysical(user.VerificationDocumentPath);
            if (physical == null || !System.IO.File.Exists(physical)) return NotFound();

            return PhysicalFile(physical, "application/octet-stream",
                Path.GetFileName(user.VerificationDocumentPath));
        }

        // ════════════════════════════════════════════════════════════════════
        // POST
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Adds https:// when only the domain was typed. Friendlier than
        /// refusing the whole form over an optional field.
        /// </summary>
        private static string? NormalizeWebsite(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            url = url.Trim();
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }
            return url;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return RedirectToPage("/Login");

            PartForm              = user.PartForm;
            VerificationStatus    = user.VerificationStatus ?? "None";
            RejectionReason       = user.VerificationRejectionReason;
            CurrentDocumentPath   = user.VerificationDocumentPath; // <--- ЗАДАВАМЕ СТОЙНОСТТА ТУК
            HasExistingSubmission = !string.IsNullOrEmpty(user.VerificationDocumentPath);

            if (PartForm is not ("2" or "4"))
                return RedirectToPage();

            // Mirrors the guard in OnGetAsync: without it a direct POST would
            // overwrite documents that are already being reviewed.
            if (VerificationStatus is "Pending" or "Approved")
                return RedirectToPage("/Profile");

            // The page carries both forms and posts both; only the one matching
            // the participation form is validated, or a student would be asked
            // for a media outlet.
            if (PartForm == "2")
            {
                foreach (var key in ModelState.Keys.Where(k => k.StartsWith("JournalistInput")).ToList())
                    ModelState.Remove(key);
            }
            else
            {
                foreach (var key in ModelState.Keys.Where(k => k.StartsWith("StudentInput")).ToList())
                    ModelState.Remove(key);
            }

            IFormFile? doc1 = PartForm == "2" ? StudentInput.StudentCard : JournalistInput.PressCard;
            var fileFieldName = PartForm == "2" ? "StudentInput.StudentCard" : "JournalistInput.PressCard";

            // A file is required the first time only: a rejected submission is
            // often corrected in the text fields alone, and the document already
            // on file still applies.
            if ((doc1 == null || doc1.Length == 0) && !HasExistingSubmission)
            {
                ModelState.AddModelError(fileFieldName,
                    PartForm == "2"
                        ? _localizer["SD_Err_Student_Doc1Required"].Value
                        : _localizer["SD_Err_Journalist_Doc1Required"].Value);
            }

            ModelState.LocalizeErrors(_localizer, "SD_Err_");

            if (!ModelState.IsValid)
            {
                return Page();
            }

            string? newPath = null;

            if (doc1 != null && doc1.Length > 0)
            {
                var fileError = ValidateFile(doc1);
                if (fileError != null)
                {
                    ModelState.AddModelError(fileFieldName, fileError);
                    return Page();
                }

                try
                {
                    var subfolder = PartForm == "2" ? "students" : "journalists";
                    var uploadDir = _uploadPaths.EnsureDirectory("uploads", "submitted-documents", subfolder);

                    newPath = await SaveFileAsync(doc1, uploadDir, subfolder);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error uploading verification documents for user {UserId}", user.Id);
                    ModelState.AddModelError(string.Empty, _localizer["SD_Err_UploadFailed"].Value);
                    return Page();
                }
            }

            if (PartForm == "2")
            {
                user.VerificationInstitution = StudentInput.University;
                user.VerificationSpecialty   = StudentInput.Specialty;
                user.VerificationYear        = StudentInput.StudyYear;
                user.VerificationStudentId   = StudentInput.StudentId;
            }
            else
            {
                user.VerificationInstitution = JournalistInput.MediaOutlet;
                user.VerificationSpecialty   = JournalistInput.Position;
                user.VerificationYear        = NormalizeWebsite(JournalistInput.MediaWebsite);
                user.VerificationStudentId   = null;
            }

            // The old file is deleted only once the new one is safely written,
            // so a failed upload cannot leave the person with no document at all.
            if (newPath != null)
            {
                DeleteOldFile(user.VerificationDocumentPath);
                user.VerificationDocumentPath = newPath;
            }

            user.VerificationStatus = "Pending";
            user.VerificationRejectionReason = null;
            user.VerificationSubmittedAt = DateTime.UtcNow;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                // Nothing was stored, so the file just written has no row
                // pointing at it: it goes rather than staying as an orphan.
                if (newPath != null) DeleteOldFile(newPath);

                _logger.LogWarning("UpdateAsync failed for user {UserId}: {Errors}",
                    user.Id, string.Join(", ", result.Errors.Select(e => e.Description)));
                ModelState.AddModelError(string.Empty, _localizer["SD_Err_SaveFailed"].Value);
                return Page();
            }

            await _audit.LogAsync(user.Id, user.Email ?? string.Empty,
                "Verification Documents Submitted",
                $"Type={GetTypeName(PartForm)} Institution={user.VerificationInstitution}");

            // [T-12] A TempData["SdSubmitMessage"] used to be set here that
            // nobody could ever see: this very save raises the status to
            // "Pending", and in that state OnGetAsync redirects to /Profile
            // before the view is rendered. The message, its markup and both resx
            // keys were removed — the status panel in the profile says the same
            // thing and is actually visible.
            return RedirectToPage();
        }

        // ════════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════════

        private string? ValidateFile(IFormFile file)
        {
            if (file.Length > MaxFileSizeBytes)
                return _localizer["SD_Err_FileTooLarge"].Value;

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
                return _localizer["SD_Err_FileType"].Value;

            var mime = file.ContentType.ToLowerInvariant();
            if (!AllowedMimeTypes.Contains(mime))
                return _localizer["SD_Err_FileType"].Value;

            return null;
        }

        // The name is a bare GUID, with no part of the person's name in it:
        // these files sit outside wwwroot and are served only to their owner,
        // and a name that describes them would leak who they belong to wherever
        // the path appears.
        private async Task<string> SaveFileAsync(IFormFile file, string dir, string subfolder)
        {
            var ext      = Path.GetExtension(file.FileName).ToLowerInvariant();
            var fileName = Guid.NewGuid().ToString() + ext;
            var fullPath = Path.Combine(dir, fileName);

            await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await file.CopyToAsync(stream);

            return _uploadPaths.ToRelative("uploads", "submitted-documents", subfolder, fileName);
        }

        private void DeleteOldFile(string? relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return;

            var fullPath = _uploadPaths.ToPhysical(relativePath);

            if (fullPath == null || !System.IO.File.Exists(fullPath)) return;

            try
            {
                System.IO.File.Delete(fullPath);
                _logger.LogInformation("Deleted old verification document: {Path}", fullPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete old document at {Path}", fullPath);
            }
        }

        private static string GetTypeName(string partForm) => partForm switch
        {
            "2" => "Student/PhD",
            "4" => "Journalist",
            _   => partForm
        };
    }
}