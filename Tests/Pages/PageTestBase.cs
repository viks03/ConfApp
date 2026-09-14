// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using ConferenceApp.Tests.Auth;
using ConferenceApp.Tests.Fixtures;
using Microsoft.Playwright;

namespace ConferenceApp.Tests.Pages;

/// <summary>One page open in a browser, together with the console errors it produced.</summary>
public sealed class PageVisit : IAsyncDisposable
{
    public required IBrowserContext Context { get; init; }
    public required IPage           Page    { get; init; }
    public required List<string>    Errors  { get; init; }

    /// <summary>The status of opening the page that was asked for.</summary>
    public int Status { get; init; }

    public async ValueTask DisposeAsync() => await Context.DisposeAsync();
}

/// <summary>A page's HTTP response, ready to be read.</summary>
public sealed record Fetched(HttpResponseMessage Response, string Html)
{
    public System.Net.HttpStatusCode Status => Response.StatusCode;
    public string FinalPath => Response.RequestMessage!.RequestUri!.AbsolutePath;
}

/// <summary>
/// The common ground of part 7. Every page is opened the way a person reaches
/// it: the anonymous ones directly, the profile and the payment page as a
/// signed-in participant, Verification after a code has been asked for, and Done
/// after a real registration.
/// <para>
/// Every test creates the participant it needs and deletes it afterwards.
/// </para>
/// </summary>
[Collection(AppCollection.Name)]
public abstract class PageTestBase : IAsyncLifetime
{
    protected readonly AppFixture App;
    private readonly List<string> _createdUsers = new();
    private readonly string _tag;

    protected PageTestBase(AppFixture app, string tag)
    {
        App  = app;
        _tag = tag;
    }

    public virtual Task InitializeAsync()
    {
        App.Smtp.Clear();
        return Task.CompletedTask;
    }

    public virtual async Task DisposeAsync()
    {
        foreach (var email in _createdUsers)
        {
            await App.Db.DeleteParticipantAsync(email);
            await App.Db.DeleteOtpCodesAsync(email);
        }
    }

    protected string NewEmail() => $"{_tag}-{Guid.NewGuid():N}@example.test";

    protected async Task<ApplicationUser> NewParticipantAsync(string partForm = "1")
    {
        var email = NewEmail();
        _createdUsers.Add(email);
        return await App.Db.CreateParticipantAsync(email, partForm);
    }

    // ════════════════════════════════════════════════════════════════════
    // The language
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The value of the language cookie, the same one <c>LanguageController</c>
    /// writes. It is used when a test wants the page in a given language
    /// straight away; switching from the top bar is a test of its own.
    /// </summary>
    public static string CultureCookie(string culture) => $"c={culture}|uic={culture}";

    public const string CultureCookieName = ".AspNetCore.Culture";

    /// <summary>Sets the language through the top bar's button: a POST to the controller.</summary>
    protected static async Task SetLanguageAsync(HttpSession session, string culture, string returnUrl = "/")
    {
        var response = await session.Client.PostAsync("/Language/SetLanguage",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["culture"]   = culture,
                ["returnUrl"] = returnUrl
            }));

        response.EnsureSuccessStatusCode();
    }

    // ════════════════════════════════════════════════════════════════════
    // Over HTTP
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens the page over HTTP in the given language, going through whatever it
    /// needs to exist at all.
    /// </summary>
    protected async Task<Fetched> FetchAsync(PublicPage target, string culture = "bg")
    {
        using var session = App.NewSession();
        await SetLanguageAsync(session, culture);

        switch (target.Entry)
        {
            case PageEntry.Participant:
            {
                var user = await NewParticipantAsync();
                await session.LoginParticipantAsync(user.Email!);
                break;
            }

            case PageEntry.LoginCodeRequested:
            {
                var user = await NewParticipantAsync();
                var token = await session.AntiforgeryTokenAsync("/Login");

                // Submitting an e-mail on /Login leads exactly to /Verification.
                var asked = await session.PostFormAsync("/Login", new Dictionary<string, string>
                {
                    ["Email"] = user.Email!,
                    ["__RequestVerificationToken"] = token
                });
                asked.EnsureSuccessStatusCode();
                break;
            }

            case PageEntry.JustRegistered:
            {
                // The Done page is seen only once, right after the registration is
                // confirmed: the answer to the confirmation IS the page. A second
                // GET goes to /Profile.
                var email = NewEmail();
                _createdUsers.Add(email);

                await RegistrationFlow.RunAsync(session, new RegistrationForm { Email = email });

                var otp = await App.Db.WaitForOtpAsync(email, "Registration", TimeSpan.FromSeconds(20))
                    ?? throw new InvalidOperationException($"Няма код за регистрация на {email}.");

                var done = await session.PostFormAsync("/Verification", new Dictionary<string, string>
                {
                    ["VerificationCode"] = otp.Code,
                    ["__RequestVerificationToken"] = await session.AntiforgeryTokenAsync("/Verification")
                });

                return new Fetched(done, await done.Content.ReadAsStringAsync());
            }
        }

        var response = await session.Client.GetAsync(target.Path);
        return new Fetched(response, await response.Content.ReadAsStringAsync());
    }

    // ════════════════════════════════════════════════════════════════════
    // In a browser
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Opens the page in a browser at the given language and width and collects
    /// everything the console said while <b>that page</b> loaded: the setup —
    /// signing in, registering — happens before the listener is attached.
    /// </summary>
    protected async Task<PageVisit> OpenAsync(
        PublicPage target, string culture = "bg", int width = 1440, int height = 900)
    {
        var (context, email) = await ContextForAsync(target, culture);

        await context.AddCookiesAsync(new[]
        {
            new Cookie
            {
                Name = CultureCookieName, Value = CultureCookie(culture),
                Domain = "127.0.0.1", Path = "/"
            }
        });

        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);

        var errors = new List<string>();
        page.Console += (_, msg) =>
        {
            if (msg.Type != "error") return;

            // The location goes into the message: "Failed to load resource" without
            // an address does not say which file is missing.
            var at = msg.Location;

            // A message from a FOREIGN frame is not an error of the page: the
            // embedded Google map runs its own CORS check and declares it failed in
            // the shared console. Neither the address nor the code is ours and
            // nothing on the page can change it. The filter works by address rather
            // than by text, so a script of ours with an error stays ours.
            if (!string.IsNullOrEmpty(at) &&
                at.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                !at.StartsWith(App.BaseUrl, StringComparison.OrdinalIgnoreCase))
                return;

            errors.Add(string.IsNullOrEmpty(at) ? msg.Text : $"{msg.Text}  [{at}]");
        };
        page.PageError += (_, error) => errors.Add(error);

        var response = await GoToAsync(page, target, email, errors);

        // Chromium reports the document's own 404 as a console error too. That is
        // the answer the test expects, not a fault in the page.
        if (target.Entry == PageEntry.Missing)
            errors.RemoveAll(e =>
                e.Contains("Failed to load resource", StringComparison.Ordinal) &&
                e.Contains(target.Path, StringComparison.Ordinal));

        return new PageVisit
        {
            Context = context,
            Page    = page,
            Errors  = errors,
            Status  = response?.Status ?? 0
        };
    }

    /// <summary>A context in which the page can be opened at all.</summary>
    private async Task<(IBrowserContext Context, string? Email)> ContextForAsync(
        PublicPage target, string culture)
    {
        var locale = culture == "en" ? "en-US" : "bg-BG";

        switch (target.Entry)
        {
            case PageEntry.Participant:
            {
                var user = await NewParticipantAsync();
                return (await App.NewLoggedInContextAsync(user.Email!, locale), user.Email);
            }

            case PageEntry.LoginCodeRequested:
            {
                var user = await NewParticipantAsync();
                return (await App.NewBrowserContextAsync(locale), user.Email);
            }

            case PageEntry.JustRegistered:
            {
                var context = await App.NewBrowserContextAsync(locale);
                await context.AddCookiesAsync(await JustRegisteredCookiesAsync(culture));
                return (context, null);
            }

            default:
                return (await App.NewBrowserContextAsync(locale), null);
        }
    }

    /// <summary>
    /// The cookies of someone who has just finished registering: signed in, and
    /// carrying the flag the Done page reads once.
    /// <para>
    /// The registration runs in an HTTP session and the last POST does not follow
    /// the redirect; otherwise that session would consume the flag and the
    /// browser would be handed nothing.
    /// </para>
    /// </summary>
    private async Task<IEnumerable<Cookie>> JustRegisteredCookiesAsync(string culture)
    {
        using var session = App.NewSession();
        await SetLanguageAsync(session, culture);

        var email = NewEmail();
        _createdUsers.Add(email);

        await RegistrationFlow.RunAsync(session, new RegistrationForm { Email = email });

        var otp = await App.Db.WaitForOtpAsync(email, "Registration", TimeSpan.FromSeconds(20))
            ?? throw new InvalidOperationException($"Няма код за регистрация на {email}.");

        var token = await session.AntiforgeryTokenAsync("/Verification");

        using var noRedirect = session.NoRedirectClient();
        var confirmed = await noRedirect.PostAsync("/Verification",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["VerificationCode"] = otp.Code,
                ["__RequestVerificationToken"] = token
            }));

        if (confirmed.StatusCode != System.Net.HttpStatusCode.Redirect ||
            confirmed.Headers.Location?.ToString().Contains("/Done") != true)
            throw new InvalidOperationException(
                $"Потвърждаването на {email} не завърши на /Done, а на " +
                $"{(int)confirmed.StatusCode} {confirmed.Headers.Location}.");

        return session.PlaywrightCookies();
    }

    /// <summary>
    /// The opening itself. The path to Verification goes through /Login, so
    /// whatever the console said up to that point is discarded: the test asks
    /// about the target page, not the one before it.
    /// </summary>
    private static async Task<IResponse?> GoToAsync(
        IPage page, PublicPage target, string? email, List<string> errors)
    {
        if (target.Entry != PageEntry.LoginCodeRequested)
            return await page.GotoAsync(target.Path);

        await page.GotoAsync("/Login");
        await page.AcceptCookieNoticeAsync();
        await page.FillAsync("#Email", email!);

        errors.Clear();

        var response = await page.RunAndWaitForResponseAsync(
            async () => await page.ClickAsync("button.auth-submit"),
            r => r.Request.IsNavigationRequest &&
                 r.Url.Contains("/Verification", StringComparison.OrdinalIgnoreCase));

        await page.WaitForURLAsync("**/Verification");
        return response;
    }
}
