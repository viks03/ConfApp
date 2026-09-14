// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Profile;

/// <summary>
/// Part 4, uploading a paper after registration. Downloading someone else's
/// paper is part 5; here there is only one's own.
/// </summary>
[Collection(AppCollection.Name)]
public class PaperUploadTests : ProfileTestBase
{
    public PaperUploadTests(AppFixture app) : base(app, "pf-paper") { }

    [Fact]
    public async Task Докладът_се_качва_от_профила_и_ляга_под_частния_корен()
    {
        var user = await NewParticipantAsync();
        var auditFrom = await App.Db.LastAuditIdAsync();

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var response = await session.PostMultipartAsync("/Profile",
            ProfileForm(user),
            new[] { UploadFile.Pdf("Input.UploadedFile") });

        Assert.Contains(Resx.Value("Pages.Profile", "Msg_UpdateSuccess"), await response.ReadPageAsync());

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.NotNull(after!.PaperFilePath);
        Assert.StartsWith("uploads/papers26/", after.PaperFilePath);
        Assert.EndsWith(".pdf", after.PaperFilePath);
        Assert.True(File.Exists(PhysicalPath(after.PaperFilePath!)),
            "Докладът не е записан под частния корен.");

        var audit = await App.Db.AuditSinceAsync(auditFrom);
        Assert.Contains(audit, a => a.Action == "Profile Update"
                                    && a.Details != null
                                    && a.Details.Contains("Uploaded New File"));
    }

    [Fact]
    public async Task Свой_доклад_се_сваля()
    {
        var user = await NewParticipantAsync();

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        await session.PostMultipartAsync("/Profile", ProfileForm(user),
            new[] { UploadFile.Pdf("Input.UploadedFile") });

        var response = await session.Client.GetAsync("/Profile?handler=Download");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("%PDF", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Сваляне_без_качен_доклад_връща_404()
    {
        var user = await NewParticipantAsync();

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var response = await session.Client.GetAsync("/Profile?handler=Download");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Новият_доклад_замества_стария_и_старият_файл_изчезва()
    {
        var user = await NewParticipantAsync();

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        await session.PostMultipartAsync("/Profile", ProfileForm(user),
            new[] { UploadFile.Pdf("Input.UploadedFile", "purvi.pdf") });

        var first = (await App.Db.FindUserAsync(user.Email!))!.PaperFilePath!;
        var firstPhysical = PhysicalPath(first);
        Assert.True(File.Exists(firstPhysical));

        await session.PostMultipartAsync("/Profile", ProfileForm(user),
            new[] { UploadFile.Pdf("Input.UploadedFile", "vtori.pdf") });

        var second = (await App.Db.FindUserAsync(user.Email!))!.PaperFilePath!;

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(PhysicalPath(second)), "Новият доклад липсва.");
        Assert.False(File.Exists(firstPhysical),
            "Старият доклад е останал на диска — сираци се трупат при всяка подмяна.");
    }

    [Theory]
    [InlineData("doklad.exe",  "application/octet-stream")]
    [InlineData("doklad.html", "text/html")]
    [InlineData("doklad.png",  "image/png")]
    public async Task Непозволено_разширение_се_отхвърля_и_не_пипа_стария(string name, string mime)
    {
        var user = await NewParticipantAsync();

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        await session.PostMultipartAsync("/Profile", ProfileForm(user),
            new[] { UploadFile.Pdf("Input.UploadedFile", "dobur.pdf") });

        var existing = (await App.Db.FindUserAsync(user.Email!))!.PaperFilePath!;

        var response = await session.PostMultipartAsync("/Profile", ProfileForm(user),
            new[] { UploadFile.Pdf("Input.UploadedFile", name) with { ContentType = mime } });

        Assert.Contains(Resx.Value("Pages.Profile", "Error_InvalidFileType"),
            await response.ReadPageAsync());

        // A rejected upload must not take away the paper already uploaded.
        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal(existing, after!.PaperFilePath);
        Assert.True(File.Exists(PhysicalPath(existing)));
    }

    [Fact]
    public async Task Файл_над_лимита_се_отхвърля()
    {
        var user = await NewParticipantAsync();

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        // The limit in the profile is 10 MB.
        var tooBig = UploadFile.Pdf("Input.UploadedFile").OfSize(10 * 1024 * 1024 + 1024);

        var response = await session.PostMultipartAsync("/Profile", ProfileForm(user), new[] { tooBig });

        Assert.Contains(Resx.Prefix("Pages.Profile", "Error_FileTooLarge"),
            await response.ReadPageAsync());
        Assert.Null((await App.Db.FindUserAsync(user.Email!))!.PaperFilePath);
    }

    [Fact]
    public async Task Отказаното_качване_не_записва_и_останалите_промени()
    {
        // The handler returns Page() as soon as the file is wrong, so the name the
        // user changed in the same form is not saved either. The check guards
        // exactly that: all of it or none of it.
        var user = await NewParticipantAsync();

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        await session.PostMultipartAsync("/Profile",
            ProfileForm(user, firstName: "Ivana"),
            new[] { UploadFile.Pdf("Input.UploadedFile", "losh.exe") with { ContentType = "application/octet-stream" } });

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal(user.FirstName, after!.FirstName);
        Assert.Null(after.PaperFilePath);
    }
}
