// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ConferenceApp.Tests.Fixtures;

/// <summary>
/// An HTTP session with cookies of its own, and therefore one user. It is used
/// for the paths that need no browser: webhooks, the API, access rights.
/// <para>
/// Signing in goes the same way as it does for a person: a request for a code
/// through <c>/Login</c>, the code read from <c>OtpCodes</c>, and entering it on
/// <c>/Verification</c>.
/// </para>
/// </summary>
public sealed class HttpSession : IDisposable
{
    private readonly TestDb _db;
    private readonly CookieContainer _cookies = new();

    public HttpClient Client { get; }
    public string BaseUrl { get; }

    /// <summary>
    /// The address this session presents itself with. The rate limit is per IP
    /// (300 requests a minute overall, 60 for a webhook) and every test comes
    /// from loopback, so without an address of its own the suite throttles
    /// itself halfway through.
    /// <para>
    /// The header is honoured because <c>ForwardedHeaders:KnownProxies</c>
    /// contains 127.0.0.1, exactly as it would behind a real proxy. A request
    /// from an untrusted neighbour still cannot choose its own address; that is
    /// a test of its own.
    /// </para>
    /// </summary>
    public string ClientIp { get; }

    private static int _seq;

    public HttpSession(string baseUrl, TestDb db)
    {
        BaseUrl = baseUrl;
        _db = db;
        ClientIp = NextClientIp();

        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            AllowAutoRedirect = true,
            UseCookies = true
        };

        Client = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(60)
        };
        Client.DefaultRequestHeaders.Add("X-Forwarded-For", ClientIp);
    }

    /// <summary>The next address from 10.0.0.0/8: it does not exist, but it is valid and unique.</summary>
    public static string NextClientIp()
    {
        var n = Interlocked.Increment(ref _seq);
        return $"10.{(n >> 16) & 0xFF}.{(n >> 8) & 0xFF}.{(n & 0xFF) + 1}";
    }

    /// <summary>A client that does NOT follow redirects, for asserting on the redirect itself.</summary>
    public HttpClient NoRedirectClient()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            AllowAutoRedirect = false,
            UseCookies = true
        };

        var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ClientIp);
        return client;
    }

    // ════════════════════════════════════════════════════════════════════
    // Signing in
    // ════════════════════════════════════════════════════════════════════

    /// <summary>A participant: a one-time code taken from the database, no password.</summary>
    public async Task LoginParticipantAsync(string email)
    {
        var token = await AntiforgeryTokenAsync("/Login");

        var request = await PostFormAsync("/Login", new Dictionary<string, string>
        {
            ["Email"] = email,
            ["__RequestVerificationToken"] = token
        });
        request.EnsureSuccessStatusCode();

        var otp = await _db.WaitForOtpAsync(email, "Login", TimeSpan.FromSeconds(15))
            ?? throw new InvalidOperationException(
                $"В OtpCodes не се появи код за влизане на {email}. " +
                "Приложението не е поискало код или не го е записало.");

        var verifyToken = await AntiforgeryTokenAsync("/Verification");

        var verify = await PostFormAsync("/Verification", new Dictionary<string, string>
        {
            ["VerificationCode"] = otp.Code,
            ["__RequestVerificationToken"] = verifyToken
        });
        verify.EnsureSuccessStatusCode();

        if (!await IsSignedInAsync())
            throw new InvalidOperationException(
                $"Влизането с код за {email} не подейства — /Profile още пренасочва към входа.");
    }

    /// <summary>An administrator: an ordinary sign-in with a password.</summary>
    public async Task LoginAdminAsync(string email, string password)
    {
        // The first request, with the e-mail alone, makes the page ask for a password.
        var token = await AntiforgeryTokenAsync("/Login");
        await PostFormAsync("/Login", new Dictionary<string, string>
        {
            ["Email"] = email,
            ["__RequestVerificationToken"] = token
        });

        var token2 = await AntiforgeryTokenAsync("/Login");
        var response = await PostFormAsync("/Login", new Dictionary<string, string>
        {
            ["Email"]    = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token2
        });
        response.EnsureSuccessStatusCode();

        if (!await IsSignedInAsync("/Admin"))
            throw new InvalidOperationException(
                "Администраторът не влезе. Провери admin.password в test-credentials.local.json " +
                "срещу AdminSettings:SystemAdminPassword.");
    }

    /// <summary>
    /// Whether the session is signed in, judged by a page that requires it. For
    /// an administrator ask /Admin, since /Profile is the participant's page.
    /// </summary>
    public async Task<bool> IsSignedInAsync(string protectedPath = "/Profile")
    {
        using var client = NoRedirectClient();
        var response = await client.GetAsync(protectedPath);
        return response.StatusCode == HttpStatusCode.OK;
    }

    /// <summary>
    /// A client holding a copy of the cookies as they are right now. The session
    /// may change afterwards; this copy does not. It answers the question of
    /// whether a cookie taken before signing out still works.
    /// </summary>
    public HttpClient SnapshotClient()
    {
        var jar = new CookieContainer();
        foreach (System.Net.Cookie cookie in _cookies.GetCookies(new Uri(BaseUrl)))
            jar.Add(new Uri(BaseUrl), new System.Net.Cookie(cookie.Name, cookie.Value, cookie.Path));

        var client = new HttpClient(new HttpClientHandler
        {
            CookieContainer = jar,
            AllowAutoRedirect = false,
            UseCookies = true
        })
        {
            BaseAddress = new Uri(BaseUrl)
        };

        client.DefaultRequestHeaders.Add("X-Forwarded-For", ClientIp);
        return client;
    }

    /// <summary>
    /// The session's cookies in the shape Playwright understands, so that every
    /// browser test does not have to sign in again.
    /// </summary>
    public IEnumerable<Microsoft.Playwright.Cookie> PlaywrightCookies()
    {
        foreach (System.Net.Cookie cookie in _cookies.GetCookies(new Uri(BaseUrl)))
        {
            yield return new Microsoft.Playwright.Cookie
            {
                Name     = cookie.Name,
                Value    = cookie.Value,
                Domain   = "127.0.0.1",
                Path     = string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path,
                HttpOnly = cookie.HttpOnly,
                Secure   = false,
                SameSite = Microsoft.Playwright.SameSiteAttribute.Lax
            };
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // The language
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Changes the language the way the button in the top bar does: a POST to
    /// <c>LanguageController</c>, which leaves the <c>.AspNetCore.Culture</c>
    /// cookie in the session's jar.
    /// <para>
    /// Part 8 uses this: the language of a message is read from the culture of
    /// the request, so the test has to change the language the same way a person
    /// does rather than slipping a cookie in from the side.
    /// </para>
    /// </summary>
    public async Task SetLanguageAsync(string culture, string returnUrl = "/")
    {
        var response = await Client.PostAsync("/Language/SetLanguage",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["culture"]   = culture,
                ["returnUrl"] = returnUrl
            }));

        response.EnsureSuccessStatusCode();
    }

    // ════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════

    /// <summary>The token from the page's hidden field, the way the browser sends it.</summary>
    public async Task<string> AntiforgeryTokenAsync(string path)
    {
        var html = await Client.GetStringAsync(path);
        return ExtractAntiforgeryToken(html, path);
    }

    public static string ExtractAntiforgeryToken(string html, string forPath)
    {
        var match = Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"");

        if (!match.Success)
            match = Regex.Match(html,
                "value=\"(?<token>[^\"]+)\"[^>]*name=\"__RequestVerificationToken\"");

        if (!match.Success)
            throw new InvalidOperationException(
                $"На {forPath} няма поле __RequestVerificationToken — не мога да подам форма.");

        return match.Groups["token"].Value;
    }

    public Task<HttpResponseMessage> PostFormAsync(string path, Dictionary<string, string> fields) =>
        Client.PostAsync(path, new FormUrlEncodedContent(fields));

    /// <summary>
    /// A POST to a Razor Page handler (<c>?handler=Name</c>) with a token taken
    /// from the page.
    /// </summary>
    /// <param name="tokenFrom">
    /// Where to take the token from when the page itself shows no form: once a
    /// payment is confirmed, for instance, /Payment shows a message rather than
    /// a form ([T-02]). The token belongs to the session rather than to the
    /// page, so any page with a form will do.
    /// </param>
    public async Task<HttpResponseMessage> PostHandlerAsync(
        string page, string handler, Dictionary<string, string>? fields = null,
        string? tokenFrom = null)
    {
        var token = await AntiforgeryTokenAsync(tokenFrom ?? page);
        var body = fields ?? new Dictionary<string, string>();
        body["__RequestVerificationToken"] = token;

        var separator = page.Contains('?') ? "&" : "?";
        return await Client.PostAsync($"{page}{separator}handler={handler}", new FormUrlEncodedContent(body));
    }

    /// <summary>
    /// A form with a file: <c>multipart/form-data</c>, as the browser sends it.
    /// The token is taken from the page itself.
    /// </summary>
    public async Task<HttpResponseMessage> PostMultipartAsync(
        string path,
        Dictionary<string, string> fields,
        IEnumerable<UploadFile>? files = null,
        string? tokenFrom = null)
    {
        var content = new MultipartFormDataContent();

        foreach (var (key, value) in fields)
            content.Add(new StringContent(value, Encoding.UTF8), key);

        content.Add(new StringContent(await AntiforgeryTokenAsync(tokenFrom ?? path), Encoding.UTF8),
            "__RequestVerificationToken");

        foreach (var file in files ?? Enumerable.Empty<UploadFile>())
        {
            var part = new ByteArrayContent(file.Content);
            part.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
            content.Add(part, file.Field, file.FileName);
        }

        return await Client.PostAsync(path, content);
    }

    public Task<HttpResponseMessage> PostJsonAsync(string path, string json) =>
        Client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));

    public void Dispose() => Client.Dispose();
}
