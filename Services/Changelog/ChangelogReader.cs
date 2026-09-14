// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text;
using System.Text.RegularExpressions;

namespace ConferenceApp.Services.Changelog
{
    public sealed class ChangelogEntry
    {
        public string Version { get; set; } = "";
        public string? Date { get; set; }

        /// <summary>Rendered HTML for the "for the administrator" part.</summary>
        public string AdminHtml { get; set; } = "";

        /// <summary>Rendered HTML for the technical part.</summary>
        public string TechHtml { get; set; } = "";

        public bool HasAdmin => AdminHtml.Length > 0;
        public bool HasTech => TechHtml.Length > 0;

        /// <summary>"Unreleased" is not a release — the panel shows it
        /// differently and LatestVersion skips it.</summary>
        public bool IsUnreleased =>
            Version.Contains("unreleased", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads <c>CHANGELOG.md</c> from the project root and turns it into HTML.
    ///
    /// <para>
    /// <b>Why a parser of our own rather than a library:</b> the file is ours
    /// and uses exactly three constructs — a version heading, a section heading
    /// and a list. A full Markdown parser would be a dependency, its updates and
    /// its vulnerabilities, for something that fits in eighty lines.
    /// </para>
    ///
    /// <para>
    /// <b>Why there is no sanitizer:</b> the file is written by the developer
    /// and arrives through git, not through a form. Everything is escaped on the
    /// way out, so even if it did contain HTML it would come out as text. If it
    /// ever becomes editable from the panel, this has to be revisited.
    /// </para>
    /// </summary>
    public sealed class ChangelogReader
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<ChangelogReader> _logger;

        // The file is read rarely and does not change between restarts, so it
        // is kept in memory.
        private List<ChangelogEntry>? _cache;
        private DateTime _cachedAt;

        public ChangelogReader(IWebHostEnvironment env, ILogger<ChangelogReader> logger)
        {
            _env = env;
            _logger = logger;
        }

        public IReadOnlyList<ChangelogEntry> Read()
        {
            // Five minutes is long enough that opening the tab does not read the
            // file every time, and short enough to see an edit without a
            // restart.
            if (_cache is not null && (DateTime.UtcNow - _cachedAt).TotalMinutes < 5)
                return _cache;

            try
            {
                // CHANGELOG.md lives in the project ROOT, not in wwwroot.
                //
                // The reason is not tidiness: wwwroot is the public folder, so
                // anyone could open /changelog.md and read the technical notes —
                // which fields were added, where a bug was. There is no reason
                // for that to be public.
                //
                // ContentRootPath points at the root both in development and
                // after publish, as long as the file is copied, which is set up
                // in ConferenceApp.csproj.
                var path = Path.Combine(_env.ContentRootPath, "CHANGELOG.md");
                if (!File.Exists(path))
                {
                    _logger.LogWarning("CHANGELOG.md липсва в {Path}", path);
                    return _cache = new List<ChangelogEntry>();
                }

                _cache = Parse(File.ReadAllText(path));
                _cachedAt = DateTime.UtcNow;
                return _cache;
            }
            catch (Exception ex)
            {
                // The changelog is information, not a feature. If the read
                // fails, the tab is empty and the panel keeps working.
                _logger.LogError(ex, "Неуспешно четене на changelog.md");
                return _cache = new List<ChangelogEntry>();
            }
        }

        /// <summary>The number of the newest release, for the "new version"
        /// notice. Skips Unreleased.</summary>
        public string? LatestVersion() =>
            Read().FirstOrDefault(e => !e.IsUnreleased)?.Version;

        private static List<ChangelogEntry> Parse(string md)
        {
            var entries = new List<ChangelogEntry>();
            ChangelogEntry? current = null;
            var admin = new StringBuilder();
            var tech = new StringBuilder();
            var inTech = false;
            var listOpen = false;

            void CloseList()
            {
                if (!listOpen) return;
                (inTech ? tech : admin).Append("</ul>\n");
                listOpen = false;
            }

            void Flush()
            {
                if (current is null) return;
                CloseList();
                current.AdminHtml = admin.ToString().Trim();
                current.TechHtml = tech.ToString().Trim();
                if (current.HasAdmin || current.HasTech) entries.Add(current);
                admin.Clear();
                tech.Clear();
            }

            foreach (var raw in md.Split('\n'))
            {
                var line = raw.TrimEnd();

                // A version heading: ## [1.1.0] — 6 септември 2026
                var ver = Regex.Match(line, @"^##\s+\[([^\]]+)\]\s*[—\-–]?\s*(.*)$");
                if (ver.Success)
                {
                    Flush();
                    current = new ChangelogEntry
                    {
                        Version = ver.Groups[1].Value.Trim(),
                        Date = string.IsNullOrWhiteSpace(ver.Groups[2].Value)
                                   ? null : ver.Groups[2].Value.Trim()
                    };
                    inTech = false;
                    continue;
                }

                if (current is null) continue;   // the file header, before the first version

                // A section heading: ### Технически / ### Промени за администратора.
                // Both language spellings are matched, because the file has been
                // written in both.
                if (line.StartsWith("###"))
                {
                    CloseList();
                    inTech = line.Contains("Техническ", StringComparison.OrdinalIgnoreCase)
                          || line.Contains("Technical", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (line.StartsWith("---")) continue;

                // A list item.
                if (line.TrimStart().StartsWith("- "))
                {
                    var sb = inTech ? tech : admin;
                    if (!listOpen) { sb.Append("<ul class=\"cl-list\">\n"); listOpen = true; }
                    sb.Append("  <li>").Append(Inline(line.TrimStart()[2..])).Append("</li>\n");
                    continue;
                }

                // A continuation of the previous item: a wrapped line, indented
                // under its bullet.
                if (listOpen && line.Length > 0 && line.StartsWith("  "))
                {
                    var sb = inTech ? tech : admin;
                    // Reopen the last <li> by cutting its closing tag, append the
                    // continuation, close it again.
                    var s = sb.ToString();
                    var i = s.LastIndexOf("</li>", StringComparison.Ordinal);
                    if (i >= 0)
                    {
                        sb.Clear();
                        sb.Append(s[..i]).Append(' ').Append(Inline(line.Trim())).Append("</li>\n");
                    }
                    continue;
                }

                if (line.Length == 0) { CloseList(); continue; }
            }

            Flush();
            return entries;
        }

        /// <summary>
        /// Escapes everything, then brings back only `code` and **bold**.
        /// The order matters: escape first, insert afterwards — the other way
        /// round would let HTML from the file through.
        /// </summary>
        private static string Inline(string text)
        {
            var s = text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");

            s = Regex.Replace(s, @"`([^`]+)`", "<code>$1</code>");
            s = Regex.Replace(s, @"\*\*([^*]+)\*\*", "<b>$1</b>");
            return s;
        }
    }
}
