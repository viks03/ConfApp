// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using ConferenceApp.Models;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Files;

/// <summary>
/// Part 5, the photograph of an identity document. Of the three kinds of
/// uploaded file this is the most sensitive: a student card or a press card with
/// a photograph and a number on it.
/// </summary>
[Collection(AppCollection.Name)]
public class VerificationDocAccessTests : FileTestBase
{
    public VerificationDocAccessTests(AppFixture app) : base(app, "fl-verif") { }

    /// <summary>An image with distinguishable content, for the question of whose document came back.</summary>
    private static UploadFile Card(string marker) =>
        UploadFile.Png("StudentInput.StudentCard", "karta.png") with
        {
            Content = UploadFile.Png("x").Content
                .Concat(System.Text.Encoding.ASCII.GetBytes("\n% " + marker))
                .ToArray()
        };

    /// <summary>Submits documents as a student, the human way.</summary>
    private async Task<string> SubmitStudentAsync(
        HttpSession session, ApplicationUser user, string marker)
    {
        var response = await session.PostMultipartAsync("/SubmitDocuments",
            new Dictionary<string, string>
            {
                ["StudentInput.University"] = "UNWE",
                ["StudentInput.Specialty"]  = "Finance",
                ["StudentInput.StudyYear"]  = "3",
                ["StudentInput.StudentId"]  = "1234567"
            },
            new[] { Card(marker) });

        response.EnsureSuccessStatusCode();

        var after = await App.Db.FindUserAsync(user.Email!);

        return after?.VerificationDocumentPath
            ?? throw new InvalidOperationException(
                $"Подаването на документи за {user.Email} не остави път в базата.");
    }

    // ════════════════════════════════════════════════════════════════════
    // Who can reach the document
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Администраторът_сваля_документа_на_участник()
    {
        var user = await NewParticipantAsync(partForm: "2");
        using var session = await SignedInAsync(user);
        await SubmitStudentAsync(session, user, "za-panela");

        var row = (await App.Db.FindUserAsync(user.Email!))!;

        using var admin = await SignedInAdminAsync();
        var response = await admin.Client.GetAsync($"/Admin?handler=DownloadVerifDoc&userId={row.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Card("za-panela").Content, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Участник_не_сваля_чужд_документ_за_верификация()
    {
        var victim = await NewParticipantAsync(partForm: "2");
        using var victimSession = await SignedInAsync(victim);
        await SubmitStudentAsync(victimSession, victim, "taen");

        var victimRow = (await App.Db.FindUserAsync(victim.Email!))!;

        var attacker = await NewParticipantAsync(partForm: "2");
        using var attackerSession = await SignedInAsync(attacker);

        using var client = attackerSession.NoRedirectClient();
        var response = await client.GetAsync($"/Admin?handler=DownloadVerifDoc&userId={victimRow.Id}");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("PNG", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Външен_не_сваля_документ_за_верификация()
    {
        var user = await NewParticipantAsync(partForm: "2");
        using var session = await SignedInAsync(user);
        await SubmitStudentAsync(session, user, "taen");

        var row = (await App.Db.FindUserAsync(user.Email!))!;

        using var anonymous = App.NewClient(followRedirects: false);
        var response = await anonymous.GetAsync($"/Admin?handler=DownloadVerifDoc&userId={row.Id}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login", response.Headers.Location?.OriginalString ?? string.Empty);
    }

    /// <summary>
    /// [T-14] The "View" link on <c>/SubmitDocuments</c> points at the admin
    /// panel's handler with the user's own id. The test follows exactly that
    /// link.
    /// </summary>
    [Fact]
    public async Task Своят_собствен_документ_се_вижда_от_връзката_в_страницата()
    {
        // Someone who was rejected is the only one who sees the page with a file
        // already uploaded: under "Pending" and "Approved" OnGetAsync sends them
        // to the profile.
        var user = await NewParticipantAsync(partForm: "2");
        using var session = await SignedInAsync(user);

        var relative = await SubmitStudentAsync(session, user, "moят");
        await App.Db.SetVerificationAsync(user.Email!, "Rejected", relative, "Нечетлива снимка.");

        var page = await session.Client.GetStringAsync("/SubmitDocuments");

        // The link from the view is what is searched for, rather than an assumed
        // address: the address in it is exactly what was wrong ([T-14]).
        var link = System.Text.RegularExpressions.Regex.Match(page,
            "class=\"sd-ghost-btn\"[^>]*href=\"(?<url>[^\"]+)\"");

        Assert.True(link.Success,
            "На /SubmitDocuments няма връзка към качения документ — тестът щеше да провери нищо.");

        // The link is relative to the page, and is resolved the way a browser
        // resolves it.
        var url = new Uri(new Uri(App.BaseUrl + "/SubmitDocuments"),
                          WebUtility.HtmlDecode(link.Groups["url"].Value));

        using var client = session.NoRedirectClient();
        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Card("moят").Content, await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>
    /// The new handler has no parameter for a user: the file is taken from
    /// whoever is signed in. The test tries to steer it anyway.
    /// </summary>
    [Fact]
    public async Task Своят_handler_не_дава_чужд_документ()
    {
        var victim = await NewParticipantAsync(partForm: "2");
        using var victimSession = await SignedInAsync(victim);
        var victimDoc = await SubmitStudentAsync(victimSession, victim, "chuzhd");
        await App.Db.SetVerificationAsync(victim.Email!, "Rejected", victimDoc, "…");

        var victimRow = (await App.Db.FindUserAsync(victim.Email!))!;

        var attacker = await NewParticipantAsync(partForm: "2");
        using var attackerSession = await SignedInAsync(attacker);
        var own = await SubmitStudentAsync(attackerSession, attacker, "moi");
        await App.Db.SetVerificationAsync(attacker.Email!, "Rejected", own, "…");

        var response = await attackerSession.Client.GetAsync(
            $"/SubmitDocuments?handler=ViewDocument&userId={victimRow.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Card("moi").Content, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Своят_handler_иска_влизане()
    {
        using var anonymous = App.NewClient(followRedirects: false);
        var response = await anonymous.GetAsync("/SubmitDocuments?handler=ViewDocument");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login", response.Headers.Location?.OriginalString ?? string.Empty);
    }

    // ════════════════════════════════════════════════════════════════════
    // The path and the address
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Документът_не_се_раздава_на_стария_си_адрес()
    {
        var user = await NewParticipantAsync(partForm: "2");
        using var session = await SignedInAsync(user);

        var relative = await SubmitStudentAsync(session, user, "не-за-статичния");

        Assert.StartsWith("uploads/submitted-documents/", relative);
        Assert.True(File.Exists(PhysicalPath(relative)),
            "Документът не е под частния корен — тестът щеше да провери грешното нещо.");

        using var anonymous = App.NewClient(followRedirects: false);

        foreach (var url in new[] { "/" + relative, "/App_Data/" + relative, "/wwwroot/" + relative })
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(url)).StatusCode);

        var asOwner = await session.Client.GetAsync("/" + relative);
        Assert.Equal(HttpStatusCode.NotFound, asOwner.StatusCode);
    }

    [Theory]
    [InlineData("uploads/submitted-documents/../../../../conferenceapp.db")]
    [InlineData("../../conferenceapp.db")]
    [InlineData("/etc/hosts")]
    public async Task Път_извън_частния_корен_не_се_сваля_и_от_панела(string doctored)
    {
        var user = await NewParticipantAsync(partForm: "2");
        using var session = await SignedInAsync(user);

        TrackFile(await SubmitStudentAsync(session, user, "истински"));
        await App.Db.SetVerificationDocumentPathAsync(user.Email!, doctored);

        var row = (await App.Db.FindUserAsync(user.Email!))!;

        using var admin = await SignedInAdminAsync();
        var response = await admin.Client.GetAsync($"/Admin?handler=DownloadVerifDoc&userId={row.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
