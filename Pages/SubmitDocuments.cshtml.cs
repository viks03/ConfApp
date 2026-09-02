using System.Linq;
using ConferenceApp.Data;
using ConferenceApp.Models;
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

        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png" };
        private static readonly string[] AllowedMimeTypes  = { "image/jpeg", "image/jpg", "image/png" };
        private const long MaxFileSizeBytes = 3 * 1024 * 1024;

        public SubmitDocumentsModel(
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment environment,
            ILogger<SubmitDocumentsModel> logger,
            IStringLocalizerFactory localizerFactory,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _environment = environment;
            _logger      = logger;
            _localizer   = localizerFactory.Create("Pages.SubmitDocuments", Assembly.GetExecutingAssembly().GetName().Name!);
            _context     = context;
        }

        public string PartForm { get; set; } = string.Empty;
        public bool HasExistingSubmission { get; set; } = false;
        public string VerificationStatus  { get; set; } = "None";
        public string? RejectionReason { get; set; } 
        public string? CurrentDocumentPath { get; set; } // <--- ТУК ЛИПСВАШЕ ТАЗИ ПРОМЕНЛИВА

        [BindProperty(Name = "StudentInput")]
        public StudentInputModel StudentInput { get; set; } = new();

        [BindProperty(Name = "JournalistInput")]
        public JournalistInputModel JournalistInput { get; set; } = new();

        public class StudentInputModel
        {
            public IFormFile? StudentCard { get; set; }

            // Всяко поле има СОБСТВЕНО съобщение — преди всички делеха общото
            // "SD_Err_Required", при това непреведено (виждаше се буквално
            // ключът). Преводът става в LocalizeModelStateErrors().
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

            // Незадължително поле.
            // БЪГ ФИКС: тук имаше [RegularExpression], който изискваше адресът да
            // започва с http(s)://. Това чупеше подаването за журналисти в два
            // случая: (а) потребителят пише "media.bg" както е свикнал, и (б) в
            // профила вече има запазена стойност без схема — тогава формата
            // отказваше да се подаде заради поле, което дори не е задължително.
            // Вместо да отхвърляме, нормализираме стойността в OnPostAsync.
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

            // Щом документите са подадени и чакат проверка (или са одобрени),
            // страницата няма какво да предложи — статусът се следи от профила.
            // Преди тук се рендираше форма със заключени полета и без бутон,
            // което изглеждаше като счупена страница.
            // "Rejected" НЕ се блокира: тогава потребителят трябва да качи нов файл.
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
        // POST
        // ════════════════════════════════════════════════════════════════════
        /// <summary>
        /// Превежда resx ключовете от validation атрибутите. Без това
        /// потребителят виждаше буквално "SD_Err_Required" на екрана.
        /// </summary>
        private void LocalizeModelStateErrors()
        {
            foreach (var entry in ModelState)
            {
                foreach (var error in entry.Value.Errors.ToList())
                {
                    if (error.ErrorMessage.StartsWith("SD_Err_"))
                    {
                        entry.Value.Errors.Remove(error);
                        entry.Value.Errors.Add(_localizer[error.ErrorMessage].Value);
                    }
                }
            }
        }

        /// <summary>
        /// Прибавя https:// когато потребителят е написал само домейна.
        /// По-дружелюбно от това да откажем цялата форма заради незадължително поле.
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

            // Огледало на защитата в OnGetAsync — иначе директен POST би
            // презаписал документи, които вече се проверяват.
            if (VerificationStatus is "Pending" or "Approved")
                return RedirectToPage("/Profile");

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

            if ((doc1 == null || doc1.Length == 0) && !HasExistingSubmission)
            {
                ModelState.AddModelError(fileFieldName,
                    PartForm == "2"
                        ? _localizer["SD_Err_Student_Doc1Required"].Value
                        : _localizer["SD_Err_Journalist_Doc1Required"].Value);
            }

            LocalizeModelStateErrors();

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
                    var uploadDir = Path.Combine(_environment.WebRootPath, "uploads", "submitted-documents", subfolder);
                    Directory.CreateDirectory(uploadDir);

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
                if (newPath != null) DeleteOldFile(newPath);

                _logger.LogWarning("UpdateAsync failed for user {UserId}: {Errors}",
                    user.Id, string.Join(", ", result.Errors.Select(e => e.Description)));
                ModelState.AddModelError(string.Empty, _localizer["SD_Err_SaveFailed"].Value);
                return Page();
            }

            _context.Set<AuditLog>().Add(new AuditLog
            {
                UserId    = user.Id,
                UserEmail = user.Email ?? string.Empty,
                Action    = "Verification Documents Submitted",
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                Details   = $"Type={GetTypeName(PartForm)} Institution={user.VerificationInstitution}",
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            // БЪГ ФИКС: беше TempData["SuccessMessage"] — СЪЩИЯТ ключ, който Profile
            // показва в главното си боди. Ако потребителят отидеше на профила преди
            // съобщението да е прочетено тук, то изскачаше там и дублираше статус
            // панела вдясно, който вече казва същото. Собствен ключ = няма изтичане.
            TempData["SdSubmitMessage"] = _localizer["SD_SubmitSuccess"].Value;
            
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

        private async Task<string> SaveFileAsync(IFormFile file, string dir, string subfolder)
        {
            var ext      = Path.GetExtension(file.FileName).ToLowerInvariant();
            var fileName = Guid.NewGuid().ToString() + ext;
            var fullPath = Path.Combine(dir, fileName);

            await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await file.CopyToAsync(stream);

            return Path.Combine("uploads", "submitted-documents", subfolder, fileName)
                       .Replace("\\", "/");
        }

        private void DeleteOldFile(string? relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return;

            relativePath = relativePath.TrimStart('/', '\\');
            var fullPath = Path.Combine(_environment.WebRootPath, relativePath);

            if (!System.IO.File.Exists(fullPath)) return;

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