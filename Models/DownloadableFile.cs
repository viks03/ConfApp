using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    /// <summary>
    /// Файл за изтегляне — програмата на конференцията и документите за автори.
    ///
    /// <para>
    /// <b>Ключовете са ФИКСИРАНИ, не свободен списък.</b> Всеки от петте има
    /// свое място в дизайна на страницата: иконка, подредба, придружаващ текст.
    /// Ако администраторът можеше да трие редове, изтриването на „Декларация за
    /// авторство“ би оставило дупка между два документа — без грешка никъде.
    /// </para>
    ///
    /// <para>
    /// Затова: панелът пълни и подменя, но не създава и не трие. Шести документ
    /// е промяна на страницата, не на данните.
    /// </para>
    /// </summary>
    public class DownloadableFile
    {
        public int Id { get; set; }

        /// <summary>Един от <see cref="Keys"/>. Уникален.</summary>
        [Required, MaxLength(48)]
        public string FileKey { get; set; } = string.Empty;

        /// <summary>
        /// Пътят до качения файл, относителен спрямо wwwroot
        /// (<c>/uploads/documents/…</c>). <c>null</c> значи „още не е качен“ —
        /// страницата тогава не показва бутона изобщо, вместо да води наникъде.
        /// </summary>
        [MaxLength(300)]
        public string? FilePath { get; set; }

        /// <summary>
        /// Името, с което файлът се сваля. Пазим го отделно, защото на диска
        /// името е GUID — посетител, който свали „a3f9…pdf“, няма да разбере
        /// какво е това след седмица.
        /// </summary>
        [MaxLength(200)]
        public string? DownloadName { get; set; }

        /// <summary>За надписа „PDF · 2.4 MB“.</summary>
        public long SizeBytes { get; set; }

        public DateTime? UpdatedAt { get; set; }

        [MaxLength(256)]
        public string? UpdatedBy { get; set; }

        // ── Фиксираните ключове ──────────────────────────────────────────
        public const string ProgrammeBg = "programme.bg";
        public const string ProgrammeEn = "programme.en";
        public const string DocTemplate = "doc.template";
        public const string DocDeclaration = "doc.declaration";
        public const string DocCopyright = "doc.copyright";

        public static readonly string[] Keys =
        {
            ProgrammeBg, ProgrammeEn,
            DocTemplate, DocDeclaration, DocCopyright
        };

        /// <summary>Човешко описание — само за админ панела.</summary>
        public static string Describe(string key) => key switch
        {
            ProgrammeBg    => "Програма (BG)",
            ProgrammeEn    => "Програма (EN)",
            DocTemplate    => "Шаблон за доклад",
            DocDeclaration => "Декларация за авторство",
            DocCopyright   => "Съгласие CEEOL",
            _              => key
        };

        /// <summary>На коя страница се показва — за групиране в панела.</summary>
        public static string PageOf(string key) =>
            key.StartsWith("programme", StringComparison.Ordinal) ? "Програма" : "Конференция";

        /// <summary>„2.4 MB“ / „380 KB“. Празно при липсващ файл.</summary>
        public string SizeLabel
        {
            get
            {
                if (SizeBytes <= 0) return string.Empty;
                if (SizeBytes >= 1024 * 1024)
                    return $"{SizeBytes / 1024d / 1024d:0.#} MB";
                return $"{SizeBytes / 1024d:0} KB";
            }
        }

        /// <summary>„PDF“ / „DOCX“ — от разширението на изтегляното име.</summary>
        public string TypeLabel =>
            string.IsNullOrEmpty(DownloadName)
                ? string.Empty
                : Path.GetExtension(DownloadName).TrimStart('.').ToUpperInvariant();

        public bool IsUploaded => !string.IsNullOrWhiteSpace(FilePath);
    }
}
