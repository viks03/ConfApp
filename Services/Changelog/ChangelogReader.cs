using System.Text;
using System.Text.RegularExpressions;

namespace ConferenceApp.Services.Changelog
{
    public sealed class ChangelogEntry
    {
        public string Version { get; set; } = "";
        public string? Date { get; set; }

        /// <summary>Готов HTML за частта „за администратора".</summary>
        public string AdminHtml { get; set; } = "";

        /// <summary>Готов HTML за техническата част.</summary>
        public string TechHtml { get; set; } = "";

        public bool HasAdmin => AdminHtml.Length > 0;
        public bool HasTech => TechHtml.Length > 0;

        /// <summary>„Unreleased“ не е издание — панелът я показва различно.</summary>
        public bool IsUnreleased =>
            Version.Contains("unreleased", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Чете <c>CHANGELOG.md</c> от корена на проекта и го превръща в HTML.
    ///
    /// <para>
    /// <b>Защо собствен парсер, а не библиотека:</b> файлът е наш и има точно
    /// три конструкции — заглавие на версия, заглавие на раздел и списък.
    /// Пълен Markdown парсер е зависимост, ъпдейти и уязвимости заради нещо,
    /// което се събира в осемдесет реда.
    /// </para>
    ///
    /// <para>
    /// <b>Защо няма санитайзер:</b> файлът се пише от разработчика и влиза през
    /// git, не през форма. Всичко се екранира при извеждане, тоест дори да
    /// съдържа HTML, той ще излезе като текст. Ако някога стане редактируем от
    /// панела, това трябва да се преразгледа.
    /// </para>
    /// </summary>
    public sealed class ChangelogReader
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<ChangelogReader> _logger;

        // Файлът се чете рядко и не се мени между рестарти — държим го в паметта.
        private List<ChangelogEntry>? _cache;
        private DateTime _cachedAt;

        public ChangelogReader(IWebHostEnvironment env, ILogger<ChangelogReader> logger)
        {
            _env = env;
            _logger = logger;
        }

        public IReadOnlyList<ChangelogEntry> Read()
        {
            // Пет минути е достатъчно, за да не се чете при всяко отваряне на
            // таба, и достатъчно кратко, за да видиш промяна без рестарт.
            if (_cache is not null && (DateTime.UtcNow - _cachedAt).TotalMinutes < 5)
                return _cache;

            try
            {
                // CHANGELOG.md е в КОРЕНА на проекта, не в wwwroot.
                //
                // Причината не е подредба: wwwroot е публичната папка и всеки
                // би могъл да отвори сайтът.bg/changelog.md и да прочете
                // техническите бележки — кои полета сме добавили, къде е имало
                // бъг. Няма причина това да е публично.
                //
                // ContentRootPath сочи към корена и при разработка, и след
                // публикуване — стига файлът да се копира, което е нагласено
                // в ConferenceApp.csproj.
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
                // Changelog-ът е информация, не функция. Ако четенето се провали,
                // табът показва празно, а панелът работи.
                _logger.LogError(ex, "Неуспешно четене на changelog.md");
                return _cache = new List<ChangelogEntry>();
            }
        }

        /// <summary>Номерът на най-новото издание — за известието „има ново".</summary>
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

                // ## [1.1.0] — 6 септември 2026
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

                if (current is null) continue;   // шапката на файла

                // ### Технически  /  ### Промени за администратора
                if (line.StartsWith("###"))
                {
                    CloseList();
                    inTech = line.Contains("Техническ", StringComparison.OrdinalIgnoreCase)
                          || line.Contains("Technical", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (line.StartsWith("---")) continue;

                // - точка от списък
                if (line.TrimStart().StartsWith("- "))
                {
                    var sb = inTech ? tech : admin;
                    if (!listOpen) { sb.Append("<ul class=\"cl-list\">\n"); listOpen = true; }
                    sb.Append("  <li>").Append(Inline(line.TrimStart()[2..])).Append("</li>\n");
                    continue;
                }

                // продължение на предишната точка (пренесен ред)
                if (listOpen && line.Length > 0 && line.StartsWith("  "))
                {
                    var sb = inTech ? tech : admin;
                    // отрязваме затварящия </li>, дописваме, затваряме пак
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
        /// Екранира всичко, после връща само `код` и **удебелено**.
        /// Редът е важен: първо екранираме, после вмъкваме — обратното би
        /// позволило HTML от файла да мине.
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
