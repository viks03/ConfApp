// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Files;

/// <summary>
/// Part 5, the second way in for a paper: phase 2 of registration. There the
/// file is written to disk BEFORE an account exists — otherwise it could not
/// travel between the phases — so the rules on extension and size are the only
/// thing standing between an anonymous request and a write into the private
/// root.
/// </summary>
[Collection(AppCollection.Name)]
public class RegistrationUploadTests : FileTestBase
{
    public RegistrationUploadTests(AppFixture app) : base(app, "fl-reg") { }

    /// <summary>The fields of phases 1 and 2 together, as the browser sends them in phase 2.</summary>
    private static Dictionary<string, string> PhaseTwoFields(string email) => new()
    {
        ["Phase"]               = "2",
        ["Input.FirstName"]     = "Ivan",
        ["Input.LastName"]      = "Petrov",
        ["Input.Age"]           = "34",
        ["Input.AcademicTitle"] = "Assoc. Prof.",
        ["Input.Email"]         = email,
        ["Input.Phone"]         = "+359 888 123456",
        ["Input.IsForeigner"]   = "false",
        ["Input.Workplace"]     = "UNWE",
        ["Input.PartForm"]      = "1"
    };

    private static UploadFile Paper(string name, string mime = "application/pdf") =>
        UploadFile.Pdf("Input.UploadedFile", name) with { ContentType = mime };

    /// <summary>The files currently under <c>uploads/papers26</c>.</summary>
    private static string[] PapersOnDisk()
    {
        var folder = Path.Combine(AppFixture.PrivateUploadsRoot, "uploads", "papers26");
        return Directory.Exists(folder)
            ? Directory.GetFiles(folder).Select(f => Path.GetFileName(f)).ToArray()
            : Array.Empty<string>();
    }

    // ════════════════════════════════════════════════════════════════════
    // What is accepted
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("doklad.pdf",  "application/pdf")]
    [InlineData("doklad.doc",  "application/msword")]
    [InlineData("doklad.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public async Task Позволените_разширения_минават_фаза_2_и_лягат_под_частния_корен(
        string name, string mime)
    {
        var email = $"fl-reg-{Guid.NewGuid():N}@example.test";
        using var session = App.NewSession();

        var before = PapersOnDisk();

        var response = await session.PostMultipartAsync("/Register",
            PhaseTwoFields(email), new[] { Paper(name, mime) });

        Assert.Empty(await response.ValidationErrorsAsync());
        Assert.Contains("<span class=\"auth-step\">03 / 03</span>",
            await response.Content.ReadAsStringAsync());

        // The file is written out at once; from there on only its sealed path
        // travels between the phases.
        var added = PapersOnDisk().Except(before).ToArray();
        Assert.Single(added);
        TrackFile("uploads/papers26/" + added[0]);

        Assert.EndsWith(Path.GetExtension(name), added[0]);
        Assert.False(File.Exists(Path.Combine(TestPaths.RepoRoot, "wwwroot", "uploads", "papers26", added[0])),
            "Докладът е записан в wwwroot — раздава се анонимно.");

        // And it is not at the old address either.
        using var anonymous = App.NewClient(followRedirects: false);
        Assert.Equal(HttpStatusCode.NotFound,
            (await anonymous.GetAsync($"/uploads/papers26/{added[0]}")).StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════
    // What is rejected
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("doklad.exe",  "application/octet-stream")]
    [InlineData("doklad.html", "text/html")]
    [InlineData("doklad.png",  "image/png")]
    [InlineData("doklad.pdf.exe", "application/pdf")]
    public async Task Непозволено_разширение_не_минава_и_не_оставя_файл(string name, string mime)
    {
        var email = $"fl-reg-{Guid.NewGuid():N}@example.test";
        using var session = App.NewSession();

        var before = PapersOnDisk();

        var response = await session.PostMultipartAsync("/Register",
            PhaseTwoFields(email), new[] { Paper(name, mime) });

        Assert.Contains(Resx.Value("Pages.Register", "Error_InvalidFileType"),
            await response.ValidationErrorsAsync());

        Assert.Empty(PapersOnDisk().Except(before));
    }

    [Fact]
    public async Task Файл_над_лимита_не_минава_и_не_оставя_файл()
    {
        var email = $"fl-reg-{Guid.NewGuid():N}@example.test";
        using var session = App.NewSession();

        var before = PapersOnDisk();

        // 10 MB: the same ceiling as in the profile, as the hint under the field,
        // and as validation.js ([T-16]).
        var tooBig = Paper("doklad.pdf") with { Content = new byte[10 * 1024 * 1024 + 1024] };

        var response = await session.PostMultipartAsync("/Register",
            PhaseTwoFields(email), new[] { tooBig });

        Assert.Contains(await response.ValidationErrorsAsync(),
            error => error.StartsWith(Resx.Prefix("Pages.Register", "Error_FileTooLarge")));

        Assert.Empty(PapersOnDisk().Except(before));
    }

    /// <summary>
    /// [T-15] The message has a <c>{0}</c> placeholder for the size. Only
    /// <c>validation.js</c> used to fill it in; the server rendered the string as
    /// it stood, so with scripting off — or on a request that did not come from a
    /// browser — the literal <c>{0}</c> appeared on screen.
    /// </summary>
    [Fact]
    public async Task Съобщението_за_голям_файл_показва_размера_а_не_заместителя()
    {
        var email = $"fl-reg-{Guid.NewGuid():N}@example.test";
        using var session = App.NewSession();

        var tooBig = Paper("doklad.pdf") with { Content = new byte[12 * 1024 * 1024] };

        var response = await session.PostMultipartAsync("/Register",
            PhaseTwoFields(email), new[] { tooBig });

        Assert.Contains(Resx.Format("Pages.Register", "Error_FileTooLarge", "12.0"),
            await response.ValidationErrorsAsync());
    }

    [Fact]
    public async Task Съобщението_за_голям_файл_в_профила_също_показва_размера()
    {
        var user = await NewParticipantAsync();
        using var session = await SignedInAsync(user);

        var tooBig = UploadFile.Pdf("Input.UploadedFile").OfSize(12 * 1024 * 1024);

        var response = await session.PostMultipartAsync("/Profile",
            ParticipantForms.Profile(user), new[] { tooBig });

        Assert.Contains(Resx.Format("Pages.Profile", "Error_FileTooLarge", "12.0"),
            await response.ValidationErrorsAsync());
    }

    /// <summary>
    /// [T-16] The two ways in for a paper now cut off at the same point. Until
    /// now a paper between 10 and 25 MB passed at registration and could then
    /// never be replaced from the profile.
    /// </summary>
    [Fact]
    public async Task Двата_входа_за_доклад_имат_един_и_същ_лимит()
    {
        var between = new byte[12 * 1024 * 1024];

        var email = $"fl-reg-{Guid.NewGuid():N}@example.test";
        using var registration = App.NewSession();

        var atRegister = await registration.PostMultipartAsync("/Register",
            PhaseTwoFields(email), new[] { Paper("doklad.pdf") with { Content = between } });

        var user = await NewParticipantAsync();
        using var profile = await SignedInAsync(user);

        var atProfile = await profile.PostMultipartAsync("/Profile",
            ParticipantForms.Profile(user),
            new[] { UploadFile.Pdf("Input.UploadedFile") with { Content = between } });

        Assert.Contains(await atRegister.ValidationErrorsAsync(),
            e => e.StartsWith(Resx.Prefix("Pages.Register", "Error_FileTooLarge")));
        Assert.Contains(await atProfile.ValidationErrorsAsync(),
            e => e.StartsWith(Resx.Prefix("Pages.Profile", "Error_FileTooLarge")));

        Assert.Null((await App.Db.FindUserAsync(user.Email!))!.PaperFilePath);
    }

    // ════════════════════════════════════════════════════════════════════
    // The path between the phases
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The path to the already saved paper travels through a hidden input, that
    /// is, through the client. A doctored value must not be accepted ([A-01]).
    /// </summary>
    [Theory]
    [InlineData("uploads/papers26/chuzhd.pdf")]
    [InlineData("../../conferenceapp.db")]
    [InlineData("CfDJ8-podpravena-stoynost")]
    public async Task Подправен_път_между_фазите_се_пренебрегва(string forged)
    {
        var email = $"fl-reg-{Guid.NewGuid():N}@example.test";
        using var session = App.NewSession();

        var fields = PhaseTwoFields(email);
        fields["Phase"]                         = "3";
        fields["Input.IsGDPR"]                  = "true";
        fields["Input.IsMarketing"]             = "false";
        fields["Input.ConsentToPublishPaper"]   = "false";
        fields["Input.SavedFilePath"]           = forged;

        var token = await session.AntiforgeryTokenAsync("/Register");
        fields["__RequestVerificationToken"] = token;

        var response = await session.PostFormAsync("/Register", fields);
        response.EnsureSuccessStatusCode();

        // The registration goes through, but with no paper rather than someone
        // else's.
        var user = await App.Db.FindUserAsync(email);
        Assert.NotNull(user);

        try
        {
            Assert.Null(user!.PaperFilePath);
        }
        finally
        {
            await App.Db.DeleteParticipantAsync(email);
            await App.Db.DeleteOtpCodesAsync(email);
        }
    }
}
