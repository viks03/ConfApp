// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;
using Microsoft.Playwright;

namespace ConferenceApp.Tests.Profile;

/// <summary>
/// Part 4 through a browser: what a person does with a mouse — fills in the
/// profile, picks a file from the disk, clicks.
/// </summary>
[Collection(AppCollection.Name)]
public class ProfileBrowserTests : ProfileTestBase
{
    public ProfileBrowserTests(AppFixture app) : base(app, "pf-ui") { }

    [Fact]
    public async Task Профилът_се_редактира_от_екрана_и_промяната_остава_след_презареждане()
    {
        var user = await NewParticipantAsync();

        await using var context = await App.NewLoggedInContextAsync(user.Email!);
        var page = await context.NewPageAsync();

        var consoleErrors = new List<string>();
        page.Console += (_, msg) => { if (msg.Type == "error") consoleErrors.Add(msg.Text); };

        await page.GotoAsync("/Profile");
        await page.AcceptCookieNoticeAsync();

        await page.FillAsync("#Input_FirstName", "Mariya");
        await page.FillAsync("#Input_Workplace", "Technical University");
        await page.ClickAsync("#submitBtn");

        await page.WaitForSelectorAsync(".pf-saved");

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Mariya", after!.FirstName);
        Assert.Equal("Technical University", after.Workplace);

        // Reload: the fields show what was saved, not what was there before.
        await page.ReloadAsync();
        Assert.Equal("Mariya", await page.InputValueAsync("#Input_FirstName"));
        Assert.Equal("Technical University", await page.InputValueAsync("#Input_Workplace"));

        Assert.Empty(consoleErrors);
    }

    [Fact]
    public async Task Докладът_се_качва_от_екрана()
    {
        var user = await NewParticipantAsync();

        var paper = Path.Combine(TestPaths.RunScratch, $"doklad-{Guid.NewGuid():N}.pdf");
        Directory.CreateDirectory(TestPaths.RunScratch);
        await File.WriteAllBytesAsync(paper, UploadFile.Pdf("x").Content);

        await using var context = await App.NewLoggedInContextAsync(user.Email!);
        var page = await context.NewPageAsync();

        await page.GotoAsync("/Profile");
        await page.AcceptCookieNoticeAsync();

        await page.SetInputFilesAsync("#Input_UploadedFile", paper);
        await page.ClickAsync("#submitBtn");
        await page.WaitForSelectorAsync(".pf-saved");

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.NotNull(after!.PaperFilePath);
        Assert.True(File.Exists(PhysicalPath(after.PaperFilePath!)));
    }

    [Fact]
    public async Task Студент_подава_документите_си_от_екрана_и_статусът_се_сменя()
    {
        var user = await NewParticipantAsync(partForm: "2");

        var card = Path.Combine(TestPaths.RunScratch, $"karta-{Guid.NewGuid():N}.png");
        Directory.CreateDirectory(TestPaths.RunScratch);
        await File.WriteAllBytesAsync(card, UploadFile.Png("x").Content);

        await using var context = await App.NewLoggedInContextAsync(user.Email!);
        var page = await context.NewPageAsync();

        // The profile prompts for documents to be submitted, so that is where this
        // starts.
        await page.GotoAsync("/Profile");
        await page.AcceptCookieNoticeAsync();
        await page.ClickAsync("a.status-action-btn[href='/SubmitDocuments']");
        await page.WaitForURLAsync("**/SubmitDocuments");

        await page.SetInputFilesAsync("#student-doc1", card);
        await page.FillAsync("#StudentInput_University", "UNWE");
        await page.FillAsync("#StudentInput_Specialty",  "Finance");
        await page.FillAsync("#StudentInput_StudentId",  "1234567");
        await page.ClickAsync(".sd-tile:has(input[value='3'])");

        await page.ClickAsync("button.btn-submit");

        // [T-12] Submitting ends up in the profile rather than on the submission
        // page.
        await page.WaitForURLAsync("**/Profile");

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Pending", after!.VerificationStatus);
        Assert.Equal("3",       after.VerificationYear);
        Assert.True(File.Exists(PhysicalPath(after.VerificationDocumentPath!)));

        // The status on screen now says the documents are under review.
        Assert.Contains("status-panel status-pending", await page.ContentAsync());
    }
}
