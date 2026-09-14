// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.ComponentModel.DataAnnotations;

namespace ConferenceApp.Models
{
    /// <summary>
    /// The visual layer settings for ONE page.
    ///
    /// <para>
    /// A row exists only once somebody has touched that page. No row is a valid
    /// state and means "whatever _GlobalEffects.cshtml falls back to", which is
    /// why the table is not pre-filled with twenty rows.
    /// </para>
    /// </summary>
    public class PageStyleSetting
    {
        public int Id { get; set; }

        /// <summary>
        /// The page key, with a leading slash — "/Index", "/Lecturers". It
        /// matches <c>RouteData.Values["page"]</c> exactly, so that the filter
        /// can find the row without translating anything.
        ///
        /// <para>
        /// One key is special: <c>"*"</c> holds the global settings, which
        /// belong to no page. It is a row in the SAME table rather than a new
        /// one: a single setting does not justify a migration, a table and a
        /// model, and the unique index on PageKey already guarantees there is
        /// only one of it.
        /// </para>
        /// </summary>
        [Required, MaxLength(64)]
        public string PageKey { get; set; } = string.Empty;

        /// <summary>The key of the row holding the global settings.</summary>
        public const string GlobalKey = "*";

        /// <summary>One of the built-in slugs (see
        /// <c>Services.Styles.AmbientBackgrounds</c>), or
        /// „custom:&lt;slug&gt;“.</summary>
        [Required, MaxLength(48)]
        public string Background { get; set; } = "grid";

        // ── The seven variables ───────────────────────────────────────────
        // null anywhere means "leave the value the CSS defines". That matters:
        // without it every saved row would pin seven values, and a later change
        // in globalEffects.css would reach no page at all.

        public double? Intensity   { get; set; }
        public double? Ink         { get; set; }
        public double? Glow        { get; set; }
        public double? CursorAlpha { get; set; }
        public int?    GridStep    { get; set; }
        public int?    PaperStep   { get; set; }
        public int?    BarHeight   { get; set; }

        /// <summary>The sanitized CSS, already scoped at the time it was
        /// saved. It is not processed again on read.</summary>
        [MaxLength(4000)]
        public string? CustomCss { get; set; }

        /// <summary>
        /// Deliberately a separate column from <see cref="CustomCss"/>:
        /// switching the CSS off does not delete what was written, so the same
        /// CSS comes back with one toggle.
        /// </summary>
        public bool CustomCssEnabled { get; set; }

        /// <summary>
        /// The motion of the background: null = as the preset intends,
        /// "off" = stopped, "slow" = slower, "fast" = faster.
        /// Deliberately NOT "which kind of motion" — see the note in
        /// globalEffects.css for why.
        /// </summary>
        [MaxLength(8)]
        public string? Motion { get; set; }

        /// <summary>
        /// Speed, independent of the kind: slower | slow | fast | faster.
        /// null = the duration the preset intends.
        /// </summary>
        [MaxLength(8)]
        public string? MotionSpeed { get; set; }

        /// <summary>
        /// Whether the background is drawn on a phone. NOT by default: on a
        /// small screen the gain is small and the cost — a full-screen gradient
        /// repainting on scroll, on battery — is not.
        /// </summary>
        public bool ShowOnMobile { get; set; }

        public DateTime? UpdatedAt { get; set; }

        [MaxLength(256)]
        public string? UpdatedBy { get; set; }
    }

    /// <summary>
    /// A custom background, assembled from parameters rather than from free
    /// CSS.
    /// <para>
    /// Why parameters: custom CSS on one page is a bounded risk — the scoping
    /// keeps it inside the background layer — but a background is used on
    /// several pages and stays there for months, which is the same risk
    /// multiplied and made permanent. Parameters can be validated by value; CSS
    /// can only be validated by shape.
    /// </para>
    /// </summary>
    public class CustomBackground
    {
        public int Id { get; set; }

        /// <summary>[a-z0-9-] only, because it goes straight into a CSS
        /// selector as data-gfx-bg="custom:&lt;slug&gt;".</summary>
        [Required, MaxLength(40)]
        public string Slug { get; set; } = string.Empty;

        [Required, MaxLength(60)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// "params" — the background is assembled from
        /// <see cref="LayersJson"/>.
        /// "css" — it is drawn from <see cref="RawCss"/>, written by hand.
        /// </summary>
        [Required, MaxLength(10)]
        public string Mode { get; set; } = "params";

        /// <summary>
        /// The two layers as JSON rather than as eighteen columns: the
        /// parameters of a layer are always read and written together, and a new
        /// parameter needs no migration. Used only when Mode == "params".
        /// </summary>
        [Required, MaxLength(1200)]
        public string LayersJson { get; set; } = "{}";

        /// <summary>
        /// Free CSS for <c>.afx-a</c> / <c>.afx-b</c>, already SANITIZED at the
        /// time it was saved. Used only when Mode == "css".
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
    /// A snapshot of the state before a change — the way back.
    ///
    /// <para>
    /// The justification is one sentence: custom CSS is the only place in the
    /// panel where an administrator can break a public page without anyone
    /// seeing an error. There has to be a way back that does not depend on
    /// remembering what you wrote.
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
