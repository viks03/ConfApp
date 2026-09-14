// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    /// <summary>
    /// A theme — a set of values for the custom properties in <c>:root</c>.
    ///
    /// <para>
    /// <b>The property that matters most:</b> with NO active theme the site
    /// looks exactly as it does today. The values in <c>mainStyle.css</c> are
    /// untouched and serve as the base; a theme only overrides them. Switching
    /// every theme off returns the site to its original state without restoring
    /// anything.
    /// </para>
    /// </summary>
    public class SiteTheme
    {
        public int Id { get; set; }

        /// <summary>A short key: "dark", "light", "contrast", or one chosen
        /// when a theme is uploaded.</summary>
        [Required, MaxLength(48)]
        public string ThemeKey { get; set; } = string.Empty;

        [Required, MaxLength(80)]
        public string Name { get; set; } = string.Empty;

        /// <summary>The token values, already validated at import time — see
        /// <c>Services.Theming.ThemeTokens</c>.</summary>
        [Required, MaxLength(4000)]
        public string TokensJson { get; set; } = "{}";

        /// <summary>
        /// The built-in themes ship with the application and cannot be deleted
        /// from the panel — otherwise an administrator could be left without a
        /// single working theme.
        /// </summary>
        public bool IsBuiltIn { get; set; }

        /// <summary>Only ONE theme may be active. The handler enforces it;
        /// there is no database constraint.</summary>
        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        [MaxLength(256)]
        public string? UpdatedBy { get; set; }
    }
}
