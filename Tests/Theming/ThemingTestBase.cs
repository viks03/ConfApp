// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Globalization;
using System.Text.RegularExpressions;
using ConferenceApp.Tests.Admin;
using ConferenceApp.Tests.Fixtures;
using ConferenceApp.Tests.Pages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace ConferenceApp.Tests.Theming;

/// <summary>
/// The common ground of part 9. It stands on part 7's scaffolding
/// (<see cref="PageTestBase"/>) — the same way of opening a browser, the same
/// collecting of the console, the same catalogue of pages — and adds the two
/// things this part needs on top: activating a theme and setting a background
/// preset.
///
/// <para>
/// ⚠️ <b>The active theme and the page style rows are SHARED.</b> There is no
/// test-only instance: the same row serves the admin panel, the home page and
/// every later test. Every change is therefore undone in
/// <see cref="DisposeAsync"/> rather than at the end of the test body: if an
/// assertion fails halfway through, the site still has to be left as it started.
/// Otherwise the failure surfaces in a DIFFERENT part.
/// </para>
/// </summary>
public abstract class ThemingTestBase : PageTestBase
{
    private bool _themeTouched;
    private readonly List<string> _styleKeys = new();

    protected ThemingTestBase(AppFixture app, string tag) : base(app, tag) { }

    public override async Task DisposeAsync()
    {
        if (_themeTouched)
        {
            // Switched off rather than restored to "the previous theme": before part
            // 9 there is no active theme at all (see the check in the README after a
            // run).
            using var admin = await SignedInAdminAsync();
            await PostAsync(admin, "ActivateTheme", new Dictionary<string, string> { ["themeKey"] = "" });
        }

        if (_styleKeys.Count > 0)
        {
            await App.Db.WriteAsync(async db =>
            {
                foreach (var key in _styleKeys)
                {
                    var row = await db.PageStyleSettings.FirstOrDefaultAsync(p => p.PageKey == key);
                    if (row != null) db.PageStyleSettings.Remove(row);

                    var revisions = await db.PageStyleRevisions.Where(r => r.PageKey == key).ToListAsync();
                    db.PageStyleRevisions.RemoveRange(revisions);
                }
            });
        }

        await base.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // The admin panel: where the theme and the preset are changed
    // ════════════════════════════════════════════════════════════════════

    protected async Task<HttpSession> SignedInAdminAsync()
    {
        var session = App.NewSession();
        await session.LoginAdminAsync(App.Credentials.AdminEmail, App.Credentials.AdminPassword);
        return session;
    }

    protected static async Task<AdminReply> PostAsync(
        HttpSession session, string handler, Dictionary<string, string>? fields = null)
    {
        using var response = await session.PostHandlerAsync("/Admin", handler, fields);
        return await AdminReply.ReadAsync(response);
    }

    /// <summary>
    /// Activates a built-in theme, the same way a person activates it.
    /// <para>
    /// The panel itself is opened first: the built-in themes are seeded from the
    /// files when it is opened rather than by a migration, so before that first
    /// opening they are not in the database at all.
    /// </para>
    /// </summary>
    protected async Task ActivateThemeAsync(string themeKey)
    {
        _themeTouched = true;

        using var admin = await SignedInAdminAsync();
        await PanelAsync(admin);

        var reply = await PostAsync(admin, "ActivateTheme",
            new Dictionary<string, string> { ["themeKey"] = themeKey });

        Assert.True(reply.Success, $"Темата „{themeKey}“ не се активира: {reply.Message}");
    }

    /// <summary>Returns the site to the values in <c>mainStyle.css</c>.</summary>
    protected async Task DeactivateThemeAsync()
    {
        _themeTouched = true;

        using var admin = await SignedInAdminAsync();
        var reply = await PostAsync(admin, "ActivateTheme",
            new Dictionary<string, string> { ["themeKey"] = "" });

        Assert.True(reply.Success, $"Темата не се изключи: {reply.Message}");
    }

    private static async Task<string> PanelAsync(HttpSession session)
    {
        using var response = await session.Client.GetAsync("/Admin");
        response.EnsureSuccessStatusCode();
        return await response.ReadPageAsync();
    }

    // ── The background preset ───────────────────────────────────────────

    /// <summary>The form on the Visual Styles tab: every field, as the panel sends them.</summary>
    protected static Dictionary<string, string> StyleForm(
        string pageKey, string background,
        string? motion = null, string? speed = null, bool showOnMobile = false) => new()
    {
        ["pageKey"]          = pageKey,
        ["background"]       = background,
        ["intensity"]        = "0.5",
        ["ink"]              = "0.08",
        ["glow"]             = "0.2",
        ["cursorAlpha"]      = "0.1",
        ["gridStep"]         = "48",
        ["paperStep"]        = "12",
        ["barHeight"]        = "3",
        ["customCss"]        = "",
        ["customCssEnabled"] = "false",
        ["motion"]           = motion ?? "",
        ["motionSpeed"]      = speed ?? "",
        ["showOnMobile"]     = showOnMobile ? "true" : "false"
    };

    /// <summary>Sets a page's style and notes it down to be restored after the test.</summary>
    protected async Task SavePageStyleAsync(Dictionary<string, string> form)
    {
        var key = form["pageKey"];
        if (!_styleKeys.Contains(key)) _styleKeys.Add(key);

        using var admin = await SignedInAdminAsync();
        var reply = await PostAsync(admin, "SavePageStyle", form);

        Assert.True(reply.Success, $"Стилът на {key} не се записа: {reply.Message}");
    }

    // ════════════════════════════════════════════════════════════════════
    // Reading from the rendered page
    // ════════════════════════════════════════════════════════════════════

    /// <summary>The value of a CSS variable as the browser sees it.</summary>
    protected static async Task<string> VarAsync(IPage page, string name) =>
        (await page.EvaluateAsync<string>(
            "n => getComputedStyle(document.documentElement).getPropertyValue(n)", name)).Trim();

    /// <summary>A computed property of the first element matching the selector.</summary>
    protected static async Task<string> StyleAsync(IPage page, string selector, string property) =>
        (await page.EvaluateAsync<string?>(@"
            a => {
                const el = document.querySelector(a.selector);
                if (!el) return null;
                return getComputedStyle(el).getPropertyValue(a.property);
            }", new { selector, property })
         ?? throw new InvalidOperationException($"На страницата няма елемент {selector}."))
        .Trim();

    /// <summary>
    /// The colour a person SEES behind a given element. This is not the same as
    /// the <c>background-color</c> of the element itself: a transparent element
    /// shows its parent's background, and so on upwards to the first opaque one.
    /// <para>
    /// If the search reaches the root without finding an opaque colour, the
    /// drawing on <c>body</c> is returned and <c>From</c> is "body", meaning that
    /// what is seen in that spot is the page's background, whatever it happens to
    /// be.
    /// </para>
    /// </summary>
    protected static async Task<PaintedBackground> PaintedBehindAsync(IPage page, string selector)
    {
        // An array of three strings is returned rather than an object, so that
        // marshalling through Playwright does not depend on the field names.
        var parts = await page.EvaluateAsync<string[]>(@"
            selector => {
                const describe = el => el.tagName.toLowerCase() +
                    (el.id ? '#' + el.id : '') +
                    (el.className && typeof el.className === 'string' && el.className.trim()
                        ? '.' + el.className.trim().split(/\s+/).join('.') : '');

                let el = document.querySelector(selector);
                if (!el) return ['', '', ''];

                for (let node = el; node; node = node.parentElement) {
                    const bg = getComputedStyle(node).backgroundColor;
                    const channels = (bg.match(/rgba?\(([^)]+)\)/) || [null, ''])[1]
                        .split(',').map(v => parseFloat(v));
                    const alpha = channels.length === 4 ? channels[3] : 1;

                    if (channels.length >= 3 && alpha > 0.95)
                        return [bg, describe(node), 'opaque'];
                }

                // Стигнахме корена без плътен цвят: остава рисунката на body.
                return [getComputedStyle(document.body).backgroundImage, 'body', ''];
            }", selector);

        if (parts.Length != 3 || parts[1].Length == 0)
            throw new InvalidOperationException($"На страницата няма елемент {selector}.");

        return new PaintedBackground
        {
            Color  = parts[0],
            From   = parts[1],
            Opaque = parts[2] == "opaque"
        };
    }

    /// <summary>
    /// The background colour at twenty-five points across the visible part of the
    /// page.
    ///
    /// <para>
    /// For each point <c>elementFromPoint</c> is asked and the walk goes upwards
    /// to the first thing that draws. An opaque colour comes back as a colour; an
    /// image, a canvas or a gradient come back as "image", because those are
    /// drawings and are not measured by colour.
    /// </para>
    /// <para>
    /// This is the closest a test can get to "look at the page": it asks what is
    /// under the cursor at a given spot rather than about one chosen element.
    /// </para>
    /// </summary>
    protected static Task<string[]> SampledBackgroundsAsync(IPage page) =>
        page.EvaluateAsync<string[]>(@"
            () => {
                const describe = el => el.tagName.toLowerCase() +
                    (el.id ? '#' + el.id : '') +
                    (el.className && typeof el.className === 'string' && el.className.trim()
                        ? '.' + el.className.trim().split(/\s+/).join('.') : '');

                // '' = прозрачен, продължавай нагоре; 'image' = рисунка;
                // иначе цвят.
                const paintOf = el => {
                    if (['IMG', 'VIDEO', 'CANVAS', 'IFRAME', 'PICTURE'].includes(el.tagName))
                        return 'image';

                    const style = getComputedStyle(el);
                    const channels = (style.backgroundColor.match(/rgba?\(([^)]+)\)/) || [null, ''])[1]
                        .split(',').map(v => parseFloat(v));
                    const alpha = channels.length === 4 ? channels[3] : 1;

                    if (channels.length >= 3 && alpha > 0.95) return style.backgroundColor;
                    if (style.backgroundImage !== 'none') return 'image';
                    return '';
                };

                const seen = [];
                const w = window.innerWidth, h = window.innerHeight;

                for (let col = 1; col <= 5; col++) {
                    for (let row = 1; row <= 5; row++) {
                        const x = Math.round(w * col / 6), y = Math.round(h * row / 6);
                        const el = document.elementFromPoint(x, y);
                        if (!el) continue;

                        let paint = '';
                        for (let node = el; node && !paint; node = node.parentElement)
                            paint = paintOf(node);

                        // Нищо не рисува чак до корена — такава точка показва
                        // фона на страницата, какъвто и да е той.
                        if (!paint) paint = getComputedStyle(document.body).backgroundColor;

                        seen.push(x + ',' + y + '|' + paint + '|' + describe(el));
                    }
                }

                return seen;
            }");

    // ════════════════════════════════════════════════════════════════════
    // Colours
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Turns a hex colour into the rgb(…) form the browser returns.</summary>
    protected static string ToRgb(string hex)
    {
        var (r, g, b, a) = ParseColor(hex);
        return a >= 1
            ? $"rgb({r}, {g}, {b})"
            : $"rgba({r}, {g}, {b}, {a.ToString("0.##", CultureInfo.InvariantCulture)})";
    }

    /// <summary>
    /// Two colours that are in practice the same one, with a tolerance per
    /// channel.
    /// <para>
    /// It is used to recognise the accent together with the colours derived from
    /// it: <c>--accent-hover</c> and <c>--accent-active</c> are the same colour
    /// darkened by 15% and 30%.
    /// </para>
    /// </summary>
    protected static bool IsNear(string first, string second, int tolerance = 45)
    {
        var a = ParseColor(first);
        var b = ParseColor(second);

        return Math.Abs(a.R - b.R) <= tolerance
            && Math.Abs(a.G - b.G) <= tolerance
            && Math.Abs(a.B - b.B) <= tolerance;
    }

    /// <summary>
    /// The WCAG contrast between two colours. The guidance for the themes asks for
    /// 7:1 on the main text and 4.5:1 on the quieter text, so the figure has to be
    /// measured rather than eyeballed.
    /// </summary>
    protected static double Contrast(string first, string second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        var (hi, lo) = a > b ? (a, b) : (b, a);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Luminance(string color)
    {
        var (r, g, b, _) = ParseColor(color);

        static double Channel(int v)
        {
            var c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    /// <summary>Understands #rgb, #rrggbb, rgb(…) and rgba(…) alike.</summary>
    private static (int R, int G, int B, double A) ParseColor(string raw)
    {
        var value = raw.Trim();

        if (value.StartsWith('#'))
        {
            var hex = value[1..];
            if (hex.Length == 3)
                hex = string.Concat(hex.Select(c => new string(c, 2)));

            int Part(int i) => int.Parse(hex.Substring(i, 2), NumberStyles.HexNumber);

            var alpha = hex.Length == 8 ? Part(6) / 255.0 : 1.0;
            return (Part(0), Part(2), Part(4), alpha);
        }

        var match = Regex.Match(value, @"rgba?\(([^)]+)\)");
        if (!match.Success)
            throw new InvalidOperationException($"Не разпознавам цвят „{raw}“.");

        var numbers = match.Groups[1].Value
            .Split(new[] { ',', '/', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => double.Parse(v.Trim(), CultureInfo.InvariantCulture))
            .ToArray();

        return ((int)numbers[0], (int)numbers[1], (int)numbers[2],
                numbers.Length > 3 ? numbers[3] : 1.0);
    }
}

/// <summary>The colour behind an element, and which element it comes from.</summary>
public sealed class PaintedBackground
{
    public string Color { get; set; } = string.Empty;
    public string From  { get; set; } = string.Empty;

    /// <summary>True when some element behind this one paints an opaque colour.</summary>
    public bool Opaque { get; set; }

    public override string ToString() => $"{Color} (от {From})";
}
