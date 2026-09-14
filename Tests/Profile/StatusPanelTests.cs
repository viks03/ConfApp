// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;

namespace ConferenceApp.Tests.Profile;

/// <summary>
/// Part 4, the status panel in the profile in each of its states. The class on
/// the panel is what is asserted on, because that is what gives the user the
/// colour and the tone: waiting, confirmed, rejected.
/// </summary>
[Collection(AppCollection.Name)]
public class StatusPanelTests : ProfileTestBase
{
    public StatusPanelTests(AppFixture app) : base(app, "pf-status") { }

    private async Task<string> ProfilePageAsync(string email)
    {
        using var session = App.NewSession();
        await session.LoginParticipantAsync(email);
        return Html.Text(await session.Client.GetStringAsync("/Profile"));
    }

    /// <summary>
    /// The buttons of the status panel alone.
    /// <para>
    /// Searching the whole page for "/SubmitDocuments" does not work: the footer
    /// carries quick links to the same addresses on every page, so any such
    /// assertion passes every time. Only the anchor with the panel's class is
    /// looked at here.
    /// </para>
    /// </summary>
    private static string[] StatusActions(string page) =>
        System.Text.RegularExpressions.Regex.Matches(page, "<a\\s[^>]*>")
            .Select(m => m.Value)
            // The attribute order is not the same throughout: anchors written by hand
            // start with href, while those from asp-page get href last.
            .Where(tag => tag.Contains("status-action-btn", StringComparison.Ordinal))
            .Select(tag => System.Text.RegularExpressions.Regex
                .Match(tag, "href=\"(?<href>[^\"]*)\"").Groups["href"].Value)
            .Where(href => href.Length > 0)
            .ToArray();

    // ── The group that pays: lecturers and online attendees ─────────────

    [Fact]
    public async Task Неплатил_вижда_подкана_за_плащане_с_връзка_към_своята_тарифа()
    {
        var user = await NewParticipantAsync(partForm: "1");
        var page = await ProfilePageAsync(user.Email!);

        Assert.Contains("status-panel status-warning", page);
        Assert.Contains(user.ReferenceNumber!, page);
        Assert.Contains(StatusActions(page), href => href.StartsWith("/Payment/"));

        Assert.DoesNotContain("status-panel status-confirmed", page);
    }

    [Fact]
    public async Task Заявил_превод_вижда_очакване_а_не_подкана()
    {
        var user = await NewParticipantAsync(partForm: "1");
        await App.Db.MarkIbanSubmittedAsync(user.Email!);

        var page = await ProfilePageAsync(user.Email!);

        Assert.Contains("status-iban", page);
        Assert.Contains(user.ReferenceNumber!, page);
        Assert.DoesNotContain("status-panel status-warning", page);
    }

    [Fact]
    public async Task Платил_вижда_потвърдено()
    {
        var user = await NewParticipantAsync(partForm: "1", paymentStatus: "Confirmed");
        var page = await ProfilePageAsync(user.Email!);

        Assert.Contains("status-panel status-confirmed", page);
        Assert.DoesNotContain("status-panel status-warning", page);

        // Someone confirmed has nothing to pay, so the panel offers no action.
        Assert.Empty(StatusActions(page));
    }

    // ── The group that gets verified: students and journalists ──────────

    [Fact]
    public async Task Студент_без_документи_вижда_подкана_за_подаване()
    {
        var user = await NewParticipantAsync(partForm: "2");
        var page = await ProfilePageAsync(user.Email!);

        Assert.Contains("status-panel status-warning", page);
        Assert.Contains("/SubmitDocuments", StatusActions(page));
    }

    [Fact]
    public async Task Подадените_документи_се_виждат_като_очакване()
    {
        var user = await NewParticipantAsync(partForm: "2");
        await App.Db.SetVerificationAsync(user.Email!, "Pending",
            "uploads/submitted-documents/students/probe.png");

        var page = await ProfilePageAsync(user.Email!);

        Assert.Contains("status-panel status-pending", page);
        Assert.DoesNotContain("status-panel status-rejected", page);

        // While it is under review the panel has nothing to offer.
        Assert.Empty(StatusActions(page));
    }

    [Fact]
    public async Task Одобрената_верификация_се_вижда_като_потвърдена()
    {
        var user = await NewParticipantAsync(partForm: "2");
        await App.Db.SetVerificationAsync(user.Email!, "Approved",
            "uploads/submitted-documents/students/probe.png");

        var page = await ProfilePageAsync(user.Email!);

        Assert.Contains("status-panel status-confirmed", page);
        Assert.Empty(StatusActions(page));
    }

    [Fact]
    public async Task Отказаната_верификация_показва_причината_и_път_напред()
    {
        const string reason = "Снимката е нечетима — качете по-ясен документ.";

        var user = await NewParticipantAsync(partForm: "2");
        await App.Db.SetVerificationAsync(user.Email!, "Rejected",
            "uploads/submitted-documents/students/probe.png", reason);

        var page = await ProfilePageAsync(user.Email!);

        Assert.Contains("status-panel status-rejected", page);
        Assert.Contains(reason, page);

        // Someone who was turned down has to be able to submit again.
        Assert.Contains("/SubmitDocuments", StatusActions(page));
    }

    [Fact]
    public async Task Статусът_на_чужд_участник_не_се_вижда()
    {
        // The profile has no parameter for a user: it shows only whoever is signed
        // in. The check guards that: the other person's reference number does not
        // appear.
        var mine  = await NewParticipantAsync(partForm: "1");
        var other = await NewParticipantAsync(partForm: "1", paymentStatus: "Confirmed");

        var page = await ProfilePageAsync(mine.Email!);

        Assert.Contains(mine.ReferenceNumber!, page);
        Assert.DoesNotContain(other.ReferenceNumber!, page);
        Assert.DoesNotContain(other.Email!, page);
    }

    [Fact]
    public async Task Статусът_проследява_смяната_на_формата_на_участие()
    {
        // One profile in two different states: an unpaid lecturer sees a prompt to
        // pay, and after switching to student a prompt for documents.
        var user = await NewParticipantAsync(partForm: "1");

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        var before = Html.Text(await session.Client.GetStringAsync("/Profile"));
        Assert.Contains(StatusActions(before), href => href.StartsWith("/Payment/"));

        var form = ProfileForm(user, partForm: "2");
        form["__RequestVerificationToken"] = await session.AntiforgeryTokenAsync("/Profile");
        await session.PostFormAsync("/Profile", form);

        var after = Html.Text(await session.Client.GetStringAsync("/Profile"));
        Assert.Contains("/SubmitDocuments", StatusActions(after));
        Assert.DoesNotContain(StatusActions(after), href => href.StartsWith("/Payment/"));
    }
}
