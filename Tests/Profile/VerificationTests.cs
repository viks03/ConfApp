// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Profile;

/// <summary>
/// Part 4, submitting for verification with a photograph of a document. A
/// student and a journalist go through the same page with different fields.
/// </summary>
[Collection(AppCollection.Name)]
public class VerificationTests : ProfileTestBase
{
    public VerificationTests(AppFixture app) : base(app, "pf-verif") { }

    private static Dictionary<string, string> StudentForm(
        string university = "UNWE",
        string specialty  = "Finance",
        string year       = "3",
        string studentId  = "1234567") => new()
    {
        ["StudentInput.University"] = university,
        ["StudentInput.Specialty"]  = specialty,
        ["StudentInput.StudyYear"]  = year,
        ["StudentInput.StudentId"]  = studentId
    };

    private static Dictionary<string, string> JournalistForm(
        string outlet   = "Dnevnik",
        string position = "Reporter",
        string? website = null) => new()
    {
        ["JournalistInput.MediaOutlet"]  = outlet,
        ["JournalistInput.Position"]     = position,
        ["JournalistInput.MediaWebsite"] = website ?? string.Empty
    };

    // ════════════════════════════════════════════════════════════════════
    // The happy path
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Студент_подава_снимка_и_данни_и_отива_в_очакване()
    {
        var user = await NewParticipantAsync(partForm: "2");
        var auditFrom = await App.Db.LastAuditIdAsync();

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var response = await session.PostMultipartAsync("/SubmitDocuments",
            StudentForm(),
            new[] { UploadFile.Jpeg("StudentInput.StudentCard") });

        // [T-12] A successful submission ends in the profile rather than on the
        // submission page: once it is saved the status is "Pending" and OnGetAsync
        // forwards. That is why the success message lives there, in the status
        // panel.
        Assert.EndsWith("/Profile", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("status-panel status-pending", await response.ReadPageAsync());

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Pending", after!.VerificationStatus);
        Assert.Equal("UNWE",    after.VerificationInstitution);
        Assert.Equal("Finance", after.VerificationSpecialty);
        Assert.Equal("3",       after.VerificationYear);
        Assert.Equal("1234567", after.VerificationStudentId);
        Assert.NotNull(after.VerificationSubmittedAt);

        Assert.StartsWith("uploads/submitted-documents/students/", after.VerificationDocumentPath);
        Assert.True(File.Exists(PhysicalPath(after.VerificationDocumentPath!)),
            "Снимката на документа не е записана под частния корен.");

        var audit = await App.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "Verification Documents Submitted"
                                    && a.UserEmail == user.Email);
    }

    [Fact]
    public async Task Журналист_подава_и_адресът_се_нормализира()
    {
        var user = await NewParticipantAsync(partForm: "4");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        await session.PostMultipartAsync("/SubmitDocuments",
            JournalistForm(website: "media.bg"),
            new[] { UploadFile.Png("JournalistInput.PressCard") });

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Pending",  after!.VerificationStatus);
        Assert.Equal("Dnevnik",  after.VerificationInstitution);
        Assert.Equal("Reporter", after.VerificationSpecialty);

        // An address without a scheme does not make the form fail; the scheme is
        // filled in.
        Assert.Equal("https://media.bg", after.VerificationYear);

        // The student's field is left empty, which is how the page tells the two
        // types apart.
        Assert.Null(after.VerificationStudentId);
        Assert.StartsWith("uploads/submitted-documents/journalists/", after.VerificationDocumentPath);
    }

    [Fact]
    public async Task Вече_въведен_адрес_със_схема_не_се_удвоява()
    {
        var user = await NewParticipantAsync(partForm: "4");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        await session.PostMultipartAsync("/SubmitDocuments",
            JournalistForm(website: "https://media.bg"),
            new[] { UploadFile.Png("JournalistInput.PressCard") });

        Assert.Equal("https://media.bg",
            (await App.Db.FindUserAsync(user.Email!))!.VerificationYear);
    }

    [Fact]
    public async Task Отхвърленият_подава_наново_и_причината_за_отказ_отпада()
    {
        var user = await NewParticipantAsync(partForm: "2");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        // The first submission.
        await session.PostMultipartAsync("/SubmitDocuments", StudentForm(),
            new[] { UploadFile.Jpeg("StudentInput.StudentCard", "purva.jpg") });

        var first = (await App.Db.FindUserAsync(user.Email!))!.VerificationDocumentPath!;

        // The administrator turns it down; that is part 6, here it is only the
        // state that matters.
        await App.Db.SetVerificationAsync(user.Email!, "Rejected", first, "Снимката е нечетима.");

        // Someone who was turned down sees the form again rather than a redirect.
        Assert.Equal(HttpStatusCode.OK,
            (await session.Client.GetAsync("/SubmitDocuments")).StatusCode);

        await session.PostMultipartAsync("/SubmitDocuments", StudentForm(studentId: "7654321"),
            new[] { UploadFile.Jpeg("StudentInput.StudentCard", "vtora.jpg") });

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Pending", after!.VerificationStatus);
        Assert.Null(after.VerificationRejectionReason);
        Assert.Equal("7654321", after.VerificationStudentId);

        Assert.NotEqual(first, after.VerificationDocumentPath);
        Assert.True(File.Exists(PhysicalPath(after.VerificationDocumentPath!)));
        Assert.False(File.Exists(PhysicalPath(first)),
            "Отхвърлената снимка е останала на диска — лични данни без собственик.");
    }

    [Fact]
    public async Task Второ_подаване_без_нов_файл_обновява_само_данните()
    {
        var user = await NewParticipantAsync(partForm: "2");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        await session.PostMultipartAsync("/SubmitDocuments", StudentForm(),
            new[] { UploadFile.Jpeg("StudentInput.StudentCard") });

        var document = (await App.Db.FindUserAsync(user.Email!))!.VerificationDocumentPath!;
        await App.Db.SetVerificationAsync(user.Email!, "Rejected", document, "Липсва номер.");

        await session.PostMultipartAsync("/SubmitDocuments", StudentForm(studentId: "9999999"), files: null);

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("9999999", after!.VerificationStudentId);
        Assert.Equal(document,  after.VerificationDocumentPath);
        Assert.True(File.Exists(PhysicalPath(document)));
    }

    [Fact]
    public async Task Снимката_на_документа_се_трие_при_смяна_към_форма_без_верификация()
    {
        // [T-13] The profile used to clear only VerificationStatus, while the
        // photograph of a student or press card stayed both on disk and in the
        // row: personal data that no longer proves anything and that nobody
        // looks at.
        var user = await NewParticipantAsync(partForm: "2");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        await session.PostMultipartAsync("/SubmitDocuments", StudentForm(),
            new[] { UploadFile.Jpeg("StudentInput.StudentCard") });

        var submitted = await App.Db.FindUserAsync(user.Email!);
        var document  = submitted!.VerificationDocumentPath!;
        Assert.True(File.Exists(PhysicalPath(document)));

        // The change is allowed only while nothing is locked, that is, in the
        // rejected state.
        await App.Db.SetVerificationAsync(user.Email!, "Rejected", document, "Нечетимо.");

        var form = ProfileForm(submitted, partForm: "1");
        form["__RequestVerificationToken"] = await session.AntiforgeryTokenAsync("/Profile");
        await session.PostFormAsync("/Profile", form);

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("1",    after!.PartForm);
        Assert.Equal("None", after.VerificationStatus);

        // Neither in the row nor on the disk.
        Assert.Null(after.VerificationDocumentPath);
        Assert.Null(after.VerificationInstitution);
        Assert.Null(after.VerificationSpecialty);
        Assert.Null(after.VerificationYear);
        Assert.Null(after.VerificationStudentId);
        Assert.Null(after.VerificationSubmittedAt);
        Assert.Null(after.VerificationRejectionReason);
        Assert.False(File.Exists(PhysicalPath(document)),
            "Снимката на личния документ е останала на диска.");

        // The trace stays in the audit log: the deletion must not be silent.
        var audit = await App.Db.AuditForAsync(user.Email!);
        Assert.Contains(audit, a => a.Action == "Profile Update"
                                    && a.Details != null
                                    && a.Details.Contains("Deleted Verification Document"));
    }

    [Fact]
    public async Task Върналият_се_към_студент_подава_наново()
    {
        // A consequence of [T-13]: once the document is deleted, coming back to a
        // type that needs verification starts from scratch rather than from an old
        // file.
        var user = await NewParticipantAsync(partForm: "2");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        await session.PostMultipartAsync("/SubmitDocuments", StudentForm(),
            new[] { UploadFile.Jpeg("StudentInput.StudentCard") });

        var submitted = await App.Db.FindUserAsync(user.Email!);
        await App.Db.SetVerificationAsync(user.Email!,
            "Rejected", submitted!.VerificationDocumentPath, "Нечетимо.");

        async Task SwitchToAsync(string partForm)
        {
            var current = await App.Db.FindUserAsync(user.Email!);
            var form = ProfileForm(current!, partForm: partForm);
            form["__RequestVerificationToken"] = await session.AntiforgeryTokenAsync("/Profile");
            await session.PostFormAsync("/Profile", form);
        }

        await SwitchToAsync("1");
        await SwitchToAsync("2");

        // The profile prompts for documents again.
        var page = Html.Text(await session.Client.GetStringAsync("/Profile"));
        Assert.Contains("status-panel status-warning", page);

        // And submitting without a file no longer works: there is nothing left to
        // build on.
        var response = await session.PostMultipartAsync("/SubmitDocuments", StudentForm(), files: null);
        Assert.Contains(Resx.Value("Pages.SubmitDocuments", "SD_Err_Student_Doc1Required"),
            await response.ReadPageAsync());
    }

    // ════════════════════════════════════════════════════════════════════
    // The rejections
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Първо_подаване_без_снимка_се_отказва()
    {
        var user = await NewParticipantAsync(partForm: "2");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var response = await session.PostMultipartAsync("/SubmitDocuments", StudentForm(), files: null);

        Assert.Contains(Resx.Value("Pages.SubmitDocuments", "SD_Err_Student_Doc1Required"),
            await response.ReadPageAsync());

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Null(after!.VerificationDocumentPath);
        Assert.NotEqual("Pending", after.VerificationStatus ?? "None");
    }

    [Theory]
    [InlineData("SD_Err_UniversityRequired", "University")]
    [InlineData("SD_Err_SpecialtyRequired",  "Specialty")]
    [InlineData("SD_Err_StudentIdRequired",  "StudentId")]
    public async Task Липсващо_задължително_поле_спира_подаването(string errorKey, string field)
    {
        var user = await NewParticipantAsync(partForm: "2");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var form = StudentForm();
        form["StudentInput." + field] = string.Empty;

        var response = await session.PostMultipartAsync("/SubmitDocuments", form,
            new[] { UploadFile.Jpeg("StudentInput.StudentCard") });

        Assert.Contains(Resx.Value("Pages.SubmitDocuments", errorKey), await response.ReadPageAsync());
        Assert.Null((await App.Db.FindUserAsync(user.Email!))!.VerificationDocumentPath);
    }

    [Fact]
    public async Task Непозната_година_на_обучение_се_отказва()
    {
        var user = await NewParticipantAsync(partForm: "2");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var response = await session.PostMultipartAsync("/SubmitDocuments",
            StudentForm(year: "9"),
            new[] { UploadFile.Jpeg("StudentInput.StudentCard") });

        Assert.Contains(Resx.Value("Pages.SubmitDocuments", "SD_Err_YearInvalid"),
            await response.ReadPageAsync());
        Assert.Null((await App.Db.FindUserAsync(user.Email!))!.VerificationDocumentPath);
    }

    [Theory]
    [InlineData("karta.pdf", "application/pdf")]
    [InlineData("karta.gif", "image/gif")]
    [InlineData("karta.png", "text/html")]
    [InlineData("karta.exe", "image/png")]
    public async Task Документ_който_не_е_снимка_се_отказва(string name, string mime)
    {
        // Both the extension and the declared type are checked: the client sets
        // both, so a mismatch has to fail as well.
        var user = await NewParticipantAsync(partForm: "2");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var response = await session.PostMultipartAsync("/SubmitDocuments", StudentForm(),
            new[] { UploadFile.Png("StudentInput.StudentCard", name) with { ContentType = mime } });

        Assert.Contains(Resx.Value("Pages.SubmitDocuments", "SD_Err_FileType"),
            await response.ReadPageAsync());
        Assert.Null((await App.Db.FindUserAsync(user.Email!))!.VerificationDocumentPath);
    }

    [Fact]
    public async Task Снимка_над_три_мегабайта_се_отказва()
    {
        var user = await NewParticipantAsync(partForm: "2");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var tooBig = UploadFile.Jpeg("StudentInput.StudentCard").OfSize(3 * 1024 * 1024 + 1024);

        var response = await session.PostMultipartAsync("/SubmitDocuments", StudentForm(), new[] { tooBig });

        Assert.Contains(Resx.Prefix("Pages.SubmitDocuments", "SD_Err_FileTooLarge"),
            await response.ReadPageAsync());
        Assert.Null((await App.Db.FindUserAsync(user.Email!))!.VerificationDocumentPath);
    }

    // ════════════════════════════════════════════════════════════════════
    // Who gets this far at all
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Подадени_документи_не_се_презаписват_докато_чакат_проверка()
    {
        var user = await NewParticipantAsync(partForm: "2");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        await session.PostMultipartAsync("/SubmitDocuments", StudentForm(),
            new[] { UploadFile.Jpeg("StudentInput.StudentCard") });

        var submitted = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Pending", submitted!.VerificationStatus);

        // The page has nothing left to offer and forwards to the profile.
        using (var client = session.NoRedirectClient())
        {
            var page = await client.GetAsync("/SubmitDocuments");
            Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
            Assert.Equal("/Profile", page.Headers.Location?.OriginalString);
        }

        // The same for a direct POST; otherwise a forged form would overwrite
        // documents that are already under review.
        await session.PostMultipartAsync("/SubmitDocuments",
            StudentForm(university: "Друг университет", studentId: "0000000"),
            new[] { UploadFile.Jpeg("StudentInput.StudentCard", "podmiana.jpg") },
            tokenFrom: "/Profile");

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal(submitted.VerificationInstitution,  after!.VerificationInstitution);
        Assert.Equal(submitted.VerificationStudentId,    after.VerificationStudentId);
        Assert.Equal(submitted.VerificationDocumentPath, after.VerificationDocumentPath);
    }

    [Fact]
    public async Task Одобрените_документи_също_не_се_презаписват()
    {
        var user = await NewParticipantAsync(partForm: "2");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        await session.PostMultipartAsync("/SubmitDocuments", StudentForm(),
            new[] { UploadFile.Jpeg("StudentInput.StudentCard") });

        var document = (await App.Db.FindUserAsync(user.Email!))!.VerificationDocumentPath!;
        await App.Db.SetVerificationAsync(user.Email!, "Approved", document);

        await session.PostMultipartAsync("/SubmitDocuments", StudentForm(studentId: "0000000"),
            new[] { UploadFile.Jpeg("StudentInput.StudentCard", "podmiana.jpg") },
            tokenFrom: "/Profile");

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Approved", after!.VerificationStatus);
        Assert.Equal(document,   after.VerificationDocumentPath);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("3")]
    public async Task Форма_без_верификация_не_подава_документи(string partForm)
    {
        var user = await NewParticipantAsync(partForm: partForm);

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        // The page opens, but with no submission form on it.
        Assert.Equal(HttpStatusCode.OK,
            (await session.Client.GetAsync("/SubmitDocuments")).StatusCode);

        await session.PostMultipartAsync("/SubmitDocuments", StudentForm(),
            new[] { UploadFile.Jpeg("StudentInput.StudentCard") });

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Null(after!.VerificationDocumentPath);
        Assert.NotEqual("Pending", after.VerificationStatus ?? "None");
    }

    [Fact]
    public async Task Подаване_без_влизане_се_отказва()
    {
        using var client = App.NewClient(followRedirects: false);
        var response = await client.PostAsync("/SubmitDocuments",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
