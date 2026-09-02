using System.Text.Encodings.Web;

namespace ConferenceApp.Services.Email
{
    /// <summary>
    /// Стойностите за един имейл. Съществува, за да направи екранирането
    /// решение по подразбиране, а не нещо, което трябва да се помни.
    /// <para>
    /// <see cref="Set"/> ЕКРАНИРА — ползвай го за всичко, което идва от
    /// потребител или администратор (причина за отхвърляне, име, организация).
    /// Без това причина съдържаща &lt;script&gt; влиза сурова в HTML-а.
    /// </para>
    /// <para>
    /// <see cref="SetRaw"/> НЕ екранира — само за низове от resx, за които
    /// знаем, че съдържат HTML (напр. Email_..._MainText с &lt;strong&gt;).
    /// Никога за потребителски вход.
    /// </para>
    /// </summary>
    public sealed class EmailPlaceholders
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        /// <summary>Екранирана стойност. Това е правилният избор в 95% от случаите.</summary>
        public EmailPlaceholders Set(string key, string? value)
        {
            _values[key] = HtmlEncoder.Default.Encode(value ?? string.Empty);
            return this;
        }

        /// <summary>
        /// Многоредова стойност от потребителски вход (напр. причина за
        /// отхвърляне, писана в textarea от администратор).
        ///
        /// <para>
        /// Защо съществува: HTML сам по себе си не пази нови редове — те са
        /// просто празно място. Очакването е да се свият до интервал, но
        /// sanitizer-ът на Gmail ги МАХА при пренареждането на текста и
        /// съседните думи се слепват: "Снимката\nне се вижда" излиза като
        /// "Снимкатане се вижда".
        /// </para>
        ///
        /// <para>
        /// Редът на действията тук е важен: първо се екранира (за да стане
        /// евентуален &lt;script&gt; безобиден), чак после се вмъкват &lt;br&gt;.
        /// Обратното би екранирало и самите &lt;br&gt;.
        /// </para>
        /// </summary>
        public EmailPlaceholders SetMultiline(string key, string? value)
        {
            var encoded = HtmlEncoder.Default.Encode(value ?? string.Empty);

            encoded = encoded
                .Replace("\r\n", "<br />")
                .Replace("\n",    "<br />")
                .Replace("\r",    "<br />");

            _values[key] = encoded;
            return this;
        }

        /// <summary>Сурова стойност — само за доверен HTML от resx.</summary>
        public EmailPlaceholders SetRaw(string key, string? value)
        {
            _values[key] = value ?? string.Empty;
            return this;
        }

        public IReadOnlyDictionary<string, string> Values => _values;

        public bool Contains(string key) => _values.ContainsKey(key);
    }
}
