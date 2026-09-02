using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;
using ConferenceApp.Data;
using ConferenceApp.Models;
using ConferenceApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Pages
{
    public class RegisterModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<RegisterModel> _logger;
        private readonly ConferenceApp.Services.Email.IMailComposer _mail;
        private readonly IConfiguration _config;
        private readonly IStringLocalizer _localizer;

        public RegisterModel(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            ILogger<RegisterModel> logger,
            ConferenceApp.Services.Email.IMailComposer mail,
            IConfiguration config,
            IStringLocalizerFactory localizerFactory)
        {
            _userManager = userManager;
            _context = context;
            _environment = environment;
            _logger = logger;
            _mail = mail;
            _config = config;

            _localizer = localizerFactory.Create("Pages.Register", Assembly.GetExecutingAssembly().GetName().Name!);
        }

        // ── НОВО: 3-фазов wizard (виж Register.cshtml.cs.snippet от дизайн
        // пакета). Реалната per-field валидация по фаза (имена, телефон,
        // формат на файла) умишлено НЕ е добавена тук — по изрична молба
        // остава за отделен bug-fix passthrough, след като интеграцията
        // мине. Засега фаза 1→2→3 напредва механично; финалната регистрация
        // на фаза 3 минава през СЪЩЕСТВУВАЩИТЕ проверки по-долу, непроменени.
        [BindProperty]
        public int Phase { get; set; } = 1;

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public class InputModel
        {
            // БЪГ ФИКС: досега този клас нямаше НИТО ЕДИН validation атрибут,
            // затова `ModelState.IsValid` винаги връщаше true и единствената
            // реална проверка беше validation.js в браузъра — тривиално
            // заобиколима (изключен JS, curl, Postman). Съобщенията са ключове
            // от Pages.Register.*.resx, преведени в OnPostAsync.
            [Required(ErrorMessage = "Error_Required")]
            [StringLength(100, MinimumLength = 2, ErrorMessage = "Error_NameLength")]
            [RegularExpression(@"^[A-Za-z\s\-']+$", ErrorMessage = "Error_NameLatinOnly")]
            public string FirstName { get; set; } = string.Empty;

            [Required(ErrorMessage = "Error_Required")]
            [StringLength(100, MinimumLength = 2, ErrorMessage = "Error_NameLength")]
            [RegularExpression(@"^[A-Za-z\s\-']+$", ErrorMessage = "Error_NameLatinOnly")]
            public string LastName { get; set; } = string.Empty;

            // Age е string в модела (идва от text input), затова диапазонът се
            // проверява с regex + допълнителна числова проверка в OnPostAsync.
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
            public bool ConsentToPublishPaper { get; set; } // НОВО ПОЛЕ
            public IFormFile? UploadedFile { get; set; }

            // БЪГ ФИКС: пътят на вече качения доклад, пренасян между фаза 2 и 3.
            // IFormFile физически не може да пътува в hidden input, затова
            // файлът се записва веднага при напускане на фаза 2, а тук пътува
            // само относителният път до него. Виж OnPostAsync.
            public string? SavedFilePath { get; set; }
        }

        // ── Preset тип участие от URL (?type=...) ─────────────
        // Премахнат Guest. Нови ID-та: online=3, journalist=4
        private static readonly Dictionary<string, string> PartFormTypeMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "lector",     "1" },
            { "student",    "2" },
            { "online",     "3" },
            { "journalist", "4" }
        };

        // ── Защита за логнати потребители ─────────────────────────────────────
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

        // ── НОВО: бутон "Назад" — само сваля фазата, без валидация. ───────────
        public IActionResult OnPostBack()
        {
            ModelState.Clear();
            // Същият clamp като в OnPostAsync — Phase идва от клиента.
            if (Phase < 1 || Phase > 3) Phase = 1;
            Phase = Math.Max(1, Phase - 1);
            return Page();
        }

        // ── Проверка за свободен email (извиква се live от JS) ────────────────
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

        // ── Кои полета принадлежат на коя фаза (за per-phase валидация) ───────
        private static readonly string[] Phase1Fields =
            { "Input.FirstName", "Input.LastName", "Input.Age", "Input.AcademicTitle", "Input.Email", "Input.Phone" };
        private static readonly string[] Phase2Fields =
            { "Input.Workplace", "Input.PartForm", "Input.UploadedFile" };
        private static readonly string[] Phase3Fields =
            { "Input.IsGDPR" };

        private static readonly string[] AllowedFileExtensions = { ".pdf", ".doc", ".docx" };

        /// <summary>
        /// Превежда ключовете от validation атрибутите (напр. "Error_NameLength")
        /// в реалния локализиран текст. Атрибутите не могат да ползват
        /// IStringLocalizer директно, затова превода става тук.
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

        /// <summary>
        /// Оставя в ModelState само грешките за полетата на текущата фаза —
        /// иначе на фаза 1 биха изскочили грешки за полета от фаза 2 и 3,
        /// които потребителят още дори не е видял.
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
        /// Проверки, които атрибутите не могат да изразят: числов диапазон на
        /// възрастта и разширение/размер на файла.
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

                if (Input.UploadedFile.Length > 25 * 1024 * 1024)
                    ModelState.AddModelError("Input.UploadedFile", _localizer["Error_FileTooLarge"].Value);
            }
        }

        /// <summary>
        /// Записва качения файл веднага (при напускане на фаза 2) и връща
        /// относителния път. IFormFile не може да пътува в hidden input между
        /// фазите, затова се материализира тук, а нататък пътува само пътят.
        /// </summary>
        private async Task<string?> PersistUploadedFileAsync()
        {
            if (Input.UploadedFile == null || Input.UploadedFile.Length == 0) return null;

            string uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "papers26");
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

            string safeFirst = new string((Input.FirstName ?? "").Where(char.IsLetterOrDigit).ToArray());
            string safeLast = new string((Input.LastName ?? "").Where(char.IsLetterOrDigit).ToArray());
            string ext = Path.GetExtension(Input.UploadedFile.FileName).ToLowerInvariant();
            string fileName = $"{safeFirst}{safeLast}_{Guid.NewGuid().ToString()[..8]}{ext}";
            string fullPath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(fullPath, FileMode.Create))
                await Input.UploadedFile.CopyToAsync(stream);

            return Path.Combine("uploads", "papers26", fileName);
        }

        /// <summary>
        /// Изтрива вече записан файл — ползва се, ако регистрацията се провали
        /// след като файлът е материализиран, за да не остават сираци на диска.
        /// </summary>
        private void DeleteSavedFileIfAny(string? relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return;
            try
            {
                var full = Path.Combine(_environment.WebRootPath, relativePath);
                if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete orphaned upload {Path}.", relativePath);
            }
        }

        // ── Основна регистрация ───────────────────────────────────────────────
        public async Task<IActionResult> OnPostAsync()
        {
            // БЪГ ФИКС: Phase идва от hidden input, тоест от клиента. Без този
            // clamp директен POST с Phase=99 (или Phase=3 с празни полета)
            // прескачаше целия wizard. Валидацията по-долу вече е реална, но
            // ограничаваме и самата стойност за всеки случай.
            if (Phase < 1 || Phase > 3) Phase = 1;

            LocalizeModelStateErrors();
            ValidateExtras(Phase);
            KeepOnlyPhaseErrors(Phase);

            if (!ModelState.IsValid) return Page();

            // ── Фаза 1 и 2: напред, след като текущата фаза е валидна ─────────
            if (Phase < 3)
            {
                // БЪГ ФИКС: каченият доклад се губеше безшумно между фаза 2 и 3
                // (IFormFile не оцелява в hidden input). Затова го записваме
                // веднага тук и нататък носим само пътя в Input.SavedFilePath.
                if (Phase == 2 && Input.UploadedFile != null && Input.UploadedFile.Length > 0)
                {
                    try
                    {
                        DeleteSavedFileIfAny(Input.SavedFilePath); // подмяна на предишен избор
                        Input.SavedFilePath = await PersistUploadedFileAsync();
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

            // ── Фаза 3: реалната регистрация ──────────────────────────────────
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            if (!Input.IsGDPR)
            {
                ModelState.AddModelError("Input.IsGDPR", _localizer["Error_GDPR"].Value);
                return Page();
            }

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

            // Файлът обикновено е записан още на фаза 2; ако потребителят го е
            // избрал чак сега (или е сменил избора си), записваме го тук.
            string? savedFilePath = Input.SavedFilePath;
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
                ConsentToPublishPaper = Input.ConsentToPublishPaper, // ЗАПИС НА НОВОТО ПОЛЕ
                PublishConsentDate = Input.ConsentToPublishPaper ? DateTime.UtcNow : null, // ДАТА НА СЪГЛАСИЕ
                CreatedAt = DateTime.UtcNow,
                EmailConfirmed = false,
                PaperFilePath = savedFilePath
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

                // ── Обновени Audit Log имена ──────────────────────────────────
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

                _context.Set<AuditLog>().Add(new AuditLog
                {
                    UserId = user.Id,
                    UserEmail = cleanedEmail,
                    Action = "User Registered",
                    IpAddress = clientIp,
                    Details = $"Ref: {user.ReferenceNumber} | Participation: {partFormDisplay} | File: {hasFile} | PublishConsent: {consentPublish} | GDPR: Yes | Marketing: {isMarketing}",
                    Timestamp = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                // Целият блок за сглобяване на имейла (четене на файл, седем
                // .Replace(), локализация, try/catch) живееше тук, в Login и
                // във Verification — три копия на един и същ код. Сега е едно
                // повикване; подробностите са в Services/Email/.
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

            // Регистрацията се провали — вече записаният доклад няма собственик,
            // затова го махаме, вместо да остава сирак на диска.
            DeleteSavedFileIfAny(savedFilePath);
            Input.SavedFilePath = null;

            return Page();
        }
    }
}