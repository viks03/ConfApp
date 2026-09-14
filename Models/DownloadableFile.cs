// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    /// <summary>
    /// A downloadable file — the conference programme and the documents for
    /// authors.
    ///
    /// <para>
    /// <b>The keys are FIXED, not a free list.</b> Each of the five has its own
    /// place in the design of the page: an icon, a position, accompanying text.
    /// If an administrator could delete rows, removing "Декларация за авторство"
    /// would leave a hole between two documents, with no error anywhere.
    /// </para>
    ///
    /// <para>
    /// So: the panel fills in and replaces, but never creates and never deletes.
    /// A sixth document is a change to the page, not to the data.
    /// </para>
    /// </summary>
    public class DownloadableFile
    {
        public int Id { get; set; }

        /// <summary>One of <see cref="Keys"/>. Unique.</summary>
        [Required, MaxLength(48)]
        public string FileKey { get; set; } = string.Empty;

        /// <summary>
        /// The path to the uploaded file, relative to wwwroot
        /// (<c>/uploads/documents/…</c>). <c>null</c> means "not uploaded yet",
        /// and the page then omits the button entirely rather than offering a
        /// link that leads nowhere.
        /// </summary>
        [MaxLength(300)]
        public string? FilePath { get; set; }

        /// <summary>
        /// The name the file is downloaded under. Kept separately because the
        /// name on disk is a GUID — a visitor who downloads "a3f9….pdf" will
        /// have no idea what it is a week later.
        /// </summary>
        [MaxLength(200)]
        public string? DownloadName { get; set; }

        /// <summary>For the "PDF · 2.4 MB" label. Stored rather than measured
        /// on each request.</summary>
        public long SizeBytes { get; set; }

        public DateTime? UpdatedAt { get; set; }

        [MaxLength(256)]
        public string? UpdatedBy { get; set; }

        // ── The fixed keys ───────────────────────────────────────────────
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

        /// <summary>A human label, shown in the admin panel only — hence
        /// Bulgarian.</summary>
        public static string Describe(string key) => key switch
        {
            ProgrammeBg    => "Програма (BG)",
            ProgrammeEn    => "Програма (EN)",
            DocTemplate    => "Шаблон за доклад",
            DocDeclaration => "Декларация за авторство",
            DocCopyright   => "Съгласие CEEOL",
            _              => key
        };

        /// <summary>Which page the file appears on, for grouping in the
        /// panel.</summary>
        public static string PageOf(string key) =>
            key.StartsWith("programme", StringComparison.Ordinal) ? "Програма" : "Конференция";

        /// <summary>"2.4 MB" / "380 KB". Empty when there is no file.</summary>
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

        /// <summary>"PDF" / "DOCX", taken from the extension of the download
        /// name rather than from the GUID on disk.</summary>
        public string TypeLabel =>
            string.IsNullOrEmpty(DownloadName)
                ? string.Empty
                : Path.GetExtension(DownloadName).TrimStart('.').ToUpperInvariant();

        public bool IsUploaded => !string.IsNullOrWhiteSpace(FilePath);
    }
}
