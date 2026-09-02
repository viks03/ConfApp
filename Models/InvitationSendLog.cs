using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    // Един ред на всеки опит за изпращане на покана (успешен или не).
    // BatchId групира всички получатели от едно и също изпращане, за да може
    // History таба да ги филтрира/показва като "последното изпращане" при нужда.
    public class InvitationSendLog
    {
        [Key]
        public int Id { get; set; }

        public Guid BatchId { get; set; }

        [Required]
        public string Email { get; set; } = string.Empty;

        public string? RecipientName { get; set; }

        [Required]
        public string Subject { get; set; } = string.Empty;

        public bool Success { get; set; }

        // "SMTP" / "Network" / "Validation" / "Configuration" / "Unknown" — null ако Success = true
        public string? ErrorCategory { get; set; }

        // Пълното детайлно съобщение, показвано на админа в History таба
        public string? ErrorMessage { get; set; }

        // Пълния рендиран HTML, който реално е бил изпратен на този получател
        // (плейсхолдърите вече заместени) — ВИНАГИ чистата версия, без
        // tracking pixel/rewritten линкове, за да е точно това, което се
        // сваля от History. Tracking-ът се инжектира само в отделно копие в
        // момента на самото изпращане (виж InjectTracking в .cshtml.cs) и
        // никога не се пази никъде.
        public string? SentBody { get; set; }

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        // Кой админ е изпратил — audit trail
        public string? SentByEmail { get; set; }

        // ── Open/Click tracking ─────────────────────────────────────────────
        // Уникален токен, вграден в pixel/click URL-ите за ТОЗИ конкретен ред.
        // Отделен от Id нарочно — за да не може някой просто да брои нагоре
        // (Id=1,2,3...) и да генерира фалшиви "отваряния" за чужди редове.
        public Guid TrackingToken { get; set; } = Guid.NewGuid();

        // Кога пикселът/линк е гръмнал за първи път (null = никога, доколкото
        // знаем — виж честната бележка в History таба за защо не е гаранция).
        public DateTime? OpenedAt { get; set; }
        public DateTime? LastOpenedAt { get; set; }
        public int OpenCount { get; set; }

        // Клик върху линк в писмото — по-силен сигнал от самия pixel.
        public DateTime? ClickedAt { get; set; }
        public int ClickCount { get; set; }

        // User-Agent на заявката, гръмнала пиксела за първи път — помага при
        // ръчна преценка дали изглежда като истински mail клиент или
        // автоматичен prefetch/сканер (не е сигурна детекция, само сигнал).
        public string? OpenedUserAgent { get; set; }
    }
}