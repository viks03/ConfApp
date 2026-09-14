// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text;
using ConferenceApp.Models;
using ConferenceApp.Services.Email;
using ConferenceApp.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace ConferenceApp.Tests.Admin;

/// <summary>
/// Part 6, the mail sent from the panel: the switches for the automatic
/// notifications (the Email Notifications tab) and sending invitations by hand
/// (<c>/Admin/SendInvitations</c>).
/// <para>
/// A switch is not checked by the row in the database but by whether the message
/// really stops going out: a switch that is off and stops nothing is worse than
/// no switch at all.
/// </para>
/// </summary>
public class EmailsTests : AdminTestBase
{
    public EmailsTests(AppFixture app) : base(app, "ad-mail") { }

    private readonly List<Guid> _batches = new();

    public override async Task DisposeAsync()
    {
        // The switches are global: left off, every later test goes without mail.
        using (var admin = await SignedInAdminAsync())
        {
            foreach (var template in EmailNotificationSettings.Switchable)
                await PostAsync(admin, "ToggleEmailNotification", new Dictionary<string, string>
                {
                    ["templateKey"] = template.ToString(),
                    ["enabled"]     = "true"
                });
        }

        await App.Db.WriteAsync(async db =>
        {
            var logs = await db.InvitationSendLogs
                .Where(l => _batches.Contains(l.BatchId)).ToListAsync();
            db.InvitationSendLogs.RemoveRange(logs);
        });

        await base.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // The switches
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Панелът_показва_превключвател_за_всеки_шаблон()
    {
        using var admin = await SignedInAdminAsync();
        var html = await PanelAsync(admin);

        foreach (var template in EmailNotificationSettings.Switchable)
            Assert.Contains(template.ToString(), html);
    }

    /// <summary>
    /// The sign-in code has NO switch, deliberately. Turning it off would lock the
    /// site for everyone, the administrator included.
    /// </summary>
    [Fact]
    public async Task Кодът_за_вход_не_може_да_се_изключи()
    {
        Assert.DoesNotContain(EmailTemplate.Otp, EmailNotificationSettings.Switchable);

        using var admin = await SignedInAdminAsync();

        var reply = await PostAsync(admin, "ToggleEmailNotification", new Dictionary<string, string>
        {
            ["templateKey"] = EmailTemplate.Otp.ToString(),
            ["enabled"]     = "false"
        });

        Assert.False(reply.Success);

        // And most importantly, the code still goes out.
        var user = await NewParticipantAsync();
        App.Smtp.Clear();

        using var session = App.NewSession();
        await session.LoginParticipantAsync(user.Email!);

        Assert.NotNull(await App.Smtp.WaitForAsync(user.Email!, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Липсващите_ключове_се_създават_включени()
    {
        using var admin = await SignedInAdminAsync();
        await PanelAsync(admin);

        var saved = await App.Db.ReadAsync(db =>
            db.EmailNotificationSettings.AsNoTracking().ToListAsync());

        foreach (var template in EmailNotificationSettings.Switchable)
            Assert.Contains(saved, s => s.TemplateKey == template.ToString());

        Assert.DoesNotContain(saved, s => s.TemplateKey == EmailTemplate.Otp.ToString());
    }

    [Fact]
    public async Task Изключеният_шаблон_спира_писмото()
    {
        var user = await NewParticipantAsync();

        using var admin = await SignedInAdminAsync();

        var off = await PostAsync(admin, "ToggleEmailNotification", new Dictionary<string, string>
        {
            ["templateKey"] = EmailTemplate.PaymentConfirmed.ToString(),
            ["enabled"]     = "false"
        });
        Assert.Equal(System.Net.HttpStatusCode.OK, off.Status);

        App.Smtp.Clear();

        var confirm = await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id, ["method"] = "IBAN"
        });
        Assert.True(confirm.Success, confirm.Message);

        // The action itself goes through; only the notification is stopped.
        Assert.Equal("Confirmed", (await App.Db.FindUserAsync(user.Email!))!.PaymentStatus);
        Assert.Null(await App.Smtp.WaitForPaymentMailAsync(user.Email!, TimeSpan.FromSeconds(4)));
    }

    [Fact]
    public async Task Обратното_включване_връща_писмото()
    {
        using var admin = await SignedInAdminAsync();

        await PostAsync(admin, "ToggleEmailNotification", new Dictionary<string, string>
        {
            ["templateKey"] = EmailTemplate.PaymentConfirmed.ToString(),
            ["enabled"]     = "false"
        });
        await PostAsync(admin, "ToggleEmailNotification", new Dictionary<string, string>
        {
            ["templateKey"] = EmailTemplate.PaymentConfirmed.ToString(),
            ["enabled"]     = "true"
        });

        var user = await NewParticipantAsync();
        App.Smtp.Clear();

        await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
        {
            ["userId"] = user.Id, ["method"] = "IBAN"
        });

        Assert.NotNull(await App.Smtp.WaitForPaymentMailAsync(user.Email!, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Непознат_шаблон_се_отказва()
    {
        using var admin = await SignedInAdminAsync();

        var reply = await PostAsync(admin, "ToggleEmailNotification", new Dictionary<string, string>
        {
            ["templateKey"] = "NoSuchTemplate",
            ["enabled"]     = "false"
        });

        Assert.False(reply.Success);
        Assert.Contains("Unknown", reply.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Превключването_оставя_следа_в_одита()
    {
        using var admin = await SignedInAdminAsync();
        var lastId = await App.Db.LastAuditIdAsync();

        await PostAsync(admin, "ToggleEmailNotification", new Dictionary<string, string>
        {
            ["templateKey"] = EmailTemplate.StatusChanged.ToString(),
            ["enabled"]     = "false"
        });

        var since = await App.Db.AuditSinceAsync(lastId);
        Assert.Contains(since, a => a.Action == "Email Notification Toggled"
                                 && a.Details.Contains("Disabled"));
    }

    [Fact]
    public async Task Кой_е_превключил_се_запомня()
    {
        using var admin = await SignedInAdminAsync();

        await PostAsync(admin, "ToggleEmailNotification", new Dictionary<string, string>
        {
            ["templateKey"] = EmailTemplate.StatusChanged.ToString(),
            ["enabled"]     = "false"
        });

        var row = await App.Db.ReadAsync(db => db.EmailNotificationSettings.AsNoTracking()
            .FirstAsync(s => s.TemplateKey == EmailTemplate.StatusChanged.ToString()));

        Assert.False(row.IsEnabled);
        Assert.Equal(App.Credentials.AdminEmail, row.LastChangedBy);
    }

    // ════════════════════════════════════════════════════════════════════
    // The templates themselves
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Every template has to render in both languages. A missing file or a missing
    /// key only shows up when the message is due to go out, which is to say on a
    /// real participant.
    /// </summary>
    [Fact]
    public async Task Здравната_проверка_потвърждава_всички_шаблони()
    {
        using var admin = await SignedInAdminAsync();

        using var response = await admin.Client.GetAsync("/Admin?handler=HealthCheck&service=templates");
        response.EnsureSuccessStatusCode();

        var el = System.Text.Json.JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("templates", el.GetProperty("key").GetString());
        Assert.Equal("ok", el.GetProperty("status").GetString());
    }

    // ════════════════════════════════════════════════════════════════════
    // Sending by hand
    // ════════════════════════════════════════════════════════════════════

    private static string Template(string marker) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"<html><body><p>{{Greeting}}</p><p>{marker}</p>" +
            "<p><a href=\"{BaseUrl}/Register\">{BaseUrl}/Register</a></p></body></html>"));

    private async Task<AdminReply> SendOneAsync(
        HttpSession admin, string to, string subject, string templateBase64, Guid batch)
    {
        if (!_batches.Contains(batch)) _batches.Add(batch);

        using var response = await admin.PostHandlerAsync("/Admin/SendInvitations", "SendOne",
            new Dictionary<string, string>
            {
                ["email"]              = to,
                ["name"]               = "Test Recipient",
                ["subject"]            = subject,
                ["htmlTemplateBase64"] = templateBase64,
                ["batchId"]            = batch.ToString()
            });

        return await AdminReply.ReadAsync(response);
    }

    [Fact]
    public async Task Поканата_тръгва_и_влиза_в_историята()
    {
        var to = $"{Tag}-{Guid.NewGuid():N}@example.test";
        var marker = Guid.NewGuid().ToString("N")[..10];
        var batch = Guid.NewGuid();

        using var admin = await SignedInAdminAsync();
        App.Smtp.Clear();

        var reply = await SendOneAsync(admin, to, "Покана за конференцията", Template(marker), batch);
        Assert.True(reply.Success, reply.Message);

        var mail = await App.Smtp.WaitForAsync(to, TimeSpan.FromSeconds(10));
        Assert.NotNull(mail);
        Assert.Equal("Покана за конференцията", mail!.Subject);
        Assert.Contains(marker, mail.Body);
        Assert.Contains("Dear Test Recipient,", mail.Body);

        var log = await App.Db.ReadAsync(db => db.InvitationSendLogs.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Email == to));

        Assert.NotNull(log);
        Assert.True(log!.Success);
        Assert.Equal(batch, log.BatchId);
        Assert.Equal(App.Credentials.AdminEmail, log.SentByEmail);
    }

    /// <summary>
    /// The links in the message point at the application rather than at localhost,
    /// which is the whole point of <c>{BaseUrl}</c>.
    /// </summary>
    [Fact]
    public async Task Адресите_в_поканата_сочат_към_приложението()
    {
        var to = $"{Tag}-{Guid.NewGuid():N}@example.test";
        var batch = Guid.NewGuid();

        using var admin = await SignedInAdminAsync();
        App.Smtp.Clear();

        await SendOneAsync(admin, to, "Покана", Template("x"), batch);

        var mail = await App.Smtp.WaitForAsync(to, TimeSpan.FromSeconds(10));
        Assert.NotNull(mail);
        Assert.Contains($"{App.BaseUrl}/Register", mail!.Body);
        Assert.DoesNotContain("{BaseUrl}", mail.Body);
    }

    [Theory]
    [InlineData("", "Тема", "recipient email")]
    [InlineData("not-an-email", "Тема", "Invalid recipient")]
    [InlineData("someone@example.test", "", "subject")]
    public async Task Невалидна_покана_се_отказва_с_обяснение(
        string to, string subject, string expected)
    {
        var batch = Guid.NewGuid();

        using var admin = await SignedInAdminAsync();
        var reply = await SendOneAsync(admin, to, subject, Template("x"), batch);

        Assert.False(reply.Success);
        Assert.Contains(expected, reply.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Шаблон_който_не_е_base64_се_отказва()
    {
        var batch = Guid.NewGuid();

        using var admin = await SignedInAdminAsync();
        var reply = await SendOneAsync(admin,
            $"{Tag}-{Guid.NewGuid():N}@example.test", "Тема", "това не е base64 !!!", batch);

        Assert.False(reply.Success);
        Assert.Contains("base64", reply.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Шаблон_без_нито_един_таг_се_отказва()
    {
        var batch = Guid.NewGuid();
        var plain = Convert.ToBase64String(Encoding.UTF8.GetBytes("просто текст, без нищо"));

        using var admin = await SignedInAdminAsync();
        var reply = await SendOneAsync(admin,
            $"{Tag}-{Guid.NewGuid():N}@example.test", "Тема", plain, batch);

        Assert.False(reply.Success);
        Assert.Contains("HTML", reply.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Твърде_дълга_тема_се_отказва()
    {
        var batch = Guid.NewGuid();

        using var admin = await SignedInAdminAsync();
        var reply = await SendOneAsync(admin,
            $"{Tag}-{Guid.NewGuid():N}@example.test",
            new string('т', 201), Template("x"), batch);

        Assert.False(reply.Success);
        Assert.Contains("too long", reply.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Отказаната_покана_също_влиза_в_историята()
    {
        var to = $"{Tag}-{Guid.NewGuid():N}@example.test";
        var batch = Guid.NewGuid();

        using var admin = await SignedInAdminAsync();
        await SendOneAsync(admin, to, "", Template("x"), batch);

        var log = await App.Db.ReadAsync(db => db.InvitationSendLogs.AsNoTracking()
            .FirstOrDefaultAsync(l => l.BatchId == batch));

        Assert.NotNull(log);
        Assert.False(log!.Success);
        Assert.Equal("Validation", log.ErrorCategory);
        Assert.NotNull(log.ErrorMessage);
    }

    [Fact]
    public async Task Историята_се_чете_от_панела()
    {
        var to = $"{Tag}-{Guid.NewGuid():N}@example.test";
        var batch = Guid.NewGuid();

        using var admin = await SignedInAdminAsync();
        await SendOneAsync(admin, to, "Покана", Template("x"), batch);

        using var response = await admin.Client.GetAsync("/Admin/SendInvitations?handler=History&take=50");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains(to, json);
        Assert.Contains("\"hasSentBody\":true", json.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Изпратеното_писмо_се_сваля_такова_каквото_е_тръгнало()
    {
        var to = $"{Tag}-{Guid.NewGuid():N}@example.test";
        var marker = Guid.NewGuid().ToString("N")[..10];
        var batch = Guid.NewGuid();

        using var admin = await SignedInAdminAsync();
        await SendOneAsync(admin, to, "Покана", Template(marker), batch);

        var log = await App.Db.ReadAsync(db => db.InvitationSendLogs.AsNoTracking()
            .FirstAsync(l => l.Email == to));

        using var response = await admin.Client.GetAsync(
            $"/Admin/SendInvitations?handler=DownloadSentEmail&id={log.Id}");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(marker, body);

        // The downloaded version is the CLEAN one: no tracking pixel and no
        // rewritten links.
        Assert.DoesNotContain("handler=Track", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"{App.BaseUrl}/Register", body);
    }

    [Fact]
    public async Task Сваляне_на_несъществуващо_писмо_е_404()
    {
        using var admin = await SignedInAdminAsync();

        using var response = await admin.Client.GetAsync(
            "/Admin/SendInvitations?handler=DownloadSentEmail&id=999999");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Участник_не_може_да_изпраща_покани()
    {
        var user = await NewParticipantAsync();
        using var session = await SignedInAsync(user);

        var token = await session.AntiforgeryTokenAsync("/Profile");

        using var client = session.NoRedirectClient();
        using var response = await client.PostAsync("/Admin/SendInvitations?handler=SendOne",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["email"]              = "victim@example.test",
                ["subject"]            = "Тема",
                ["htmlTemplateBase64"] = Template("x"),
                ["batchId"]            = Guid.NewGuid().ToString(),
                ["__RequestVerificationToken"] = token
            }));

        Assert.NotEqual(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
