// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text;
using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Services.Styles
{
    /// <summary>
    /// Fills <c>ViewData["GfxVars"]</c> and <c>ViewData["GfxCustomCss"]</c> for
    /// every public page.
    ///
    /// <para>
    /// <b>Why a filter rather than a line in each page:</b> there are more than
    /// twenty public pages. Filling it in by hand means twenty places to
    /// remember — and they are forgotten when a new page is added, at which
    /// point the background simply says nothing. The filter is attached once and
    /// covers everything, pages that do not exist yet included.
    /// </para>
    ///
    /// <para>
    /// The same pattern as <c>AdminAuditFilter</c>, which already works this way
    /// in the project.
    /// </para>
    /// </summary>
    public sealed class GlobalStylesFilter : IAsyncPageFilter
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<GlobalStylesFilter> _logger;

        public GlobalStylesFilter(ApplicationDbContext db, ILogger<GlobalStylesFilter> logger)
        {
            _db = db;
            _logger = logger;
        }

        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context)
            => Task.CompletedTask;

        public async Task OnPageHandlerExecutionAsync(
            PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            await LoadAsync(context);
            await next();
        }

        private async Task LoadAsync(PageHandlerExecutingContext context)
        {
            try
            {
                // The admin panel has a light theme of its own and never renders
                // the ambient layer, so there is nothing to read for it.
                var area = context.RouteData.Values["area"]?.ToString();
                if (string.Equals(area, "Admin", StringComparison.OrdinalIgnoreCase)) return;

                if (context.HandlerInstance is not PageModel page) return;

                var pageKey = context.RouteData.Values["page"]?.ToString();
                if (string.IsNullOrWhiteSpace(pageKey)) return;

                // The global row ("*") is always read: it can switch the
                // background off on phones for the whole site, whatever the page
                // row says. One more row per request, but with AsNoTracking and
                // the index on PageKey that is cheap.
                var rows = await _db.PageStyleSettings
                                    .AsNoTracking()
                                    .Where(x => x.PageKey == pageKey || x.PageKey == PageStyleSetting.GlobalKey)
                                    .ToListAsync();

                var global = rows.FirstOrDefault(r => r.PageKey == PageStyleSetting.GlobalKey);
                var row = rows.FirstOrDefault(r => r.PageKey == pageKey);

                // The "*" row exists only once the administrator has touched the
                // global switch. When it says off, the ban applies everywhere and
                // overrides the page setting: a global switch that skipped the
                // pages somebody had enabled individually would not be global.
                // It is a safety valve, not a preference.
                var mobileBlockedGlobally = global is not null && !global.ShowOnMobile;

                if (row is null) return;   // no row → the defaults in the CSS apply

                // The background and the intensity go through the older partial,
                // which already reads them from ViewData.
                page.ViewData["GfxBg"] = row.Background;
                if (row.Intensity.HasValue)
                    page.ViewData["GfxIntensity"] = row.Intensity.Value.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);

                if (!string.IsNullOrWhiteSpace(row.Motion))
                    page.ViewData["GfxMotion"] = row.Motion;

                if (!string.IsNullOrWhiteSpace(row.MotionSpeed))
                    page.ViewData["GfxSpeed"] = row.MotionSpeed;

                // Nothing is set when it is off: the CSS hides the layer on a
                // phone by default anyway. Only the "yes" has to be signalled.
                if (row.ShowOnMobile && !mobileBlockedGlobally)
                    page.ViewData["GfxMobile"] = "on";

                // The six remaining variables go through the newer partial as a
                // single inline custom-property string.
                var vars = new StringBuilder();
                Append(vars, "--gfx-ink",          row.Ink);
                Append(vars, "--gfx-glow",         row.Glow);
                Append(vars, "--gfx-cursor-alpha", row.CursorAlpha);
                AppendPx(vars, "--gfx-grid",       row.GridStep);
                AppendPx(vars, "--gfx-step",       row.PaperStep);
                AppendPx(vars, "--gfx-bar-height", row.BarHeight);

                if (vars.Length > 0) page.ViewData["GfxVars"] = vars.ToString();

                // CustomCss is sanitized ON WRITE and is not processed again
                // here. If the sanitizing rules ever change, the stored rows have
                // to be run through again — not cleaned on read, or every page
                // load pays for it.
                //
                // A custom background is assembled HERE, by the same code that
                // feeds the preview in the panel, so what the administrator sees
                // and what the site draws cannot drift apart.
                var extra = new StringBuilder();

                if (row.Background.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
                {
                    var slug = row.Background[7..];
                    var bg = await _db.CustomBackgrounds.AsNoTracking()
                                      .FirstOrDefaultAsync(b => b.Slug == slug && b.IsActive);

                    if (bg is not null)
                        extra.Append(string.Equals(bg.Mode, "css", StringComparison.OrdinalIgnoreCase)
                            ? CustomBackgroundCss.BuildFromCss(bg.Slug, bg.RawCss)
                            : CustomBackgroundCss.Build(bg.Slug, bg.LayersJson));
                    else
                        // The background has been deleted or switched off while a
                        // page still points at it. A plain grid is better than a
                        // selector that matches nothing.
                        page.ViewData["GfxBg"] = "grid";
                }

                if (row.CustomCssEnabled && !string.IsNullOrWhiteSpace(row.CustomCss))
                    extra.Append(row.CustomCss);

                if (extra.Length > 0) page.ViewData["GfxCustomCss"] = extra.ToString();
            }
            catch (Exception ex)
            {
                // The visual layer is decoration. If the read fails, the page has
                // to load without it rather than fail.
                _logger.LogError(ex, "Неуспешно четене на стиловете за страницата.");
            }
        }

        private static void Append(StringBuilder sb, string name, double? value)
        {
            if (!value.HasValue) return;
            sb.Append(name).Append(':')
              .Append(value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append(';');
        }

        private static void AppendPx(StringBuilder sb, string name, int? value)
        {
            if (!value.HasValue) return;
            sb.Append(name).Append(':').Append(value.Value).Append("px;");
        }
    }
}
