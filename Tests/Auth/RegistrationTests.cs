// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace ConferenceApp.Tests.Auth;

/// <summary>
/// Part 3, registration. The happy path goes through a browser — three phases,
/// an uploaded paper, the six code boxes — while the rejections go over plain
/// HTTP, because the checks behind them live on the server and that is exactly
/// what has to be proved: that they do not depend on validation.js.
/// </summary>
[Collection(AppCollection.Name)]
public class RegistrationTests : IAsyncLifetime
{
    private readonly AppFixture _app;
    private readonly List<string> _created = new();

    public RegistrationTests(AppFixture app) => _app = app;

    public Task InitializeAsync()
    {
        _app.Smtp.Clear();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var email in _created)
        {
            var user = await _app.Db.FindUserAsync(email);
            if (user?.PaperFilePath != null) DeletePaper(user.PaperFilePath);

            await _app.Db.DeleteParticipantAsync(email);
            await _app.Db.DeleteOtpCodesAsync(email);
        }
    }

    private string NewEmail(string tag)
    {
        var email = $"reg-{tag}-{Guid.NewGuid():N}@example.test";
        _created.Add(email);
        return email;
    }

    private static void DeletePaper(string relative)
    {
        var full = Path.Combine(AppFixture.PrivateUploadsRoot,
            relative.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(full)) File.Delete(full);
    }

    // ════════════════════════════════════════════════════════════════════
    // The happy path
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Пълна_регистрация_с_доклад_стига_до_готово_и_до_профила()
    {
        var email = NewEmail("full");
        var paper = Path.Combine(TestPaths.RunScratch, $"doklad-{Guid.NewGuid():N}.pdf");
        Directory.CreateDirectory(TestPaths.RunScratch);
        await File.WriteAllBytesAsync(paper, MinimalPdf());

        await using var context = await _app.NewBrowserContextAsync();
        var page = await context.NewPageAsync();

        var consoleErrors = new List<string>();
        page.Console += (_, msg) => { if (msg.Type == "error") consoleErrors.Add(msg.Text); };

        // ── Phase 1: personal details ────────────────────────────────────
        await page.GotoAsync("/Register");
        await page.AcceptCookieNoticeAsync();
        await page.FillAsync("#Input_FirstName",     "Ivan");
        await page.FillAsync("#Input_LastName",      "Petrov");
        await page.FillAsync("#Input_Age",           "34");
        await page.FillAsync("#Input_AcademicTitle", "Assoc. Prof.");
        await page.FillAsync("#Input_Email",         email);
        await page.FillAsync("#Input_Phone",         "+359 888 123456");
        await page.ClickAsync("#submitBtn");

        // ── Phase 2: organisation, form of participation, paper ──────────
        await page.WaitForSelectorAsync("#Input_Workplace");
        await page.FillAsync("#Input_Workplace", "UNWE");
        // The checkbox is hidden beneath its own label; a person clicks the label.
        await page.ClickAsync(".auth-choice label:has(input[value='1'])");
        Assert.True(await page.IsCheckedAsync("input[name='Input.PartForm'][value='1']"));
        await page.SetInputFilesAsync("#Input_UploadedFile", paper);
        await page.ClickAsync("#submitBtn");

        // ── Phase 3: consents ────────────────────────────────────────────
        await page.WaitForSelectorAsync("#terms-accept");
        await page.ClickAsync("label.auth-check:has(#terms-accept)");
        Assert.True(await page.IsCheckedAsync("#terms-accept"));
        await page.ClickAsync("#submitBtn");

        await page.WaitForURLAsync("**/Verification");

        // ── The code must be the same in the database and in the message ─
        var otp = await _app.Db.WaitForOtpAsync(email, "Registration", TimeSpan.FromSeconds(20));
        Assert.NotNull(otp);

        var mail = await _app.Smtp.WaitForAsync(email, TimeSpan.FromSeconds(20));
        Assert.NotNull(mail);
        Assert.Contains(otp!.Code, mail!.Body);

        // The participant exists by now, but is not confirmed yet.
        var pending = await _app.Db.FindUserAsync(email);
        Assert.NotNull(pending);
        Assert.False(pending!.EmailConfirmed);
        Assert.StartsWith("BCE2026-", pending.ReferenceNumber);
        Assert.NotNull(pending.PaperFilePath);
        Assert.True(pending.HasAcceptedGdpr);

        // The paper sits under the private root rather than in wwwroot.
        var stored = Path.Combine(AppFixture.PrivateUploadsRoot,
            pending.PaperFilePath!.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(stored), $"Докладът не е записан на {stored}.");
        Assert.StartsWith("uploads/papers26/", pending.PaperFilePath);

        // ── Confirmation: the six boxes, filled in as a person fills them ─
        var boxes = page.Locator(".code-box");
        for (var i = 0; i < 6; i++)
            await boxes.Nth(i).FillAsync(otp.Code[i].ToString());

        await page.ClickAsync("button.auth-submit");
        await page.WaitForURLAsync("**/Done");

        var confirmed = await _app.Db.FindUserAsync(email);
        Assert.True(confirmed!.EmailConfirmed);
        Assert.Single(await _app.Db.OtpCodesAsync(email, "Registration"), o => o.IsUsed);

        // ── And they really are signed in ────────────────────────────────
        await page.GotoAsync("/Profile");
        Assert.Contains("/Profile", page.Url);
        Assert.Contains(confirmed.ReferenceNumber!, await page.ContentAsync());

        var audit = await _app.Db.AuditForAsync(email);
        Assert.Contains(audit, a => a.Action == "User Registered");
        Assert.Contains(audit, a => a.Action == "Email Verified");

        Assert.Empty(consoleErrors);
    }

    [Fact]
    public async Task Регистрация_без_доклад_минава_и_оставя_полето_празно()
    {
        var email = NewEmail("nofile");

        using var session = _app.NewSession();
        var result = await RegistrationFlow.RunAsync(session, new RegistrationForm { Email = email });

        Assert.Equal(System.Net.HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Contains("/Verification", result.Response.RequestMessage!.RequestUri!.AbsolutePath);

        var user = await _app.Db.FindUserAsync(email);
        Assert.NotNull(user);
        Assert.Null(user!.PaperFilePath);
        Assert.NotNull(await _app.Db.WaitForOtpAsync(email, "Registration", TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public async Task Регистрираният_излиза_и_влиза_пак_с_нов_код()
    {
        // Closes the circle of part 3: registration, code, confirmation, signing
        // out, a new code, signing in.
        var email = NewEmail("roundtrip");

        using var session = _app.NewSession();
        await RegistrationFlow.RunAsync(session, new RegistrationForm { Email = email });

        var registration = await _app.Db.WaitForOtpAsync(email, "Registration", TimeSpan.FromSeconds(15));
        Assert.NotNull(registration);

        await session.PostFormAsync("/Verification", new Dictionary<string, string>
        {
            ["VerificationCode"] = registration!.Code,
            ["__RequestVerificationToken"] = await session.AntiforgeryTokenAsync("/Verification")
        });

        Assert.True(await session.IsSignedInAsync());

        using (var client = session.NoRedirectClient())
            await client.PostAsync("/Logout", new StringContent(string.Empty));

        Assert.False(await session.IsSignedInAsync());

        // The second time goes down the sign-in path rather than registration.
        using var again = _app.NewSession();
        await again.LoginParticipantAsync(email);
        Assert.True(await again.IsSignedInAsync());
    }

    // ════════════════════════════════════════════════════════════════════
    // The rejections
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Регистрация_с_вече_зает_имейл_се_отказва()
    {
        var email = NewEmail("taken");
        var existing = await _app.Db.CreateParticipantAsync(email);

        using var session = _app.NewSession();
        var result = await RegistrationFlow.RunAsync(session, new RegistrationForm { Email = email });

        Assert.True(result.Says(Resx.Value("Pages.Register", "Error_EmailTaken")),
            "Страницата не казва, че имейлът е зает.");

        // The second account must not exist, and the first must be untouched.
        var users = await _app.Db.ReadAsync(db => db.Users.CountAsync(u => u.Email == email));
        Assert.Equal(1, users);

        var after = await _app.Db.FindUserAsync(email);
        Assert.Equal(existing.Id, after!.Id);
        Assert.Empty(await _app.Db.OtpCodesAsync(email, "Registration"));
    }

    [Fact]
    public async Task Проверката_за_свободен_имейл_вижда_заетия()
    {
        var email = NewEmail("check");
        await _app.Db.CreateParticipantAsync(email);

        using var session = _app.NewSession();

        var taken = await session.Client.GetStringAsync(
            $"/Register?handler=CheckEmail&email={Uri.EscapeDataString(email)}");
        Assert.Contains("\"isAvailable\":false", taken);

        var free = await session.Client.GetStringAsync(
            "/Register?handler=CheckEmail&email=" + Uri.EscapeDataString($"free-{Guid.NewGuid():N}@example.test"));
        Assert.Contains("\"isAvailable\":true", free);
    }

    [Fact]
    public async Task Регистрация_без_съгласие_за_лични_данни_не_създава_акаунт()
    {
        var email = NewEmail("nogdpr");

        using var session = _app.NewSession();
        var result = await RegistrationFlow.RunAsync(
            session, new RegistrationForm { Email = email, Gdpr = false });

        Assert.True(result.Says(Resx.Value("Pages.Register", "Error_GDPR")),
            "Страницата не казва, че съгласието е задължително.");
        Assert.True(result.ShowsPhase(3), "Съветникът не остана на фаза 3.");

        Assert.Null(await _app.Db.FindUserAsync(email));
        Assert.Empty(await _app.Db.OtpCodesAsync(email));
        Assert.Null(await _app.Smtp.WaitForAsync(email, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Директен_POST_на_фаза_3_с_празни_полета_не_прескача_съветника()
    {
        // The fields of InputModel used to carry no validation attribute at all,
        // so ModelState.IsValid was always true and the only check was in the
        // browser. The request here is the one that comes from curl.
        using var session = _app.NewSession();
        var token = await session.AntiforgeryTokenAsync("/Register");

        var response = await session.PostFormAsync("/Register", new Dictionary<string, string>
        {
            ["Phase"] = "3",
            ["Input.IsGDPR"] = "true",
            ["__RequestVerificationToken"] = token
        });

        var html = await response.ReadPageAsync();
        Assert.Contains(Resx.Value("Pages.Register", "Error_Required"), html);

        // Nothing was created: there is no participant with an empty e-mail.
        var empties = await _app.Db.ReadAsync(db =>
            db.Users.CountAsync(u => u.Email == "" || u.Email == null));
        Assert.Equal(0, empties);
    }

    [Fact]
    public async Task Phase_99_се_държи_като_фаза_1_а_не_като_последната()
    {
        // Phase comes from a hidden field, that is, from the client. Clamped down
        // to 1, a phase 1 body has to pass the phase 1 checks and move on to phase
        // 2 rather than be taken for a finished registration.
        var email = NewEmail("phase99");

        using var session = _app.NewSession();
        var form = new RegistrationForm { Email = email };

        var fields = form.Personal();
        fields["Phase"] = "99";
        fields["__RequestVerificationToken"] = await session.AntiforgeryTokenAsync("/Register");

        var response = await session.PostFormAsync("/Register", fields);
        var page = new PostedPage(response, await response.Content.ReadAsStringAsync());

        Assert.True(page.ShowsPhase(2), "Phase=99 с данните на фаза 1 не отиде на фаза 2.");
        Assert.Null(await _app.Db.FindUserAsync(email));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Съвсем_празен_POST_не_мести_фазата(int phase)
    {
        // [T-09] A request without a single Input.* field does not create the
        // Input object, so ModelState holds no entries and validation had nothing
        // to reject: the wizard moved on empty-handed.
        using var session = _app.NewSession();
        var before = await _app.Db.ReadAsync(db => db.Users.CountAsync());

        var response = await session.PostFormAsync("/Register", new Dictionary<string, string>
        {
            ["Phase"] = phase.ToString(),
            ["__RequestVerificationToken"] = await session.AntiforgeryTokenAsync("/Register")
        });

        var page = new PostedPage(response, await response.Content.ReadAsStringAsync());

        Assert.True(page.ShowsPhase(phase),
            $"Празна заявка на фаза {phase} премести брояча вместо да остане на място.");
        Assert.Equal(before, await _app.Db.ReadAsync(db => db.Users.CountAsync()));
    }

    [Theory]
    [InlineData("Age",       "12",     "Error_AgeRange")]
    [InlineData("Age",       "150",    "Error_AgeRange")]
    [InlineData("FirstName", "Иван",   "Error_NameLatinOnly")]
    [InlineData("Email",     "not-an-email", "Error_InvalidEmail")]
    [InlineData("Phone",     "12",     "Error_PhoneFormat")]
    public async Task Невалидно_поле_спира_на_фаза_1(string field, string value, string errorKey)
    {
        var email = NewEmail("invalid");

        var form = field switch
        {
            "Age"       => new RegistrationForm { Email = email, Age = value },
            "FirstName" => new RegistrationForm { Email = email, FirstName = value },
            "Email"     => new RegistrationForm { Email = value },
            _           => new RegistrationForm { Email = email, Phone = value }
        };

        using var session = _app.NewSession();
        var result = await RegistrationFlow.Phase1Async(session, form);

        Assert.True(result.ShowsPhase(1), "Невалидна фаза 1 пусна напред.");
        Assert.True(result.Says(Resx.Value("Pages.Register", errorKey)),
            $"Липсва съобщението за {errorKey}.");
        Assert.Null(await _app.Db.FindUserAsync(email));
    }

    [Fact]
    public async Task Непозната_форма_на_участие_не_минава_фаза_2()
    {
        var email = NewEmail("partform");

        using var session = _app.NewSession();
        var form = new RegistrationForm { Email = email, PartForm = "9" };

        var two    = await RegistrationFlow.Phase1Async(session, form);
        var result = await RegistrationFlow.Phase2Async(session, form, two);

        Assert.True(result.Says(Resx.Value("Pages.Register", "Error_InvalidPartForm")),
            "Форма на участие \"9\" беше приета.");
        Assert.Null(await _app.Db.FindUserAsync(email));
    }

    [Fact]
    public async Task Влязъл_потребител_не_вижда_регистрацията()
    {
        var email = NewEmail("logged");
        await _app.Db.CreateParticipantAsync(email);

        using var session = _app.NewSession();
        await session.LoginParticipantAsync(email);

        using var client = session.NoRedirectClient();
        var response = await client.GetAsync("/Register");

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Profile", response.Headers.Location?.OriginalString);
    }

    /// <summary>The smallest file that passes for a PDF; the content is never read.</summary>
    private static byte[] MinimalPdf() =>
        System.Text.Encoding.ASCII.GetBytes(
            "%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");
}
