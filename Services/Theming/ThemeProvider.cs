// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text.Json;
using ConferenceApp.Data;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Services.Theming
{
    /// <summary>
    /// Serves the CSS of the active theme — or nothing.
    ///
    /// <para>
    /// <b>The safety rule:</b> on any doubt it returns empty. No active theme,
    /// corrupted data, an unreachable database — the result is the same: an
    /// empty string, and the site looks exactly as <c>mainStyle.css</c> says. A
    /// theme cannot break the site, because all it does is OVERRIDE values;
    /// having none is the normal state.
    /// </para>
    ///
    /// <para>
    /// The result is cached: a theme changes rarely and is read on every
    /// request. The panel clears the cache when it changes one.
    /// </para>
    /// </summary>
    public sealed class ThemeProvider
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<ThemeProvider> _logger;

        private string? _css;
        private string? _activeKey;
        private bool _loaded;
        private readonly object _lock = new();

        public ThemeProvider(IServiceScopeFactory scopes, ILogger<ThemeProvider> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        /// <summary>The CSS of the active theme, or empty if there is none.</summary>
        public string GetCss()
        {
            if (_loaded) return _css ?? string.Empty;

            lock (_lock)
            {
                if (_loaded) return _css ?? string.Empty;
                Load();
                _loaded = true;
                return _css ?? string.Empty;
            }
        }

        public string? ActiveKey
        {
            get { GetCss(); return _activeKey; }
        }

        /// <summary>Called by the panel after every change, so that the next
        /// request reloads the theme.</summary>
        public void Invalidate()
        {
            lock (_lock)
            {
                _loaded = false;
                _css = null;
                _activeKey = null;
            }
        }

        private void Load()
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var theme = db.SiteThemes.AsNoTracking().FirstOrDefault(t => t.IsActive);
                if (theme is null) return;          // no theme is the normal state

                var check = ThemeTokens.Parse(theme.TokensJson);
                if (!check.Ok)
                {
                    // The stored values were validated on import. If they do not
                    // pass now, somebody has edited them outside the panel —
                    // better for the site to look standard than broken.
                    _logger.LogError("Активната тема „{Key}“ съдържа невалидни стойности: {Errors}",
                        theme.ThemeKey, string.Join("; ", check.Errors));
                    return;
                }

                _css = ThemeTokens.BuildCss(check.Values);
                _activeKey = theme.ThemeKey;
            }
            catch (Exception ex)
            {
                // A theme is decoration. If the read fails, the site runs on the
                // values in the CSS file.
                _logger.LogError(ex, "Неуспешно зареждане на темата.");
            }
        }
    }
}
