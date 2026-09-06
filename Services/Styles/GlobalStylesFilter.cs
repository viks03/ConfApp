using System.Text;
using ConferenceApp.Data;
using ConferenceApp.Models;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Services.Styles
{
    /// <summary>
    /// Попълва <c>ViewData["GfxVars"]</c> и <c>ViewData["GfxCustomCss"]</c> за
    /// всяка публична страница.
    ///
    /// <para>
    /// <b>Защо филтър, а не ред във всяка страница:</b> публичните страници са
    /// над двайсет. Ръчното попълване значи двайсет места, които трябва да се
    /// помнят — и се забравят при добавяне на нова страница, при което фонът
    /// просто мълчи. Филтърът се закача веднъж и покрива всичко, включително
    /// страници, които още не съществуват.
    /// </para>
    ///
    /// <para>
    /// Същият модел като <c>AdminAuditFilter</c>, който вече работи в проекта.
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
                // Админ панелът има собствена светла тема — слоят не се рендира
                // там изобщо, значи няма и какво да се чете.
                var area = context.RouteData.Values["area"]?.ToString();
                if (string.Equals(area, "Admin", StringComparison.OrdinalIgnoreCase)) return;

                if (context.HandlerInstance is not PageModel page) return;

                var pageKey = context.RouteData.Values["page"]?.ToString();
                if (string.IsNullOrWhiteSpace(pageKey)) return;

                // Глобалният ред („*") се чете винаги — той може да изключи
                // фона на телефон за целия сайт, независимо какво пише на
                // страницата. Едно четене повече, но с AsNoTracking и индекс
                // по PageKey е евтино.
                var rows = await _db.PageStyleSettings
                                    .AsNoTracking()
                                    .Where(x => x.PageKey == pageKey || x.PageKey == PageStyleSetting.GlobalKey)
                                    .ToListAsync();

                var global = rows.FirstOrDefault(r => r.PageKey == PageStyleSetting.GlobalKey);
                var row = rows.FirstOrDefault(r => r.PageKey == pageKey);

                // Глобалната забрана за телефон бие настройката на страницата:
                // тя е предпазен клапан, не предпочитание. Ако беше обратното,
                // изключването „за целия сайт" щеше да пропуска страниците,
                // които някой е включил поотделно — тоест нямаше да е глобално.
                // Ред „*" СЪЩЕСТВУВА само ако администраторът е пипал
                // глобалния превключвател. Ако го е изключил, забраната важи
                // навсякъде и бие настройката на страницата: глобалното
                // изключване, което пропуска включените поотделно страници,
                // не е глобално.
                var mobileBlockedGlobally = global is not null && !global.ShowOnMobile;

                if (row is null) return;   // няма запис → резервните стойности

                // Фонът и силата минават през СТАРИЯ partial, който вече ги чете.
                page.ViewData["GfxBg"] = row.Background;
                if (row.Intensity.HasValue)
                    page.ViewData["GfxIntensity"] = row.Intensity.Value.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);

                if (!string.IsNullOrWhiteSpace(row.Motion))
                    page.ViewData["GfxMotion"] = row.Motion;

                if (!string.IsNullOrWhiteSpace(row.MotionSpeed))
                    page.ViewData["GfxSpeed"] = row.MotionSpeed;

                // Нищо не се задава при забрана — CSS-ът и без това скрива
                // слоя на телефон по подразбиране. Нужен е само сигналът „да".
                if (row.ShowOnMobile && !mobileBlockedGlobally)
                    page.ViewData["GfxMobile"] = "on";

                // Шестте останали променливи — през новия partial.
                var vars = new StringBuilder();
                Append(vars, "--gfx-ink",          row.Ink);
                Append(vars, "--gfx-glow",         row.Glow);
                Append(vars, "--gfx-cursor-alpha", row.CursorAlpha);
                AppendPx(vars, "--gfx-grid",       row.GridStep);
                AppendPx(vars, "--gfx-step",       row.PaperStep);
                AppendPx(vars, "--gfx-bar-height", row.BarHeight);

                if (vars.Length > 0) page.ViewData["GfxVars"] = vars.ToString();

                // CustomCss вече е санитизиран ПРИ ЗАПИС — тук не се обработва
                // наново. Ако някога се промени начинът на санитизиране, старите
                // записи трябва да се преминат наново, не да се чистят при четене
                // (иначе всяко зареждане плаща цената).
                // Собствен фон: правилата се сглобяват ТУК, от същия код, който
                // храни и прегледа в панела. Затова показаното в админа и
                // нарисуваното на сайта не могат да се разминат.
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
                        // Фонът е изтрит или изключен, докато страницата още го
                        // сочи. По-добре празен фон, отколкото счупен селектор.
                        page.ViewData["GfxBg"] = "grid";
                }

                if (row.CustomCssEnabled && !string.IsNullOrWhiteSpace(row.CustomCss))
                    extra.Append(row.CustomCss);

                if (extra.Length > 0) page.ViewData["GfxCustomCss"] = extra.ToString();
            }
            catch (Exception ex)
            {
                // Визуалният слой е украса. Ако четенето се провали, страницата
                // трябва да се зареди без него, а не да гръмне.
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
