// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Models;
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Profile;

/// <summary>
/// The common ground of part 4: a participant that cleans itself up, and the
/// profile form as the browser sends it.
/// </summary>
public abstract class ProfileTestBase : IAsyncLifetime
{
    protected readonly AppFixture App;
    private readonly List<string> _created = new();
    private readonly string _tag;

    protected ProfileTestBase(AppFixture app, string tag)
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
        foreach (var email in _created)
        {
            var user = await App.Db.FindUserAsync(email);
            DeleteUpload(user?.PaperFilePath);
            DeleteUpload(user?.VerificationDocumentPath);

            await App.Db.DeleteParticipantAsync(email);
            await App.Db.DeleteOtpCodesAsync(email);
        }
    }

    protected async Task<ApplicationUser> NewParticipantAsync(
        string partForm = "1", string paymentStatus = "Pending")
    {
        var email = $"{_tag}-{Guid.NewGuid():N}@example.test";
        _created.Add(email);
        return await App.Db.CreateParticipantAsync(email, partForm, paymentStatus);
    }

    protected static string PhysicalPath(string relative) =>
        Path.Combine(AppFixture.PrivateUploadsRoot,
            relative.Replace('/', Path.DirectorySeparatorChar));

    protected static void DeleteUpload(string? relative)
    {
        if (string.IsNullOrEmpty(relative)) return;
        var full = PhysicalPath(relative);
        if (File.Exists(full)) File.Delete(full);
    }

    /// <summary>
    /// The fields of the profile form; see <see cref="ParticipantForms.Profile"/>.
    /// They live in <c>Fixtures/</c> because part 5 uses them as well.
    /// </summary>
    protected static Dictionary<string, string> ProfileForm(
        ApplicationUser user,
        string? firstName     = null,
        string? lastName      = null,
        string? age           = null,
        string? academicTitle = null,
        string? phone         = null,
        string? workplace     = null,
        string? partForm      = null,
        bool?   isForeigner   = null,
        bool?   wantsMarketing = null) =>
        ParticipantForms.Profile(user, firstName, lastName, age, academicTitle,
                                 phone, workplace, partForm, isForeigner, wantsMarketing);
}
