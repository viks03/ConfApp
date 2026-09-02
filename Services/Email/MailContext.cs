using System.Globalization;
using ConferenceApp.Models;

namespace ConferenceApp.Services.Email
{
    /// <summary>
    /// Дребни помощници за местата, които изпращат имейл извън нормална
    /// потребителска заявка (Stripe и Go28 webhook-ове, админски действия).
    /// </summary>
    public static class MailContext
    {
        /// <summary>
        /// Публичният адрес на сайта — основата на всички линкове и изображения
        /// в имейлите.
        ///
        /// <para>
        /// Поведението се управлява от <c>AppSettings:ForceBaseUrl</c>:
        /// </para>
        /// <list type="bullet">
        ///   <item><b>0 (по подразбиране)</b> — адресът се строи от самата
        ///   заявка (<c>Request.Scheme</c> + <c>Request.Host</c>), както
        ///   работеше досега. Ако заявка няма — например при webhook от Stripe
        ///   или Go28, където Host е техният, а не нашият — се ползва
        ///   <c>AppSettings:BaseUrl</c>.</item>
        ///
        ///   <item><b>1</b> — винаги се ползва <c>AppSettings:BaseUrl</c>,
        ///   независимо откъде идва заявката. За тестване: при локално пускане
        ///   Request.Host е "localhost:5253", който телефонът не достига, и
        ///   всички изображения в имейла излизат счупени.</item>
        /// </list>
        /// </summary>
        public static string BaseUrl(IConfiguration config, HttpRequest? request = null)
        {
            var configured = (config["AppSettings:BaseUrl"] ?? "https://blockchainedu2026.unwe.bg")
                             .TrimEnd('/');

            // Приема "1", "true", "yes" — за да не се спъне някой в правописа.
            var raw = config["AppSettings:ForceBaseUrl"];
            var force = raw is not null &&
                        (raw.Trim() is "1" or "true" or "True" or "yes" or "Yes");

            if (force) return configured;

            // Стандартният път: адресът идва от заявката.
            if (request is not null && request.Host.HasValue)
                return $"{request.Scheme}://{request.Host}".TrimEnd('/');

            // Няма заявка (webhook, фонова задача) — падаме на конфигурацията.
            return configured;
        }

        /// <summary>
        /// Езикът, на който да излезе имейлът.
        /// <para>
        /// Днес ApplicationUser не пази предпочитан език, затова ползваме
        /// културата на текущата заявка. За потребителски действия това е
        /// правилно. За АДМИНСКИ действия и за webhook-ове е приблизително —
        /// виж EMAIL_SETUP.md, раздел "Езикът на получателя".
        /// </para>
        /// </summary>
        public static CultureInfo CultureFor(ApplicationUser? user = null)
        {
            // Когато потребителският модел получи поле за език, единствената
            // промяна е тук:
            //   if (!string.IsNullOrWhiteSpace(user?.PreferredLanguage))
            //       return new CultureInfo(user.PreferredLanguage);
            return CultureInfo.CurrentUICulture;
        }

        /// <summary>Име на участието, както се показва в имейла.</summary>
        public static string ParticipationName(string? partForm) => partForm switch
        {
            "1" => "Lector / Academic",
            "2" => "Student / PhD Candidate",
            "3" => "Online Participant",
            "4" => "Journalist / Media",
            _   => partForm ?? "—"
        };

        /// <summary>Човешко име на метода на плащане от PaymentMethod полето.</summary>
        public static string PaymentMethodName(string? method)
        {
            if (string.IsNullOrWhiteSpace(method)) return "—";
            if (method.StartsWith("Crypto", StringComparison.OrdinalIgnoreCase))
                return method.Replace("Crypto:", "Crypto ");
            if (method.StartsWith("Stripe", StringComparison.OrdinalIgnoreCase)) return "Card";
            return method switch
            {
                "Card"       => "Card",
                "Subsidised" => "Subsidised",
                "Manual"     => "Manual",
                "IBAN"       => "Bank transfer",
                _            => method
            };
        }
    }
}
