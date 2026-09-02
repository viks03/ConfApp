using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ConferenceApp.Services
{
    public class EmailSender
    {
        // Колко дълго чакаме SMTP сървъра, преди да прекъснем изпращането.
        // Пазим като constant, за да е на едно място с текста "timeout after
        // Xs" в CategorizeError (SendInvitations.cshtml.cs) — ако се смени
        // тук, трябва да се смени и там.
        private const int SmtpTimeoutMs = 15_000;

        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<EmailSender> _logger;

        // ── Инжектирай ILogger за production-grade logging ──────────────────────
        public EmailSender(
            IConfiguration config,
            IHttpContextAccessor httpContextAccessor,
            ILogger<EmailSender> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ────────────────────────────────────────────────────────────────────────
        //  ОСНОВЕН МЕТОД
        // ────────────────────────────────────────────────────────────────────────
        public async Task SendAsync(
            string to,
            string subject,
            string htmlBody,
            string? filePath = null)
        {
            // 1. Валидация на входните данни
            if (string.IsNullOrWhiteSpace(to))
                throw new ArgumentException("Recipient address cannot be empty.", nameof(to));
            if (string.IsNullOrWhiteSpace(subject))
                throw new ArgumentException("Email subject cannot be empty.", nameof(subject));
            if (string.IsNullOrWhiteSpace(htmlBody))
                throw new ArgumentException("Email body cannot be empty.", nameof(htmlBody));
            if (!IsValidEmail(to))
                throw new ArgumentException($"Invalid recipient email address: {to}", nameof(to));

            var settings = _config.GetSection("EmailSettings");
            ValidateSettings(settings);

            try
            {
                // 2. Замести {BaseUrl} безопасно
                string baseUrl = GetCurrentBaseUrl();
                string resolvedHtml = htmlBody.Replace("{BaseUrl}", baseUrl, StringComparison.OrdinalIgnoreCase);

                // 3. Изгради съобщението
                using var message = BuildMessage(to, subject, resolvedHtml, settings["From"]!, filePath);

                // 4. Изпрати
                // FIX: SmtpClient.Timeout официално важи само за синхронния
                // Send() — за SendMailAsync НЕ е гарантирано да прекъсва
                // връзката, ако сървърът просто не отговаря. Затова налагаме
                // собствен timeout през CancellationToken, независимо от
                // вътрешното поведение на SmtpClient.
                using var client = BuildSmtpClient(settings);
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(SmtpTimeoutMs));

                try
                {
                    await client.SendMailAsync(message, cts.Token);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    // Нашият собствен timeout е гръмнал (не external cancellation) —
                    // превръщаме го в TimeoutException, за да не се налага да пипаме
                    // CategorizeError в SendInvitations.cshtml.cs, който вече знае
                    // как да го покаже на админа.
                    throw new TimeoutException(
                        $"The mail server did not respond within {SmtpTimeoutMs / 1000} seconds.");
                }

                _logger.LogInformation("Email sent successfully to {Recipient} | Subject: {Subject}", to, subject);
            }
            catch (SmtpException ex)
            {
                // Логвай без да разкриваш sensitive данни в exception message
                _logger.LogError(ex, "SMTP error while sending email to {Recipient}", to);
                throw new InvalidOperationException("Failed to send email. Please try again later.", ex);
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "SMTP send to {Recipient} timed out after {TimeoutMs}ms", to, SmtpTimeoutMs);
                throw;
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                _logger.LogError(ex, "Unexpected error while sending email to {Recipient}", to);
                throw;
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        //  BUILDER МЕТОДИ
        // ────────────────────────────────────────────────────────────────────────
        private static MailMessage BuildMessage(
            string to,
            string subject,
            string htmlBody,
            string fromAddress,
            string? filePath)
        {
            var message = new MailMessage
            {
                From = new MailAddress(fromAddress, "Blockchain Education"),
                Subject = subject,
                // FIX: Задай SubjectEncoding за коректен UTF-8 в subject реда
                SubjectEncoding = System.Text.Encoding.UTF8,
                BodyEncoding = System.Text.Encoding.UTF8,
                IsBodyHtml = true // Outlook / Exchange изискват това
            };

            message.To.Add(to);

            // Plain-text алтернатива (задължителна за spam филтри)
            string plainText = StripHTML(htmlBody);
            var plainView = AlternateView.CreateAlternateViewFromString(
                plainText, System.Text.Encoding.UTF8, MediaTypeNames.Text.Plain);

            // HTML изглед с inline charset — критично за Gmail
            var htmlView = AlternateView.CreateAlternateViewFromString(
                htmlBody, System.Text.Encoding.UTF8, MediaTypeNames.Text.Html);

            message.AlternateViews.Add(plainView);
            message.AlternateViews.Add(htmlView);   // HTML трябва да е ПОСЛЕДЕН

            // Прикачен файл (само ако съществува)
            if (!string.IsNullOrWhiteSpace(filePath) && System.IO.File.Exists(filePath))
            {
                // FIX: Dispose на Attachment се управлява от MailMessage
                message.Attachments.Add(new Attachment(filePath));
            }

            return message;
        }

        private SmtpClient BuildSmtpClient(IConfiguration settings)
        {
            // FIX: Парсване с explicit проверки вместо сляп int.Parse
            if (!int.TryParse(settings["Port"], out int port) || port <= 0)
                throw new InvalidOperationException("EmailSettings:Port is missing or invalid.");

            if (!bool.TryParse(settings["EnableSsl"], out bool enableSsl))
                throw new InvalidOperationException("EmailSettings:EnableSsl is missing or invalid.");

            return new SmtpClient(settings["Host"])
            {
                Port = port,
                EnableSsl = enableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(
                    settings["UserName"],
                    settings["Password"]),
                // Вторична защита — реалният timeout за SendMailAsync се
                // налага през CancellationTokenSource в SendAsync (виж
                // SmtpTimeoutMs по-горе). Държим стойността еднаква с нея.
                Timeout = SmtpTimeoutMs
            };
        }

        // ────────────────────────────────────────────────────────────────────────
        //  ПОМОЩНИ МЕТОДИ
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Връща base URL на текущата заявка или fallback от конфигурацията.
        /// </summary>
        private string GetCurrentBaseUrl()
        {
            var request = _httpContextAccessor.HttpContext?.Request;
            if (request is not null)
            {
                // Използвай само схема + хост; игнорирай path/query
                return $"{request.Scheme}://{request.Host}";
            }

            string? fallback = _config["AppSettings:BaseUrl"];
            if (string.IsNullOrWhiteSpace(fallback))
            {
                _logger.LogWarning("AppSettings:BaseUrl not configured. Falling back to default URL.");
                return "https://blockchainedu2026.unwe.bg";
            }

            return fallback;
        }

        /// <summary>
        /// Валидира задължителните EmailSettings конфигурационни ключове.
        /// </summary>
        private static void ValidateSettings(IConfiguration settings)
        {
            string[] required = ["Host", "Port", "EnableSsl", "UserName", "Password", "From"];
            foreach (var key in required)
            {
                if (string.IsNullOrWhiteSpace(settings[key]))
                    throw new InvalidOperationException(
                        $"EmailSettings:{key} is missing or empty in configuration.");
            }
        }

        /// <summary>
        /// Бърза email валидация без RegEx (по-бърза от Regex за масово използване).
        /// </summary>
        private static bool IsValidEmail(string email)
        {
            try
            {
                var addr = new MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Конвертира HTML в plain-text за spam-safe алтернативен изглед.
        /// </summary>
        private static string StripHTML(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // 1. Премахни изцяло <style>…</style> и <head>…</head> блокове
            //    (без това CSS кодът изтича в plain-text версията)
            string result = Regex.Replace(input, @"<style[\s\S]*?</style>", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<script[\s\S]*?</script>", string.Empty, RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<head[\s\S]*?</head>", string.Empty, RegexOptions.IgnoreCase);

            // 2. Премахни MSO / HTML conditional comments <!--[if…]>…<![endif]-->
            result = Regex.Replace(result, @"<!--\[if[\s\S]*?<!\[endif\]-->", string.Empty, RegexOptions.IgnoreCase);

            // 3. Премахни всички останали HTML коментари
            result = Regex.Replace(result, @"<!--[\s\S]*?-->", string.Empty);

            // 3.5. FIX: преди generic strip-а по-долу да изяде <a> таговете,
            //      извади href-а до видим текст — иначе plain-text версията
            //      остава само с текста на линка ("Click here") и получателят
            //      без HTML mail клиент няма как изобщо да стигне до линка.
            result = Regex.Replace(
                result,
                @"<a\s+[^>]*href\s*=\s*[""']([^""']+)[""'][^>]*>([\s\S]*?)</a>",
                m =>
                {
                    var url = m.Groups[1].Value.Trim();
                    // Линк текстът може да съдържа вложени тагове (напр. <span>) —
                    // изчистваме ги, за да не се появят суров таг остатъци в резултата.
                    var linkText = Regex.Replace(m.Groups[2].Value, "<[^>]+>", string.Empty).Trim();

                    if (linkText.Length == 0 || linkText.Equals(url, StringComparison.OrdinalIgnoreCase))
                        return url;

                    return $"{linkText} ({url})";
                },
                RegexOptions.IgnoreCase);

            // 4. Нови редове при структурни тагове
            result = Regex.Replace(result, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</p\s*>", "\n\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</tr\s*>", "\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</h[1-6]\s*>", "\n\n", RegexOptions.IgnoreCase);

            // 5. Премахни всички останали HTML тагове
            result = Regex.Replace(result, "<[^>]+>", string.Empty);

            // 6. Декодирай HTML entities (&amp; → &, &zwnj; → празно и т.н.)
            result = WebUtility.HtmlDecode(result);

            // 7. Нормализирай излишните празни редове и whitespace
            result = Regex.Replace(result, @"[ \t]+", " ");           // множество интервали → един
            result = Regex.Replace(result, @" *\n *", "\n");          // интервали около нови редове
            result = Regex.Replace(result, @"\n{3,}", "\n\n");        // max 2 поредни нови реда

            return result.Trim();
        }
    }
}