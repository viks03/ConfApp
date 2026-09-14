// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text.RegularExpressions;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Auth;

/// <summary>The answer to one step of the wizard: both the status code and the page itself.</summary>
internal sealed record PostedPage(HttpResponseMessage Response, string Html)
{
    /// <summary>The page with the Cyrillic decoded; see <see cref="Fixtures.Html"/>.</summary>
    public string Text => Fixtures.Html.Text(Html);

    public bool ShowsPhase(int phase) =>
        Html.Contains($"<span class=\"auth-step\">0{phase} / 03</span>", StringComparison.Ordinal);

    public bool Says(string text) => Text.Contains(text, StringComparison.Ordinal);
}

/// <summary>
/// The data a person fills in across the three phases of <c>/Register</c>. The
/// names are in Latin script, because that is what the model validates for.
/// </summary>
internal sealed class RegistrationForm
{
    public required string Email { get; init; }

    public string FirstName     { get; init; } = "Ivan";
    public string LastName      { get; init; } = "Petrov";
    public string Age           { get; init; } = "34";
    public string AcademicTitle { get; init; } = "Assoc. Prof.";
    public string Phone         { get; init; } = "+359 888 123456";
    public string Workplace     { get; init; } = "UNWE";

    /// <summary>1 a lecturer, 2 a student, 3 online, 4 a journalist.</summary>
    public string PartForm  { get; init; } = "1";

    public bool IsForeigner { get; init; }
    public bool Gdpr        { get; init; } = true;
    public bool Marketing   { get; init; }
    public bool PublishPaper{ get; init; }

    public Dictionary<string, string> Personal() => new()
    {
        ["Input.FirstName"]     = FirstName,
        ["Input.LastName"]      = LastName,
        ["Input.Age"]           = Age,
        ["Input.AcademicTitle"] = AcademicTitle,
        ["Input.Email"]         = Email,
        ["Input.Phone"]         = Phone,
        ["Input.IsForeigner"]   = Bool(IsForeigner)
    };

    public Dictionary<string, string> Participation() => new()
    {
        ["Input.Workplace"] = Workplace,
        ["Input.PartForm"]  = PartForm
    };

    public Dictionary<string, string> Consents() => new()
    {
        ["Input.IsGDPR"]                = Bool(Gdpr),
        ["Input.IsMarketing"]           = Bool(Marketing),
        ["Input.ConsentToPublishPaper"] = Bool(PublishPaper)
    };

    private static string Bool(bool value) => value ? "true" : "false";
}

/// <summary>
/// Walks the three phases of <c>/Register</c> the way a browser does: each phase
/// sends the fields of the earlier ones as hidden inputs, and the token is taken
/// from the page just returned.
/// <para>
/// Uploading a paper goes through the browser (see
/// <see cref="RegistrationTests"/>); the path here carries no file, so that the
/// negative cases can be checked without multipart.
/// </para>
/// </summary>
internal static class RegistrationFlow
{
    /// <summary>Opens /Register and submits phase 1. Returns the page of phase 2.</summary>
    public static async Task<PostedPage> Phase1Async(HttpSession session, RegistrationForm form)
    {
        var token = await session.AntiforgeryTokenAsync("/Register");

        var fields = form.Personal();
        fields["Phase"] = "1";

        return await PostAsync(session, fields, token);
    }

    /// <summary>Submits phase 2 against the page returned by phase 1.</summary>
    public static async Task<PostedPage> Phase2Async(
        HttpSession session, RegistrationForm form, PostedPage phase2Page)
    {
        var fields = form.Personal();
        foreach (var (key, value) in form.Participation()) fields[key] = value;
        fields["Phase"] = "2";

        return await PostAsync(session, fields,
            HttpSession.ExtractAntiforgeryToken(phase2Page.Html, "/Register (фаза 2)"));
    }

    /// <summary>Submits phase 3, which is the registration proper.</summary>
    public static async Task<PostedPage> Phase3Async(
        HttpSession session, RegistrationForm form, PostedPage phase3Page)
    {
        var fields = form.Personal();
        foreach (var (key, value) in form.Participation()) fields[key] = value;
        foreach (var (key, value) in form.Consents())      fields[key] = value;

        fields["Phase"] = "3";

        // The path of the already saved paper travels sealed in a hidden input;
        // the test forwards it untouched, just as the browser does.
        var sealedPath = SavedFilePath(phase3Page.Html);
        if (sealedPath != null) fields["Input.SavedFilePath"] = sealedPath;

        return await PostAsync(session, fields,
            HttpSession.ExtractAntiforgeryToken(phase3Page.Html, "/Register (фаза 3)"));
    }

    /// <summary>All three phases at once, for tests that just need a finished participant.</summary>
    public static async Task<PostedPage> RunAsync(HttpSession session, RegistrationForm form)
    {
        var two   = await Phase1Async(session, form);
        var three = await Phase2Async(session, form, two);
        return await Phase3Async(session, form, three);
    }

    public static string? SavedFilePath(string html)
    {
        var match = Regex.Match(html,
            "name=\"Input\\.SavedFilePath\"[^>]*value=\"(?<v>[^\"]*)\"");
        return match.Success && match.Groups["v"].Value.Length > 0
            ? System.Net.WebUtility.HtmlDecode(match.Groups["v"].Value)
            : null;
    }

    private static async Task<PostedPage> PostAsync(
        HttpSession session, Dictionary<string, string> fields, string token)
    {
        fields["__RequestVerificationToken"] = token;

        var response = await session.PostFormAsync("/Register", fields);
        return new PostedPage(response, await response.Content.ReadAsStringAsync());
    }
}
