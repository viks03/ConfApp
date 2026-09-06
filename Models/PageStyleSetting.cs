using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    /// <summary>
    /// Настройка на визуалния слой за ЕДНА страница.
    ///
    /// <para>
    /// Запис съществува само ако някой е пипал страницата. Липсата на запис е
    /// валидно състояние и значи „резервната стойност от _GlobalEffects.cshtml“ —
    /// затова таблицата не се пълни предварително с двайсет реда.
    /// </para>
    /// </summary>
    public class PageStyleSetting
    {
        public int Id { get; set; }

        /// <summary>
        /// Ключът на страницата с водеща наклонена черта — „/Index“, „/Lecturers“.
        /// Съвпада точно с <c>RouteData.Values["page"]</c>, за да може partial-ът
        /// да намери записа без превод.
        /// </summary>
        /// <summary>
        /// Ключът на страницата с водеща наклонена черта — „/Index“.
        ///
        /// <para>
        /// Един ключ е специален: <c>"*"</c> държи глобалните настройки, които
        /// не принадлежат на страница. Прави се с ред в СЪЩАТА таблица, а не с
        /// нова: една настройка не оправдава миграция, таблица и модел, а
        /// уникалният индекс по PageKey и без това гарантира, че е един.
        /// </para>
        /// </summary>
        [Required, MaxLength(64)]
        public string PageKey { get; set; } = string.Empty;

        /// <summary>Ключът на реда с глобалните настройки.</summary>
        public const string GlobalKey = "*";

        /// <summary>Един от осемте вградени, или „custom:&lt;slug&gt;“.</summary>
        [Required, MaxLength(48)]
        public string Background { get; set; } = "grid";

        // ── Седемте променливи ────────────────────────────────────────────
        // null навсякъде значи „остави стойността от CSS-а“. Това е важно:
        // без него всеки запис би заковал седем стойности и по-късна промяна
        // в globalEffects.css не би стигала до нито една страница.

        public double? Intensity   { get; set; }
        public double? Ink         { get; set; }
        public double? Glow        { get; set; }
        public double? CursorAlpha { get; set; }
        public int?    GridStep    { get; set; }
        public int?    PaperStep   { get; set; }
        public int?    BarHeight   { get; set; }

        /// <summary>Санитизираният CSS — вече с наложен обхват при записа.</summary>
        [MaxLength(4000)]
        public string? CustomCss { get; set; }

        /// <summary>
        /// Отделно поле от <see cref="CustomCss"/> нарочно: изключването не трие
        /// написаното и админът връща същия CSS с един превключвател.
        /// </summary>
        public bool CustomCssEnabled { get; set; }

        /// <summary>
        /// Движението на фона: null = както присетът е замислен,
        /// "off" = спряно, "slow" = по-бавно, "fast" = по-бързо.
        /// Съзнателно НЕ е „кой вид движение“ — виж бележката в
        /// globalEffects.css за причината.
        /// </summary>
        [MaxLength(8)]
        public string? Motion { get; set; }

        /// <summary>
        /// Скорост, независима от вида: slower | slow | fast | faster.
        /// null = продължителността, която присетът е замислил.
        /// </summary>
        [MaxLength(8)]
        public string? MotionSpeed { get; set; }

        /// <summary>
        /// Показва ли се фонът на телефон. По подразбиране НЕ — на малък екран
        /// печалбата е малка, а цената (пълноекранен градиент при скрол,
        /// върху батерия) не е.
        /// </summary>
        public bool ShowOnMobile { get; set; }

        public DateTime? UpdatedAt { get; set; }

        [MaxLength(256)]
        public string? UpdatedBy { get; set; }
    }

    /// <summary>
    /// Собствен фон, сглобен от параметри — не от свободен CSS.
    /// <para>
    /// Причината за параметри: собственият CSS на една страница е ограничен
    /// риск (обхватът го държи във фона), но фон се ползва на няколко страници
    /// и остава там с месеци — същият риск, умножен и траен. Параметрите се
    /// валидират по стойност, CSS може да се валидира само по форма.
    /// </para>
    /// </summary>
    public class CustomBackground
    {
        public int Id { get; set; }

        /// <summary>Само [a-z0-9-]; ползва се като data-gfx-bg="custom:&lt;slug&gt;".</summary>
        [Required, MaxLength(40)]
        public string Slug { get; set; } = string.Empty;

        [Required, MaxLength(60)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// „params“ — фонът се сглобява от <see cref="LayersJson"/>.
        /// „css“ — рисува се от <see cref="RawCss"/>, писан на ръка.
        /// </summary>
        [Required, MaxLength(10)]
        public string Mode { get; set; } = "params";

        /// <summary>
        /// Двата слоя като JSON, не като осемнайсет колони: параметрите на един
        /// слой се четат и записват наведнъж, а нов параметър не иска миграция.
        /// Ползва се само при Mode == "params".
        /// </summary>
        [Required, MaxLength(1200)]
        public string LayersJson { get; set; } = "{}";

        /// <summary>
        /// Свободен CSS за <c>.afx-a</c> / <c>.afx-b</c>, вече САНИТИЗИРАН при
        /// запис. Ползва се само при Mode == "css".
        /// </summary>
        [MaxLength(6000)]
        public string? RawCss { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        [MaxLength(256)]
        public string? UpdatedBy { get; set; }
    }

    /// <summary>
    /// Снимка на състоянието преди промяна — пътят назад.
    ///
    /// <para>
    /// Оправдава се с едно изречение: собственият CSS е единственото място в
    /// панела, където админът може да развали публична страница, без някой да
    /// види грешка. Трябва връщане, което не изисква да си помниш какво си
    /// написал.
    /// </para>
    /// </summary>
    public class PageStyleRevision
    {
        public int Id { get; set; }

        [Required, MaxLength(64)]
        public string PageKey { get; set; } = string.Empty;

        [Required, MaxLength(6000)]
        public string SnapshotJson { get; set; } = "{}";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(256)]
        public string? CreatedBy { get; set; }
    }
}
