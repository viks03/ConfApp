// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text.Encodings.Web;

namespace ConferenceApp.Services.Email
{
    /// <summary>
    /// The values for one mail. It exists so that escaping is the default
    /// rather than something to remember.
    /// <para>
    /// <see cref="Set"/> ESCAPES — use it for everything that comes from a
    /// user or an administrator (rejection reason, name, organisation).
    /// Without it a reason containing &lt;script&gt; goes into the HTML raw.
    /// </para>
    /// <para>
    /// <see cref="SetRaw"/> does NOT escape — only for resx strings known to
    /// contain HTML (Email_..._MainText with &lt;strong&gt;, for instance).
    /// Never for user input.
    /// </para>
    /// </summary>
    public sealed class EmailPlaceholders
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        /// <summary>An escaped value. This is the right choice almost every
        /// time.</summary>
        public EmailPlaceholders Set(string key, string? value)
        {
            _values[key] = HtmlEncoder.Default.Encode(value ?? string.Empty);
            return this;
        }

        /// <summary>
        /// A multi-line value from user input (a rejection reason typed into a
        /// textarea by an administrator, for example).
        ///
        /// <para>
        /// Why it exists: HTML does not preserve newlines — they are just
        /// whitespace. One would expect them to collapse into a space, but the
        /// Gmail sanitizer REMOVES them while it reflows the text and the
        /// surrounding words run together: "Снимката\nне се вижда" arrives as
        /// "Снимкатане се вижда".
        /// </para>
        ///
        /// <para>
        /// The order of the two steps matters: escape first (so that any
        /// &lt;script&gt; becomes harmless), insert &lt;br&gt; only afterwards.
        /// The other way round would escape the &lt;br&gt; tags as well.
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

        /// <summary>A raw value — only for trusted HTML from resx.</summary>
        public EmailPlaceholders SetRaw(string key, string? value)
        {
            _values[key] = value ?? string.Empty;
            return this;
        }

        public IReadOnlyDictionary<string, string> Values => _values;

        public bool Contains(string key) => _values.ContainsKey(key);
    }
}
