// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Files;

/// <summary>
/// The common ground of part 5. Every test creates its own participants and
/// files and clears them away itself, from the database and from the disk alike,
/// because the private root outlives the test.
/// </summary>
public abstract class FileTestBase : IAsyncLifetime
{
    protected readonly AppFixture App;
    private readonly List<string> _createdUsers = new();
    private readonly List<string> _createdFiles = new();
    private readonly string _tag;

    protected FileTestBase(AppFixture app, string tag)
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
            var user = await App.Db.FindUserAsync(email);
            DeleteUpload(user?.PaperFilePath);
            DeleteUpload(user?.VerificationDocumentPath);

            await App.Db.DeleteParticipantAsync(email);
            await App.Db.DeleteOtpCodesAsync(email);
        }

        foreach (var relative in _createdFiles)
            DeleteUpload(relative);
    }

    protected async Task<ApplicationUser> NewParticipantAsync(
        string partForm = "1", string paymentStatus = "Pending")
    {
        var email = $"{_tag}-{Guid.NewGuid():N}@example.test";
        _createdUsers.Add(email);
        return await App.Db.CreateParticipantAsync(email, partForm, paymentStatus);
    }

    /// <summary>A signed-in participant, the human way, with a code from the database.</summary>
    protected async Task<HttpSession> SignedInAsync(ApplicationUser user)
    {
        var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);
        return session;
    }

    /// <summary>A signed-in administrator, through an ordinary password sign-in.</summary>
    protected async Task<HttpSession> SignedInAdminAsync()
    {
        var session = App.NewSession();
        await session.LoginAdminAsync(App.Credentials.AdminEmail, App.Credentials.AdminPassword);
        return session;
    }

    // ════════════════════════════════════════════════════════════════════
    // Files
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Turns a relative path, as held in the database, into a physical one under the private root.</summary>
    protected static string PhysicalPath(string relative) =>
        Path.Combine(AppFixture.PrivateUploadsRoot,
            relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The physical path of a public file; those stay under wwwroot.</summary>
    protected static string PublicPhysicalPath(string relative) =>
        Path.Combine(TestPaths.RepoRoot, "wwwroot",
            relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    /// <summary>A file that has to be gone after the test, even if no user points at it.</summary>
    protected void TrackFile(string? relative)
    {
        if (!string.IsNullOrEmpty(relative)) _createdFiles.Add(relative);
    }

    /// <summary>
    /// Removes a file after the test. It refuses to touch a path that leaves the
    /// private root: part 5 deliberately puts such paths into the database, and
    /// the cleanup must not be more dangerous than the bug the test looks for.
    /// </summary>
    protected static void DeleteUpload(string? relative)
    {
        if (string.IsNullOrEmpty(relative)) return;

        var full = Path.GetFullPath(PhysicalPath(relative));
        var root = Path.GetFullPath(AppFixture.PrivateUploadsRoot) + Path.DirectorySeparatorChar;

        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(full)) File.Delete(full);
    }

    /// <summary>
    /// Uploads a paper from the profile and returns the relative path that ended
    /// up in the database. The content is distinguishable, so that a test can say
    /// whose file it has just downloaded.
    /// </summary>
    protected async Task<string> UploadPaperAsync(
        HttpSession session, ApplicationUser user, byte[] content, string name = "doklad.pdf")
    {
        var response = await session.PostMultipartAsync("/Profile",
            ParticipantForms.Profile(user),
            new[] { UploadFile.Pdf("Input.UploadedFile", name) with { Content = content } });

        response.EnsureSuccessStatusCode();

        var after = await App.Db.FindUserAsync(user.Email!);

        return after?.PaperFilePath
            ?? throw new InvalidOperationException(
                $"Качването на доклад за {user.Email} не остави път в базата — " +
                "формата е отказала подаването.");
    }

    /// <summary>A distinguishable "PDF", for the question of whose file came back.</summary>
    protected static byte[] PaperOf(string marker) =>
        System.Text.Encoding.ASCII.GetBytes(
            $"%PDF-1.4\n% {marker}\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");
}
