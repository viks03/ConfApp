using ConferenceApp.Data;
using ConferenceApp.Models;
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
    [Authorize]
    public class ProfileModel : PageModel
    {
        // 10 MB — същият лимит както в Register и в клиентската валидация.
        private const long MaxPaperBytes = 10 * 1024 * 1024;

        private static readonly string[] AllowedPaperExtensions = { ".pdf", ".doc", ".docx" };

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IWebHostEnvironment _environment;
        private readonly IStringLocalizer _localizer;
        private readonly ILogger<ProfileModel> _logger;
        private readonly ApplicationDbContext _context;

        public ProfileModel(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IWebHostEnvironment environment,
            ILogger<ProfileModel> logger,
            IStringLocalizerFactory localizerFactory,
            ApplicationDbContext context)
        {
            _userManager   = userManager;
            _signInManager = signInManager;
            _environment   = environment;
            _logger        = logger;
            _localizer     = localizerFactory.Create("Pages.Profile", Assembly.GetExecutingAssembly().GetName().Name!);
            _context       = context;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string CurrentFileName { get; set; } = string.Empty;
        public string CurrentFilePath { get; set; } = string.Empty;

        // ── Статус панел ─────────────────────────────────────────────
        public string PaymentStatus      { get; set; } = "Pending";
        public string VerificationStatus { get; set; } = "None";
        public string ReferenceNumber    { get; set; } = string.Empty;
        public bool   IbanSubmitted      { get; set; } = false;
        public bool   HasVerifDocument   { get; set; } = false;

        // Причина за отхвърляне — попълва се от администратора
        public string? RejectionReason   { get; set; }

        // Payment slug за динамичния линк към /Payment/slug...
        public string PaymentSlug        { get; set; } = string.Empty;

        // Групи по тип участие
        public bool IsPaymentGroup => Input.PartForm is "1" or "3";
        public bool IsVerifGroup   => Input.PartForm is "2" or "4";

        /// <summary>
        /// Дали типът участие е заключен за промяна.
        /// Заключен е:
        /// 1. При успешна верификация или такава в процес.
        /// 2. При успешно плащане за съответната група.
        /// </summary>
        public bool IsPartFormLocked =>
            (IsVerifGroup && HasVerifDocument && VerificationStatus is "Pending" or "Approved") ||
            (IsPaymentGroup && PaymentStatus == "Confirmed");

        public class InputModel
        {
            // Съобщенията са resx КЛЮЧОВЕ — превеждат се в
            // LocalizeModelStateErrors() преди страницата да се върне.
            // Преди тук стояха голи ключове без превод, така че потребителят
            // виждаше буквално "Error_NameLatinOnly" на екрана.
            [Required(ErrorMessage = "Error_FirstNameRequired")]
            [StringLength(100, MinimumLength = 2, ErrorMessage = "Error_FirstNameLength")]
            [RegularExpression(@"^[A-Za-z\u00C0-\u024F\s\-']+$", ErrorMessage = "Error_FirstNameLatin")]
            public string FirstName { get; set; } = string.Empty;

            [Required(ErrorMessage = "Error_LastNameRequired")]
            [StringLength(100, MinimumLength = 2, ErrorMessage = "Error_LastNameLength")]
            [RegularExpression(@"^[A-Za-z\u00C0-\u024F\s\-']+$", ErrorMessage = "Error_LastNameLatin")]
            public string LastName { get; set; } = string.Empty;

            // БЪГ ФИКС: [Required] върху non-nullable int НЕ работи — при празно
            // поле стойността е 0, което се брои за "попълнено" и проверката
            // минава. Затова тук е [Range], който реално отсича 0 и глупостите.
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
            if (User.IsInRole("Admin")) return RedirectToPage("/Admin");

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

            var fullPath = Path.Combine(_environment.WebRootPath, user.PaperFilePath);
            if (!System.IO.File.Exists(fullPath)) return NotFound();

            return PhysicalFile(fullPath, "application/octet-stream", Path.GetFileName(user.PaperFilePath));
        }

        // ════════════════════════════════════════════════════════════════════
        // POST
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Превежда resx КЛЮЧОВЕТЕ от validation атрибутите в реален текст.
        /// Атрибутите не могат да ползват IStringLocalizer директно, затова
        /// носят ключ, а преводът става тук. Без това потребителят виждаше
        /// буквално "Error_NameLatinOnly" на екрана.
        /// </summary>
        private void LocalizeModelStateErrors()
        {
            foreach (var entry in ModelState)
            {
                foreach (var error in entry.Value.Errors.ToList())
                {
                    if (error.ErrorMessage.StartsWith("Error_"))
                    {
                        entry.Value.Errors.Remove(error);
                        entry.Value.Errors.Add(_localizer[error.ErrorMessage].Value);
                    }
                }
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (User.IsInRole("Admin")) return RedirectToPage("/Admin");

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                await _signInManager.SignOutAsync();
                HttpContext.Response.Cookies.Delete(".AspNetCore.Identity.Application");
                return RedirectToPage("/Login");
            }

            // ── Сървърна защита срещу промяна на заключен тип (Включва и плащанията) ──
            bool serverLocked = 
                ((user.PartForm is "2" or "4") && !string.IsNullOrEmpty(user.VerificationDocumentPath) && (user.VerificationStatus is "Pending" or "Approved")) ||
                ((user.PartForm is "1" or "3") && user.PaymentStatus == "Confirmed");

            if (serverLocked && Input.PartForm != user.PartForm)
                Input.PartForm = user.PartForm; // Принудително връщаме стария тип

            LocalizeModelStateErrors();

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

                // Ако преминават към тип без верификация (1 или 3), нулираме верификационния статус
                if (Input.PartForm is "1" or "3")
                    user.VerificationStatus = "None";
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
                    ModelState.AddModelError("Input.UploadedFile", _localizer["Error_FileTooLarge"].Value);
                    await LoadStatusPanelAsync(user);
                    return Page();
                }

                // SECURITY ФИКС: тук се проверяваше САМО размерът, не и типът.
                // Файловете се записват в wwwroot/uploads/papers26/, която се
                // раздава статично — тоест качването на .aspx/.html/.js файл
                // беше напълно възможно и той щеше да е достъпен по URL.
                // Разширението се взима от името, затова се проверява срещу
                // явен allowlist, не срещу Content-Type (който клиентът задава).
                var uploadExt = Path.GetExtension(Input.UploadedFile.FileName).ToLowerInvariant();
                if (!AllowedPaperExtensions.Contains(uploadExt))
                {
                    ModelState.AddModelError("Input.UploadedFile", _localizer["Error_InvalidFileType"].Value);
                    await LoadStatusPanelAsync(user);
                    return Page();
                }

                if (!string.IsNullOrEmpty(user.PaperFilePath))
                {
                    var oldPath = Path.Combine(_environment.WebRootPath, user.PaperFilePath);
                    if (System.IO.File.Exists(oldPath))
                    {
                        System.IO.File.Delete(oldPath);
                        changes.Add($"Deleted Old File: {Path.GetFileName(user.PaperFilePath)}");
                    }
                }

                try
                {
                    var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "papers26");
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                    // uploadExt е вече проверен срещу allowlist-а по-горе; името се
                    // чисти от всичко освен букви и цифри, за да не влезе нищо
                    // от потребителския вход в пътя.
                    var safeFirst = new string((Input.FirstName ?? "").Where(char.IsLetterOrDigit).ToArray());
                    var safeLast  = new string((Input.LastName  ?? "").Where(char.IsLetterOrDigit).ToArray());
                    var fileName  = $"{safeFirst}{safeLast}_{Guid.NewGuid().ToString()[..8]}{uploadExt}";
                    var fullPath = Path.Combine(uploadsFolder, fileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                        await Input.UploadedFile.CopyToAsync(stream);

                    user.PaperFilePath = Path.Combine("uploads", "papers26", fileName);
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
                    _context.Set<AuditLog>().Add(new AuditLog
                    {
                        UserId    = user.Id,
                        UserEmail = user.Email ?? string.Empty,
                        Action    = "Profile Update",
                        IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
                        Details   = string.Join(" | ", changes),
                        Timestamp = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();
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

            // Взимаме билета с ID = 2 от базата данни, точно както в Attend
            var ticket = await _context.TicketTiers.FindAsync(2);
            string ticketName = ticket?.NameEn ?? "Early Bird Ticket";

            var slug = ticketName.ToLower();
            slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
            slug = Regex.Replace(slug, @"\s+", " ").Trim();
            slug = slug.Replace(" ", "-");

            PaymentSlug = slug;
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