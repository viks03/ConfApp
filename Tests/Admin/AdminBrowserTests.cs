// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;
using Microsoft.Playwright;

namespace ConferenceApp.Tests.Admin;

/// <summary>
/// Part 6 through a browser: what a person does with a mouse — walks through
/// every tab, searches the lists, confirms a payment from the screen.
/// <para>
/// The search, the filters, switching between tabs, the dot beside Changelog and
/// the cards in Health Check exist only on the client. An HTTP test sees the
/// attributes but not whether the script uses them.
/// </para>
/// </summary>
public class AdminBrowserTests : AdminTestBase
{
    public AdminBrowserTests(AppFixture app) : base(app, "ad-ui") { }

    /// <summary>
    /// A browser with an administrator already signed in. The sign-in happens in an
    /// HTTP session and the cookies are carried across; typing the password itself
    /// is part 3.
    /// </summary>
    private async Task<IBrowserContext> AdminContextAsync()
    {
        using var session = await SignedInAdminAsync();

        var context = await App.NewBrowserContextAsync();
        await context.AddCookiesAsync(session.PlaywrightCookies());
        return context;
    }

    private static async Task<(IPage Page, List<string> Errors)> OpenPanelAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();

        var errors = new List<string>();
        page.Console += (_, msg) => { if (msg.Type == "error") errors.Add(msg.Text); };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/Admin");
        await page.AcceptCookieNoticeAsync();

        return (page, errors);
    }

    // ════════════════════════════════════════════════════════════════════
    // The tabs
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Всеки_таб_се_отваря_без_грешка_в_конзолата()
    {
        await using var context = await AdminContextAsync();
        var (page, errors) = await OpenPanelAsync(context);

        // Some of the buttons in the bar are links — Bug reports, Invitations — and
        // they carry no data-target: they are not tabs.
        var targets = (await page.Locator(".admin-tab").EvaluateAllAsync<string[]>(
                "els => els.map(e => e.dataset.target || '')"))
            .Where(t => t.Length > 0)
            .Distinct()
            .ToArray();

        Assert.True(targets.Length >= 20, $"Табовете са {targets.Length}, а се очакват поне двайсет.");

        foreach (var target in targets)
        {
            await page.ClickAsync($".admin-tab[data-target=\"{target}\"]");

            var section = page.Locator($"#{target}");
            await Assertions.Expect(section).ToHaveClassAsync(
                new System.Text.RegularExpressions.Regex(@"\bactive\b"));

            Assert.True(await section.IsVisibleAsync(), $"Таб „{target}“ се отвори празен.");
        }

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Отвореният_таб_се_помни_след_презареждане()
    {
        await using var context = await AdminContextAsync();
        var (page, _) = await OpenPanelAsync(context);

        await page.ClickAsync(".admin-tab[data-target=\"tab-themes\"]");
        await page.ReloadAsync();

        await Assertions.Expect(page.Locator("#tab-themes")).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex(@"\bactive\b"));
    }

    // ════════════════════════════════════════════════════════════════════
    // Search and filters
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Търсенето_в_участниците_стеснява_списъка()
    {
        var mine  = await NewParticipantAsync();
        var other = await NewParticipantAsync();

        await using var context = await AdminContextAsync();
        var (page, errors) = await OpenPanelAsync(context);

        await page.ClickAsync(".admin-tab[data-target=\"tab-registrations\"]");

        var mineRow  = page.Locator($".reg-data-row:has-text(\"{mine.Email}\")");
        var otherRow = page.Locator($".reg-data-row:has-text(\"{other.Email}\")");

        await Assertions.Expect(mineRow).ToBeVisibleAsync();
        await Assertions.Expect(otherRow).ToBeVisibleAsync();

        await page.FillAsync("#regSearchInput", mine.Email!);

        await Assertions.Expect(mineRow).ToBeVisibleAsync();
        await Assertions.Expect(otherRow).ToBeHiddenAsync();

        // An empty search brings everything back.
        await page.FillAsync("#regSearchInput", "");
        await Assertions.Expect(otherRow).ToBeVisibleAsync();

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Филтърът_по_статус_на_плащане_скрива_останалите()
    {
        var paid   = await NewParticipantAsync(paymentStatus: "Confirmed");
        var unpaid = await NewParticipantAsync(paymentStatus: "Pending");

        await using var context = await AdminContextAsync();
        var (page, _) = await OpenPanelAsync(context);

        await page.ClickAsync(".admin-tab[data-target=\"tab-registrations\"]");
        await page.SelectOptionAsync("#regPaymentFilter", "Confirmed");

        await Assertions.Expect(page.Locator($".reg-data-row:has-text(\"{paid.Email}\")")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator($".reg-data-row:has-text(\"{unpaid.Email}\")")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Филтърът_по_форма_на_участие_работи()
    {
        var student = await NewParticipantAsync("2");
        var lector  = await NewParticipantAsync("1");

        await using var context = await AdminContextAsync();
        var (page, _) = await OpenPanelAsync(context);

        await page.ClickAsync(".admin-tab[data-target=\"tab-registrations\"]");
        await page.SelectOptionAsync("#regTypeFilter", "2");

        await Assertions.Expect(page.Locator($".reg-data-row:has-text(\"{student.Email}\")")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator($".reg-data-row:has-text(\"{lector.Email}\")")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Двата_филтъра_заедно_се_допълват()
    {
        var wanted  = await NewParticipantAsync("2", paymentStatus: "Confirmed");
        var wrongPay = await NewParticipantAsync("2", paymentStatus: "Pending");

        await using var context = await AdminContextAsync();
        var (page, _) = await OpenPanelAsync(context);

        await page.ClickAsync(".admin-tab[data-target=\"tab-registrations\"]");
        await page.SelectOptionAsync("#regTypeFilter", "2");
        await page.SelectOptionAsync("#regPaymentFilter", "Confirmed");

        await Assertions.Expect(page.Locator($".reg-data-row:has-text(\"{wanted.Email}\")")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator($".reg-data-row:has-text(\"{wrongPay.Email}\")")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Търсенето_в_одита_стеснява_списъка()
    {
        var user = await NewParticipantAsync();

        using (var admin = await SignedInAdminAsync())
            await PostAsync(admin, "ConfirmPayment", new Dictionary<string, string>
            {
                ["userId"] = user.Id, ["method"] = "IBAN"
            });

        await using var context = await AdminContextAsync();
        var (page, errors) = await OpenPanelAsync(context);

        await page.ClickAsync(".admin-tab[data-target=\"tab-audit\"]");

        var before = await page.Locator(".audit-data-row:visible").CountAsync();
        Assert.True(before > 0, "Одитът е празен — няма какво да се филтрира.");

        await page.FillAsync("#auditSearchInput", user.Email!);

        var after = await page.Locator(".audit-data-row:visible").CountAsync();
        Assert.True(after > 0, "Търсенето по имейл на участник не намери нищо.");
        Assert.True(after < before, "Търсенето не стесни списъка.");

        Assert.Empty(errors);
    }

    // ════════════════════════════════════════════════════════════════════
    // An action taken from the screen
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Плащането_се_потвърждава_от_екрана()
    {
        var user = await NewParticipantAsync();
        await App.Db.MarkIbanSubmittedAsync(user.Email!);

        await using var context = await AdminContextAsync();
        var (page, errors) = await OpenPanelAsync(context);

        // The confirmation goes through the browser's confirm().
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();

        await page.ClickAsync(".admin-tab[data-target=\"tab-payments\"]");
        await page.ClickAsync($".confirm-payment-btn[data-userid=\"{user.Id}\"]");

        // The panel reloads after a success, so what is waited for is the reload
        // itself rather than the address, which does not change.
        await page.WaitForFunctionAsync(
            "id => !document.querySelector(`.confirm-payment-btn[data-userid=\"${id}\"]`)",
            user.Id, new PageWaitForFunctionOptions { Timeout = 20000 });

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.Equal("Confirmed", after!.PaymentStatus);
        Assert.Equal("IBAN", after.PaymentMethod);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Отказът_в_диалога_не_променя_нищо()
    {
        var user = await NewParticipantAsync();
        await App.Db.MarkIbanSubmittedAsync(user.Email!);

        await using var context = await AdminContextAsync();
        var (page, _) = await OpenPanelAsync(context);

        page.Dialog += async (_, dialog) => await dialog.DismissAsync();

        await page.ClickAsync(".admin-tab[data-target=\"tab-payments\"]");
        await page.ClickAsync($".confirm-payment-btn[data-userid=\"{user.Id}\"]");

        await page.WaitForTimeoutAsync(1500);

        var after = await App.Db.FindUserAsync(user.Email!);
        Assert.NotEqual("Confirmed", after!.PaymentStatus);
    }

    // ════════════════════════════════════════════════════════════════════
    // Health Check
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Health_рисува_карта_за_всяка_проверка()
    {
        await using var context = await AdminContextAsync();
        var (page, errors) = await OpenPanelAsync(context);

        await page.ClickAsync(".admin-tab[data-target=\"tab-health\"]");

        // The probes start when the tab is opened.
        await page.WaitForSelectorAsync(".hc-card", new PageWaitForSelectorOptions { Timeout = 30000 });

        foreach (var key in new[]
                 {
                     "database", "smtp", "stripe", "go28",
                     "emailQueue", "cleanup", "disk", "backups", "templates"
                 })
        {
            var card = page.Locator($".hc-card[data-service=\"{key}\"]");
            await Assertions.Expect(card).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
        }

        // No card may be left saying the probe is still running.
        await Assertions.Expect(page.Locator(".hc-card-message", new PageLocatorOptions
        {
            HasTextString = "Проверката тече"
        })).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 30000 });

        Assert.Empty(errors);
    }

    /// <summary>
    /// The button for a copy on demand: it exists only on the backups card, it
    /// wears the same clothes as "Провери" beside it, and it refuses a second
    /// press while it is working.
    /// <para>
    /// This is the near half of the double-click guard — the far half, the gate
    /// in the runner, is in <see cref="BackupsTests"/>. Both are needed: a
    /// disabled button is not a promise, only a courtesy.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Health_прави_копие_от_бутона_в_картата()
    {
        await using var context = await AdminContextAsync();
        var (page, errors) = await OpenPanelAsync(context);

        // The copy this test causes is a real file in the repository's backups
        // folder, named after the test database (test_*.db). It is cleared away
        // at the end.
        var backupFolder = Path.Combine(TestPaths.RepoRoot, "backups");
        var before = BackupsIn(backupFolder);

        await page.ClickAsync(".admin-tab[data-target=\"tab-health\"]");
        await page.WaitForSelectorAsync(".hc-card", new PageWaitForSelectorOptions { Timeout = 30000 });

        var card   = page.Locator(".hc-card[data-service=\"backups\"]");
        var button = card.Locator(".hc-backup-btn");

        await Assertions.Expect(button).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });

        // The same look as "Провери": the class the styling hangs on, not a new
        // one invented for this button.
        await Assertions.Expect(button).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("hc-refresh-btn"));

        // No other card has one.
        await Assertions.Expect(page.Locator(".hc-backup-btn")).ToHaveCountAsync(1);

        // The card has to have finished its first check, or "Провери" is still
        // disabled from that and the assertion below would prove nothing.
        await Assertions.Expect(card.Locator(".hc-refresh-btn:not(.hc-backup-btn)"))
            .ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 30000 });

        // Two presses, both in the same turn of the browser's event loop — the
        // fastest a double click can possibly be, and faster than a person can
        // manage. Dispatching them from one script rather than clicking twice
        // keeps the test from depending on how long a copy happens to take.
        await card.EvaluateAsync(
            "el => { const b = el.querySelector('.hc-backup-btn'); b.click(); b.click(); }");

        // The card re-reads the folder by itself when the copy is done — nobody
        // presses "Провери".
        await Assertions.Expect(card).ToHaveAttributeAsync("data-status", "ok",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 30000 });

        // The button comes back to itself rather than staying stuck as busy.
        await Assertions.Expect(button).ToBeEnabledAsync(
            new LocatorAssertionsToBeEnabledOptions { Timeout = 15000 });
        await Assertions.Expect(button).ToContainTextAsync("Копие сега");

        // The newest copy on the card is the one just made.
        await Assertions.Expect(card.Locator(".hc-card-facts")).ToContainTextAsync("test_");

        // And the two presses left ONE copy, not two.
        var added = BackupsIn(backupFolder).Except(before).ToList();
        Assert.Single(added);

        Assert.Empty(errors);

        foreach (var path in added)
        {
            try { File.Delete(path); } catch { /* left for the next run */ }
        }
    }


    private static string[] BackupsIn(string folder) =>
        Directory.Exists(folder)
            ? ConferenceApp.Services.DatabaseLocation
                .ListAutomaticBackups(folder, TestPaths.TestDbFile)
                .Select(f => f.FullName).ToArray()
            : Array.Empty<string>();

    // ════════════════════════════════════════════════════════════════════
    // Changelog
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The "something new" dot goes out when the tab is OPENED rather than when the
    /// panel loads; otherwise the notice disappears before anyone has read what is
    /// new.
    /// </summary>
    [Fact]
    public async Task Точката_до_Changelog_гасне_при_отваряне_на_таба()
    {
        await using var context = await AdminContextAsync();
        var (page, errors) = await OpenPanelAsync(context);

        // A fresh browser: nothing has been seen yet, so the dot has to be lit.
        var dot = page.Locator("#cl-dot");
        await Assertions.Expect(dot).ToBeVisibleAsync();

        // Opening a different tab does not put it out.
        await page.ClickAsync(".admin-tab[data-target=\"tab-themes\"]");
        await Assertions.Expect(dot).ToBeVisibleAsync();

        await page.ClickAsync(".admin-tab[data-target=\"tab-changelog\"]");
        await Assertions.Expect(dot).ToBeHiddenAsync();

        // And it stays out after a reload: the preference lives in the browser.
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#cl-dot")).ToBeHiddenAsync();

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Друг_браузър_още_вижда_точката()
    {
        await using (var first = await AdminContextAsync())
        {
            var (page, _) = await OpenPanelAsync(first);
            await page.ClickAsync(".admin-tab[data-target=\"tab-changelog\"]");
            await Assertions.Expect(page.Locator("#cl-dot")).ToBeHiddenAsync();
        }

        // The preference is in localStorage rather than in the database, so a new
        // context means a new browser and the notice is waiting there.
        await using var second = await AdminContextAsync();
        var (other, _) = await OpenPanelAsync(second);

        await Assertions.Expect(other.Locator("#cl-dot")).ToBeVisibleAsync();
    }

    // ════════════════════════════════════════════════════════════════════
    // Themes and styles from the screen
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Темите_се_изброяват_в_своя_таб()
    {
        await using var context = await AdminContextAsync();
        var (page, errors) = await OpenPanelAsync(context);

        await page.ClickAsync(".admin-tab[data-target=\"tab-themes\"]");

        var onDisk = Directory
            .GetFiles(Path.Combine(TestPaths.RepoRoot, "wwwroot", "themes"), "*.json")
            .Length;

        // The row with an empty key is "no theme", which is not a theme.
        var shown = await page.Locator("#tab-themes .th-row[data-th-key]:not([data-th-key=\"\"])")
            .CountAsync();
        Assert.True(shown >= onDisk,
            $"В таба се виждат {shown} теми, а във wwwroot/themes има {onDisk} файла.");

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Списъкът_със_стилове_показва_всяка_страница()
    {
        await using var context = await AdminContextAsync();
        var (page, errors) = await OpenPanelAsync(context);

        await page.ClickAsync(".admin-tab[data-target=\"tab-styles\"]");

        foreach (var key in new[] { "/Index", "/Travel", "/Profile", "/Privacy" })
            await Assertions.Expect(page.Locator($"#tab-styles :text(\"{key}\")").First)
                .ToBeVisibleAsync();

        Assert.Empty(errors);
    }
}
