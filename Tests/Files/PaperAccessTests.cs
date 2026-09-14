// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Net;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Files;

/// <summary>
/// Part 5, who can download which paper.
/// <para>
/// Phase 2 moved the uploads out of <c>wwwroot</c> ([F-02]): until then every
/// paper was served anonymously by the static-file middleware to anyone who
/// guessed the name. The only path to the file now goes through a handler, so
/// the question of whose file came back is decided by code rather than by a
/// folder.
/// </para>
/// </summary>
[Collection(AppCollection.Name)]
public class PaperAccessTests : FileTestBase
{
    public PaperAccessTests(AppFixture app) : base(app, "fl-paper") { }

    // ════════════════════════════════════════════════════════════════════
    // One's own paper
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Свой_доклад_се_сваля_с_името_под_което_е_записан()
    {
        var user = await NewParticipantAsync();
        using var session = await SignedInAsync(user);

        var relative = await UploadPaperAsync(session, user, PaperOf("moят"));

        var response = await session.Client.GetAsync("/Profile?handler=Download");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PaperOf("moят"), await response.Content.ReadAsByteArrayAsync());

        // The name in Content-Disposition is the one from the database rather than
        // the original: on disk the file is a GUID, and that is what the browser
        // gets.
        Assert.Equal(Path.GetFileName(relative),
            response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
    }

    /// <summary>
    /// The handler has no parameter for a user: the file is taken from whoever is
    /// signed in. The test tries to steer it anyway, so that if such a parameter
    /// ever appears it shows up here.
    /// </summary>
    [Theory]
    [InlineData("userId")]
    [InlineData("id")]
    [InlineData("user")]
    [InlineData("email")]
    public async Task Чужд_доклад_не_се_измъква_през_своя_профил(string parameter)
    {
        var victim = await NewParticipantAsync();
        using var victimSession = await SignedInAsync(victim);
        await UploadPaperAsync(victimSession, victim, PaperOf("chuzhd"));

        var attacker = await NewParticipantAsync();
        using var attackerSession = await SignedInAsync(attacker);
        await UploadPaperAsync(attackerSession, attacker, PaperOf("moi"));

        var victimRow = (await App.Db.FindUserAsync(victim.Email!))!;

        var response = await attackerSession.Client.GetAsync(
            $"/Profile?handler=Download&{parameter}={Uri.EscapeDataString(victimRow.Id)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PaperOf("moi"), await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Сваляне_на_доклад_без_влизане_праща_към_входа()
    {
        var user = await NewParticipantAsync();
        using var session = await SignedInAsync(user);
        await UploadPaperAsync(session, user, PaperOf("chuzhd"));

        using var anonymous = App.NewClient(followRedirects: false);
        var response = await anonymous.GetAsync("/Profile?handler=Download");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login", response.Headers.Location?.OriginalString ?? string.Empty);
    }

    // ════════════════════════════════════════════════════════════════════
    // The path through the admin panel
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Администраторът_сваля_доклада_на_участник()
    {
        var user = await NewParticipantAsync();
        using var session = await SignedInAsync(user);
        await UploadPaperAsync(session, user, PaperOf("za-panela"));

        var row = (await App.Db.FindUserAsync(user.Email!))!;

        using var admin = await SignedInAdminAsync();
        var response = await admin.Client.GetAsync($"/Admin?handler=DownloadPaper&userId={row.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PaperOf("za-panela"), await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Участник_не_сваля_чужд_доклад_през_панела()
    {
        var victim = await NewParticipantAsync();
        using var victimSession = await SignedInAsync(victim);
        await UploadPaperAsync(victimSession, victim, PaperOf("taen"));

        var victimRow = (await App.Db.FindUserAsync(victim.Email!))!;

        var attacker = await NewParticipantAsync();
        using var attackerSession = await SignedInAsync(attacker);

        using var client = attackerSession.NoRedirectClient();
        var response = await client.GetAsync($"/Admin?handler=DownloadPaper&userId={victimRow.Id}");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("%PDF", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Външен_не_сваля_доклад_през_панела()
    {
        var user = await NewParticipantAsync();
        using var session = await SignedInAsync(user);
        await UploadPaperAsync(session, user, PaperOf("taen"));

        var row = (await App.Db.FindUserAsync(user.Email!))!;

        using var anonymous = App.NewClient(followRedirects: false);
        var response = await anonymous.GetAsync($"/Admin?handler=DownloadPaper&userId={row.Id}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login", response.Headers.Location?.OriginalString ?? string.Empty);
    }

    // ════════════════════════════════════════════════════════════════════
    // A path in the database is not a promise
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>PaperFilePath</c> is a string in the database. If a value pointing
    /// outside ever lands there — through a botched migration, an import, or a
    /// future handler — the download must not follow it. What is guarded is the
    /// promise of <c>UploadPathService.ToPhysical</c>: a path outside the root
    /// yields <c>null</c>.
    /// </summary>
    [Theory]
    [InlineData("uploads/papers26/../../../../conferenceapp.db")]
    [InlineData("uploads/papers26/..\\..\\..\\..\\conferenceapp.db")]
    [InlineData("../../conferenceapp.db")]
    [InlineData("/etc/hosts")]
    [InlineData("~/../../conferenceapp.db")]
    public async Task Път_извън_частния_корен_не_се_сваля(string doctored)
    {
        var user = await NewParticipantAsync();
        using var session = await SignedInAsync(user);
        // The real file stays on disk because the row no longer points at it, so it
        // is registered separately for cleanup.
        TrackFile(await UploadPaperAsync(session, user, PaperOf("истински")));

        await App.Db.SetPaperPathAsync(user.Email!, doctored);

        var own = await session.Client.GetAsync("/Profile?handler=Download");
        Assert.Equal(HttpStatusCode.NotFound, own.StatusCode);

        var dbRow = (await App.Db.FindUserAsync(user.Email!))!;
        using var admin = await SignedInAdminAsync();
        var panel = await admin.Client.GetAsync($"/Admin?handler=DownloadPaper&userId={dbRow.Id}");
        Assert.Equal(HttpStatusCode.NotFound, panel.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════
    // The old addresses
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Каченият_доклад_не_се_раздава_на_стария_си_адрес()
    {
        var user = await NewParticipantAsync();
        using var session = await SignedInAsync(user);

        var relative = await UploadPaperAsync(session, user, PaperOf("не-за-статичния"));

        Assert.True(File.Exists(PhysicalPath(relative)),
            "Докладът не е под частния корен — тестът щеше да провери грешното нещо.");

        // The same file under the same name, but there is nothing at the old
        // address any more — not even for the signed-in owner: the static-file
        // middleware does not ask who you are, so a 200 here would mean it is
        // served to anyone who guesses the name.
        using var anonymous = App.NewClient(followRedirects: false);

        foreach (var url in new[]
                 {
                     "/" + relative,
                     "/" + relative.ToUpperInvariant(),
                     "/App_Data/" + relative,
                     "/wwwroot/" + relative
                 })
        {
            var response = await anonymous.GetAsync(url);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        var asOwner = await session.Client.GetAsync("/" + relative);
        Assert.Equal(HttpStatusCode.NotFound, asOwner.StatusCode);
    }
}
