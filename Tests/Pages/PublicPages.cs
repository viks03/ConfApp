// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Tests.Pages;

/// <summary>How a page is reached: what has to be true for it to open at all.</summary>
public enum PageEntry
{
    /// <summary>Opens to anyone off the street.</summary>
    Anonymous,

    /// <summary>Needs a signed-in participant.</summary>
    Participant,

    /// <summary>Needs a code to have been asked for, that is, an e-mail submitted on <c>/Login</c>.</summary>
    LoginCodeRequested,

    /// <summary>Needs a registration just completed (the <c>JustRegistered</c> flag).</summary>
    JustRegistered,

    /// <summary>An address that does not exist; the page is the 404 itself.</summary>
    Missing
}

/// <summary>One public page: what it is called, where it lives and how it is reached.</summary>
public sealed record PublicPage(string Title, string Path, PageEntry Entry)
{
    /// <summary>The address it is recognised by once the redirects are done.</summary>
    public string ExpectedPath { get; init; } = string.Empty;

    public string Expect => ExpectedPath.Length > 0 ? ExpectedPath : Path;

    public override string ToString() => $"{Title} — {Path}";
}

/// <summary>
/// The catalogue of part 7. The order is the order in the brief; at the end come
/// the three the brief does not name but that are public pages of the site all
/// the same.
/// <para>
/// The tests take the <b>address</b> as a parameter rather than the record
/// itself, so that the name of a failing case says which page failed and can be
/// handed straight to <c>--filter</c>.
/// </para>
/// </summary>
public static class PublicPages
{
    public static readonly IReadOnlyList<PublicPage> All = new[]
    {
        new PublicPage("Начало",           "/",                   PageEntry.Anonymous) { ExpectedPath = "/" },
        new PublicPage("За конференцията", "/Conference",         PageEntry.Anonymous),
        new PublicPage("За ICBI",          "/ICBI",               PageEntry.Anonymous),
        new PublicPage("Лектори",          "/Lecturers",          PageEntry.Anonymous),
        new PublicPage("Програма",         "/Schedule",           PageEntry.Anonymous),
        new PublicPage("Участие",          "/Attend",             PageEntry.Anonymous),
        new PublicPage("Въпроси",          "/FAQ",                PageEntry.Anonymous),
        new PublicPage("Пътуване",         "/Travel",             PageEntry.Anonymous),
        new PublicPage("Условия",          "/Terms",              PageEntry.Anonymous),
        new PublicPage("Поверителност",    "/Privacy",            PageEntry.Anonymous),
        new PublicPage("Вход",             "/Login",              PageEntry.Anonymous),
        new PublicPage("Регистрация",      "/Register",           PageEntry.Anonymous),
        new PublicPage("Потвърждаване",    "/Verification",       PageEntry.LoginCodeRequested),
        new PublicPage("Готово",           "/Done",               PageEntry.JustRegistered),
        new PublicPage("Плащане",          "/Payment/earlybird",  PageEntry.Participant),
        new PublicPage("Профил",           "/Profile",            PageEntry.Participant),
        new PublicPage("404",              "/no-such-page-here",  PageEntry.Missing),

        // Not named in the brief, but public all the same:
        new PublicPage("Бисквитки",        "/Cookies",            PageEntry.Anonymous),
        new PublicPage("Документи",        "/SubmitDocuments",    PageEntry.Participant),
        new PublicPage("Отказан достъп",   "/AccessDenied",       PageEntry.Anonymous)
    };

    public static PublicPage Of(string path) =>
        All.FirstOrDefault(p => p.Path == path)
        ?? throw new InvalidOperationException($"В каталога на част 7 няма страница {path}.");

    /// <summary>Every page once.</summary>
    public static IEnumerable<object[]> Each() =>
        All.Select(p => new object[] { p.Path });

    /// <summary>Every page in both languages.</summary>
    public static IEnumerable<object[]> EachInBothLanguages() =>
        All.SelectMany(p => new[] { "bg", "en" }.Select(c => new object[] { p.Path, c }));

    /// <summary>Every page at the three widths the brief asks about.</summary>
    public static IEnumerable<object[]> EachAtEveryWidth() =>
        All.SelectMany(p => new[] { 1440, 768, 375 }.Select(w => new object[] { p.Path, w }));
}
