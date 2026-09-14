// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ConferenceApp.Services.Email
{
    /// <summary>
    /// Reads the templates from disk, joins frame and body, substitutes the
    /// placeholders.
    ///
    /// <para>
    /// Why it exists: before this class, Register, Login and Verification each
    /// contained THE SAME ~40-line block — read a file, seven .Replace() calls,
    /// try/catch. Every template change had to be made three times, and all
    /// three behaved differently when the file was missing.
    /// </para>
    /// </summary>
    public sealed class EmailTemplateRenderer : IEmailTemplateRenderer
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<EmailTemplateRenderer> _logger;
        private readonly IHostEnvironment _hostEnv;

        // File contents are read once and kept in memory. Before that, every
        // mail sent meant a read from disk — 200 registrations, 200 pointless
        // I/O operations for a file that does not change.
        private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

        // Catches placeholders left unsubstituted, such as {Foo}.
        private static readonly Regex UnreplacedRx =
            new(@"\{([A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

        private const string LayoutKey = "__layout";

        public EmailTemplateRenderer(
            IWebHostEnvironment env,
            IHostEnvironment hostEnv,
            ILogger<EmailTemplateRenderer> logger)
        {
            _env = env;
            _hostEnv = hostEnv;
            _logger = logger;
        }

        public async Task<string> RenderAsync(EmailTemplate template, EmailPlaceholders placeholders,
                                              CancellationToken ct = default)
        {
            var layout = await LoadAsync(LayoutKey,
                Path.Combine(_env.WebRootPath, "templates", "_layout.html"), ct);

            var bodyFile = EmailTemplateFiles.FileName(template);
            var body = await LoadAsync(bodyFile,
                Path.Combine(_env.WebRootPath, "templates", "bodies", bodyFile), ct);

            // The substitution is a plain Replace and hits EVERY occurrence. If
            // the marker also appears inside a comment — in a sentence
            // explaining it, say — the body is inserted there too; the body's
            // own comments then close the surrounding one early and its content
            // shows up as visible text in the mail. This has happened, which is
            // why the check lives here rather than in the memory of whoever
            // edits the frame next.
            //
            // The token is written in two halves so that this file does not
            // contain the literal marker either.
            const string bodyToken = "{" + "Body" + "}";
            var tokenCount = CountOccurrences(layout, bodyToken);

            if (tokenCount != 1)
            {
                var msg = tokenCount == 0
                    ? "В _layout.html липсва маркерът за тялото."
                    : $"В _layout.html маркерът за тялото се среща {tokenCount} пъти — трябва точно веднъж. " +
                      "Провери дали не е споменат в коментар.";
                _logger.LogError("{Message}", msg);
                throw new InvalidOperationException(msg);
            }

            var html = layout.Replace(bodyToken, body);

            foreach (var (key, value) in placeholders.Values)
                html = html.Replace("{" + key + "}", value);

            VerifyNoLeftovers(html, template);
            return html;
        }

        public void ClearCache() => _cache.Clear();

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0, idx = 0;
            while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) != -1)
            {
                count++;
                idx += needle.Length;
            }
            return count;
        }

        private async Task<string> LoadAsync(string cacheKey, string path, CancellationToken ct)
        {
            if (_cache.TryGetValue(cacheKey, out var cached))
                return cached;

            if (!File.Exists(path))
            {
                // A missing template is a deployment defect, not something to
                // swallow. The three old call sites each reacted differently:
                // one logged, one returned false, one said nothing.
                var msg = $"Липсва имейл темплейт: {path}";
                _logger.LogError("{Message}", msg);
                throw new FileNotFoundException(msg, path);
            }

            var content = await File.ReadAllTextAsync(path, ct);
            _cache[cacheKey] = content;
            return content;
        }

        /// <summary>
        /// If a template contains {Foo} and nobody supplied a value for it, the
        /// user receives a mail with a literal "{Foo}" inside. This check
        /// catches that on the first test run rather than at the first real
        /// recipient.
        /// </summary>
        private void VerifyNoLeftovers(string html, EmailTemplate template)
        {
            var leftovers = UnreplacedRx.Matches(html)
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (leftovers.Count == 0) return;

            var joined = string.Join(", ", leftovers);
            _logger.LogError(
                "Незаместени плейсхолдъри в темплейт {Template}: {Placeholders}. " +
                "Имейлът ще излезе с буквален текст на тяхно място.",
                template, joined);

            // In development this throws: otherwise the defect travels on to
            // production. In production the mail still goes out — a placeholder
            // in the text is better than no mail at all.
            if (_hostEnv.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"Незаместени плейсхолдъри в {template}: {joined}");
            }
        }
    }
}
