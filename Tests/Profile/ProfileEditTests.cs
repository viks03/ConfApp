// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Profile;

/// <summary>Part 4, editing the details in the profile.</summary>
[Collection(AppCollection.Name)]
public class ProfileEditTests : ProfileTestBase
{
    public ProfileEditTests(AppFixture app) : base(app, "pf-edit") { }

    [Fact]
    public async Task Редактирането_записва_промените_и_оставя_следа_в_одита()
    {
        var user = await NewParticipantAsync();
        var auditFrom = await App.Db.LastAuditIdAsync();

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var response = await session.PostFormAsync("/Profile", Merge(
            ProfileForm(user,
                firstName:     "Ivana",
                lastName:      "Petrova",
                age:           "41",
                academicTitle: "Prof.",
                phone:         "+359 888 999000",
                workplace:     "Sofia University"),
            await session.AntiforgeryTokenAsync("/Profile")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(Resx.Value("Pages.Profile", "Msg_UpdateSuccess"), await response.ReadPageAsync());

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Ivana",            after!.FirstName);
        Assert.Equal("Petrova",          after.LastName);
        Assert.Equal(41,                 after.Age);
        Assert.Equal("Prof.",            after.AcademicTitle);
        Assert.Equal("+359 888 999000",  after.PhoneNumber);
        Assert.Equal("Sofia University", after.Workplace);

        // The audit log keeps what changed into what, not only that something was
        // touched.
        var audit = await App.Db.AuditSinceAsync(auditFrom);
        var entry = Assert.Single(audit, a => a.Action == "Profile Update" && a.UserEmail == user.Email);
        Assert.Contains("Test", entry.Details);
        Assert.Contains("Ivana", entry.Details);
    }

    [Fact]
    public async Task Записване_без_промяна_не_ражда_запис_в_одита()
    {
        var user = await NewParticipantAsync();
        var auditFrom = await App.Db.LastAuditIdAsync();

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        await session.PostFormAsync("/Profile", Merge(
            ProfileForm(user), await session.AntiforgeryTokenAsync("/Profile")));

        var audit = await App.Db.AuditSinceAsync(auditFrom);
        Assert.DoesNotContain(audit, a => a.Action == "Profile Update");
    }

    [Fact]
    public async Task Съгласието_за_новини_се_мени_в_двете_посоки()
    {
        var user = await NewParticipantAsync();

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        await session.PostFormAsync("/Profile", Merge(
            ProfileForm(user, wantsMarketing: true),
            await session.AntiforgeryTokenAsync("/Profile")));
        Assert.True((await App.Db.FindUserAsync(user.Email!))!.WantsMarketing);

        await session.PostFormAsync("/Profile", Merge(
            ProfileForm(user, wantsMarketing: false),
            await session.AntiforgeryTokenAsync("/Profile")));
        Assert.False((await App.Db.FindUserAsync(user.Email!))!.WantsMarketing);
    }

    [Theory]
    [InlineData("Age",       "0",   "Error_AgeRange")]
    [InlineData("Age",       "17",  "Error_AgeRange")]
    [InlineData("Age",       "101", "Error_AgeRange")]
    [InlineData("FirstName", "",    "Error_FirstNameRequired")]
    [InlineData("FirstName", "Иван", "Error_FirstNameLatin")]
    [InlineData("Phone",     "12",  "Error_PhoneFormat")]
    [InlineData("Workplace", "U",   "Error_WorkplaceLength")]
    [InlineData("PartForm",  "9",   "Error_InvalidPartForm")]
    public async Task Невалидна_стойност_не_се_записва(string field, string value, string errorKey)
    {
        var user = await NewParticipantAsync();

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var form = field switch
        {
            "Age"       => ProfileForm(user, age: value),
            "FirstName" => ProfileForm(user, firstName: value),
            "Phone"     => ProfileForm(user, phone: value),
            "Workplace" => ProfileForm(user, workplace: value),
            _           => ProfileForm(user, partForm: value)
        };

        var response = await session.PostFormAsync("/Profile",
            Merge(form, await session.AntiforgeryTokenAsync("/Profile")));

        Assert.Contains(Resx.Value("Pages.Profile", errorKey), await response.ReadPageAsync());

        // The row is untouched: not a single field got through.
        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal(user.FirstName,   after!.FirstName);
        Assert.Equal(user.Age,         after.Age);
        Assert.Equal(user.PhoneNumber, after.PhoneNumber);
        Assert.Equal(user.Workplace,   after.Workplace);
        Assert.Equal(user.PartForm,    after.PartForm);
    }

    [Fact]
    public async Task Формата_не_приема_полета_които_не_са_нейни()
    {
        // The reference number, the e-mail, the payment status and the amount paid
        // are not in InputModel. The request here submits them the way a forged
        // form would.
        var user = await NewParticipantAsync();

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var form = ProfileForm(user);
        form["Input.Email"]           = "someone-else@example.test";
        form["Email"]                 = "someone-else@example.test";
        form["Input.ReferenceNumber"] = "BCE2026-00000ZZZ";
        form["ReferenceNumber"]       = "BCE2026-00000ZZZ";
        form["Input.PaymentStatus"]   = "Confirmed";
        form["PaymentStatus"]         = "Confirmed";
        form["Input.PaidAmountEUR"]   = "0";
        form["PaidAmountEUR"]         = "0";
        form["Input.VerificationStatus"] = "Approved";

        await session.PostFormAsync("/Profile",
            Merge(form, await session.AntiforgeryTokenAsync("/Profile")));

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal(user.Email,              after!.Email);
        Assert.Equal(user.ReferenceNumber,    after.ReferenceNumber);
        Assert.Equal("Pending",               after.PaymentStatus);
        Assert.Null(after.PaidAmountEUR);
        Assert.NotEqual("Approved",           after.VerificationStatus ?? "None");
    }

    [Fact]
    public async Task Администраторът_не_отваря_профила_на_участник()
    {
        using var admin = App.NewSession();
        await admin.LoginAdminAsync(App.Credentials.AdminEmail, App.Credentials.AdminPassword);

        using var client = admin.NoRedirectClient();
        var response = await client.GetAsync("/Profile");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Admin", response.Headers.Location?.OriginalString);
    }

    // ════════════════════════════════════════════════════════════════════
    // The locked fields
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Платил_участник_не_може_да_смени_формата_на_участие()
    {
        var user = await NewParticipantAsync(partForm: "1", paymentStatus: "Confirmed");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        // The page shows the type locked, with no radio buttons.
        var page = await session.Client.GetStringAsync("/Profile");
        Assert.Contains("pf-choice is-locked", page);

        await session.PostFormAsync("/Profile", Merge(
            ProfileForm(user, partForm: "2", firstName: "Ivana"),
            await session.AntiforgeryTokenAsync("/Profile")));

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("1", after!.PartForm);

        // Only the type is locked; the rest is saved.
        Assert.Equal("Ivana", after.FirstName);
    }

    [Fact]
    public async Task Подал_документи_студент_не_може_да_смени_формата_на_участие()
    {
        var user = await NewParticipantAsync(partForm: "2");
        await App.Db.SetVerificationAsync(user.Email!, "Pending",
            documentPath: "uploads/submitted-documents/students/probe.png");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        await session.PostFormAsync("/Profile", Merge(
            ProfileForm(user, partForm: "1"),
            await session.AntiforgeryTokenAsync("/Profile")));

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("2", after!.PartForm);
        Assert.Equal("Pending", after.VerificationStatus);
    }

    [Fact]
    public async Task Одобрен_студент_не_може_да_смени_формата_на_участие()
    {
        var user = await NewParticipantAsync(partForm: "2");
        await App.Db.SetVerificationAsync(user.Email!, "Approved",
            documentPath: "uploads/submitted-documents/students/probe.png");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        await session.PostFormAsync("/Profile", Merge(
            ProfileForm(user, partForm: "3"),
            await session.AntiforgeryTokenAsync("/Profile")));

        Assert.Equal("2", (await App.Db.FindUserAsync(user.Email!))!.PartForm);
    }

    [Fact]
    public async Task Отхвърлен_студент_може_да_смени_формата_на_участие()
    {
        // "Rejected" deliberately does NOT lock: otherwise someone who was turned
        // down is stuck in a type they cannot prove.
        var user = await NewParticipantAsync(partForm: "2");
        await App.Db.SetVerificationAsync(user.Email!, "Rejected",
            documentPath: "uploads/submitted-documents/students/probe.png",
            rejectionReason: "Документът е нечетим.");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        await session.PostFormAsync("/Profile", Merge(
            ProfileForm(user, partForm: "1"),
            await session.AntiforgeryTokenAsync("/Profile")));

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("1", after!.PartForm);

        // Moving to a type that needs no verification clears the status; otherwise
        // the profile would keep showing a rejection for something that no longer
        // applies.
        Assert.Equal("None", after.VerificationStatus);
    }

    [Fact]
    public async Task Неплатил_участник_може_да_смени_формата_на_участие()
    {
        var user = await NewParticipantAsync(partForm: "1");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var page = await session.Client.GetStringAsync("/Profile");
        Assert.DoesNotContain("pf-choice is-locked", page);

        await session.PostFormAsync("/Profile", Merge(
            ProfileForm(user, partForm: "2"),
            await session.AntiforgeryTokenAsync("/Profile")));

        Assert.Equal("2", (await App.Db.FindUserAsync(user.Email!))!.PartForm);
    }

    [Fact]
    public async Task Смяна_на_парола_отвътре_няма_в_приложението()
    {
        // Part 4 asks for "changing a password from inside". The profile has no
        // such handler and no such field: participants have no password at all,
        // and the administrator's comes from AdminSettings:SystemAdminPassword.
        // This test pins that state down, so that if a password change appears it
        // fails and says the flow now needs coverage.
        var user = await NewParticipantAsync();

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var page = await session.Client.GetStringAsync("/Profile");
        Assert.DoesNotContain("type=\"password\"", page);

        // An unknown handler in Razor Pages is not an error: the request simply
        // falls through to OnPostAsync. The question is therefore not what the
        // status code is, but whether such an account can be given a password at
        // all.
        foreach (var handler in new[] { "ChangePassword", "SetPassword", "UpdatePassword" })
        {
            await session.PostHandlerAsync("/Profile", handler,
                new Dictionary<string, string> { ["password"] = "Whatever-1" });
        }

        Assert.False(await App.Db.HasPasswordAsync(user.Email!));
    }

    private static Dictionary<string, string> Merge(Dictionary<string, string> form, string token)
    {
        form["__RequestVerificationToken"] = token;
        return form;
    }
}
