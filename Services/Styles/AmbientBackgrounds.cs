// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Services.Styles
{
    /// <summary>One built-in background: the slug stored in the database, and
    /// the description shown in the admin guide. The description is UI text and
    /// stays in Bulgarian.</summary>
    public sealed record AmbientBackground(string Slug, string Description);

    /// <summary>
    /// <b>The single source for the built-in backgrounds.</b>
    ///
    /// <para>
    /// The list used to be written out in five places: <c>allowed</c> in
    /// <c>Index.cshtml.cs</c>, the radio group in <c>Index.cshtml</c>,
    /// <c>VALID_BG</c> in <c>wwwroot/js/adminPanelStyles.js</c>, the rules in
    /// <c>wwwroot/css/globalEffects.css</c>, and the guide in the panel. Four of
    /// the five agreed; the guide did not — it still listed the removed
    /// <c>beam</c> and a <c>dust</c> that never existed, while six live presets
    /// (fiber, lantern, prism, sonar, caustic, spine) were described nowhere.
    /// </para>
    ///
    /// <para>
    /// All three places in the panel — the validation, the picker and the guide
    /// — are built from here. <b>The fifth place is still the CSS</b>
    /// (<c>#ambient-fx[data-gfx-bg="…"]</c>): the drawing itself cannot be
    /// generated from C#. A new background means a rule in
    /// <c>globalEffects.css</c> <b>and</b> a line here — two places rather than
    /// five.
    /// </para>
    /// </summary>
    public static class AmbientBackgrounds
    {
        public static readonly IReadOnlyList<AmbientBackground> All = new[]
        {
            new AmbientBackground("grid",    "техническа решетка. Статична."),
            new AmbientBackground("paper",   "милиметрова хартия. Статична."),
            new AmbientBackground("hatch",   "диагонално щриховане. Бавен дрейф."),
            new AmbientBackground("glow",    "голямо акцентно сияние. Дрейф и „дишане“."),
            new AmbientBackground("contour", "топографски пръстени от точка извън екрана. Бавен пулс."),
            new AmbientBackground("fiber",   "снопове тънки нишки под лек наклон. Много бавно плъзгане."),
            new AmbientBackground("lantern", "фенерче: решетката се вижда само в кръг около курсора."),
            new AmbientBackground("prism",   "сноп лъчи, който тръгва от курсора."),
            new AmbientBackground("sonar",   "пръстени, чийто център е курсорът."),
            new AmbientBackground("caustic", "две вълни се пресичат; възлите се пренареждат при движение на мишката."),
            new AmbientBackground("spine",   "колона светлина под курсора. Статична."),
            new AmbientBackground("ascent",  "пояс, който се вдига със скрола — фонът показва докъде си стигнал."),
            new AmbientBackground("off",     "без фон. Прогрес лентата също изчезва."),
        };

        /// <summary>The slugs alone, in the order of <see cref="All"/>.</summary>
        public static readonly IReadOnlyList<string> Slugs =
            All.Select(b => b.Slug).ToArray();

        /// <summary>Whether this slug is a built-in one. A <c>custom:…</c> value
        /// is not.</summary>
        public static bool IsBuiltIn(string? slug) =>
            slug is not null && Slugs.Contains(slug, StringComparer.Ordinal);
    }
}
