// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Globalization;
using ConferenceApp.Models;

namespace ConferenceApp.Services.Email
{
    /// <summary>
    /// Small helpers for the places that send mail outside a normal user
    /// request (the Stripe and Go28 webhooks, administrative actions).
    /// </summary>
    public static class MailContext
    {
        /// <summary>
        /// The public address of the site — the base of every link and image in
        /// the mails.
        ///
        /// <para>
        /// The behaviour is controlled by <c>AppSettings:ForceBaseUrl</c>:
        /// </para>
        /// <list type="bullet">
        ///   <item><b>0 (default)</b> — the address is built from the request
        ///   itself (<c>Request.Scheme</c> + <c>Request.Host</c>). When there
        ///   is no request — a webhook from Stripe or Go28, where the Host is
        ///   theirs and not ours — <c>AppSettings:BaseUrl</c> is used
        ///   instead.</item>
        ///
        ///   <item><b>1</b> — <c>AppSettings:BaseUrl</c> is always used, no
        ///   matter where the request comes from. This is for testing: running
        ///   locally, Request.Host is "localhost:5253", which a phone cannot
        ///   reach, and every image in the mail arrives broken.</item>
        /// </list>
        /// </summary>
        public static string BaseUrl(IConfiguration config, HttpRequest? request = null)
        {
            var configured = (config["AppSettings:BaseUrl"] ?? "https://blockchainedu2026.unwe.bg")
                             .TrimEnd('/');

            // Accepts "1", "true" and "yes", so that the exact spelling is not
            // something to get wrong.
            var raw = config["AppSettings:ForceBaseUrl"];
            var force = raw is not null &&
                        (raw.Trim() is "1" or "true" or "True" or "yes" or "Yes");

            if (force) return configured;

            // The normal path: the address comes from the request.
            if (request is not null && request.Host.HasValue)
                return $"{request.Scheme}://{request.Host}".TrimEnd('/');

            // No request (webhook, background task) — fall back to configuration.
            return configured;
        }

        /// <summary>The languages the application speaks. Nothing else is
        /// accepted.</summary>
        public static readonly string[] SupportedLanguages = ["bg", "en"];

        /// <summary>
        /// The language the mail goes out in — the RECIPIENT's language.
        /// <para>
        /// [T-29] This used to be the culture of the current request. For a
        /// mail the person triggered themselves (a sign-in code, a request for
        /// review) that is correct. For the four mails sent from the admin
        /// panel, however, the request belongs to the ADMINISTRATOR, and a
        /// Stripe or Go28 webhook has neither a user request nor a language
        /// cookie — so a foreign participant received their rejection reason in
        /// Bulgarian.
        /// </para>
        /// <para>
        /// The choice is therefore made in this order:
        /// </para>
        /// <list type="number">
        ///   <item>the recipient's stored language
        ///   (<see cref="ApplicationUser.PreferredLanguage"/>);</item>
        ///   <item>the culture of the request — for rows that predate the
        ///   column and carry no language, and when no recipient is passed.</item>
        /// </list>
        /// <para>
        /// The value is checked against <see cref="SupportedLanguages"/>: the
        /// column is a string, and a row edited from outside must not throw
        /// <see cref="CultureNotFoundException"/> somewhere in the background
        /// queue where nobody will see it.
        /// </para>
        /// </summary>
        public static CultureInfo CultureFor(ApplicationUser? user = null)
        {
            var preferred = Normalize(user?.PreferredLanguage);

            return preferred is null
                ? CultureInfo.CurrentUICulture
                : new CultureInfo(preferred);
        }

        /// <summary>
        /// Reduces a language to one of the supported ones, or to <c>null</c>.
        /// It accepts "bg-BG" as well, because that is exactly what
        /// <c>CultureInfo.CurrentUICulture</c> returns for a browser that sends
        /// a full culture code.
        /// </summary>
        public static string? Normalize(string? language)
        {
            if (string.IsNullOrWhiteSpace(language)) return null;

            var value = language.Trim();
            var dash = value.IndexOf('-');
            if (dash > 0) value = value[..dash];

            return SupportedLanguages.FirstOrDefault(
                l => string.Equals(l, value, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The participation type as it appears in the mail. The
        /// numbers are the stored PartForm values.</summary>
        public static string ParticipationName(string? partForm) => partForm switch
        {
            "1" => "Lector / Academic",
            "2" => "Student / PhD Candidate",
            "3" => "Online Participant",
            "4" => "Journalist / Media",
            _   => partForm ?? "—"
        };

        /// <summary>A human-readable payment method from the PaymentMethod
        /// column, which stores gateway-prefixed values such as
        /// "Crypto:USDT".</summary>
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
