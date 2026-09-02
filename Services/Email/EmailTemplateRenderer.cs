using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ConferenceApp.Services.Email
{
    /// <summary>
    /// Чете темплейтите от диска, слепва рамка + тяло, замества плейсхолдърите.
    ///
    /// <para>
    /// Защо съществува: преди тази класа Register, Login и Verification
    /// съдържаха ЕДИН И СЪЩ блок от ~40 реда — четене на файл, седем .Replace(),
    /// try/catch. Всяка промяна в темплейта трябваше да се направи три пъти и
    /// всяко от трите места се държеше различно при липсващ файл.
    /// </para>
    /// </summary>
    public sealed class EmailTemplateRenderer : IEmailTemplateRenderer
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<EmailTemplateRenderer> _logger;
        private readonly IHostEnvironment _hostEnv;

        // Съдържанието на файловете се чете веднъж и стои в паметта.
        // Преди това всеки изпратен имейл значеше четене от диска — при 200
        // регистрации 200 излишни I/O операции за файл, който не се променя.
        private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

        // Хваща останали незаместени плейсхолдъри, напр. {Foo}.
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

            // Заместването е обикновено Replace и хваща ВСЯКО срещане. Ако
            // маркерът се появи и в коментар (например в изречение, което го
            // обяснява), тялото се вмъква и там; собствените му коментари
            // прекратяват външния рано и съдържанието излиза като видим текст
            // в писмото. Случвало се е — затова проверката е тук, а не в главата
            // на този, който следващия път редактира рамката.
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
                // Липсващ темплейт е дефект в деплоя, не нещо за преглъщане.
                // Преди трите места реагираха различно: едното логваше, другото
                // връщаше false, третото мълчеше.
                var msg = $"Липсва имейл темплейт: {path}";
                _logger.LogError("{Message}", msg);
                throw new FileNotFoundException(msg, path);
            }

            var content = await File.ReadAllTextAsync(path, ct);
            _cache[cacheKey] = content;
            return content;
        }

        /// <summary>
        /// Ако темплейт съдържа {Foo}, а никой не е подал стойност за него,
        /// потребителят получава имейл с буквално "{Foo}" вътре. Тази проверка
        /// го хваща при първия тест, вместо при първия реален получател.
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

            // В разработка спираме веднага — иначе дефектът тръгва към продукция.
            if (_hostEnv.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"Незаместени плейсхолдъри в {template}: {joined}");
            }
        }
    }
}
