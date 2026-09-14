// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using ConferenceApp.Tests.Fixtures;
using Microsoft.Playwright;

namespace ConferenceApp.Tests.Admin;

/// <summary>
/// The tier dialog: the content grew — two descriptive paragraphs above the
/// prices and two Perks fields — while the frame has a ceiling on its height. The
/// test guards the three properties that broke: the buttons inside the frame,
/// nothing outside it, and the scrolling in the body rather than on the page.
///
/// <para>
/// The cause was structural: the <c>&lt;form&gt;</c> sat between
/// <c>.modal-box</c> (a flex column with a ceiling) and <c>.modal-body</c>
/// (<c>flex:1</c> with <c>overflow-y:auto</c>) as an ordinary block and broke the
/// chain — the body had nothing to shrink against.
/// </para>
/// </summary>
[Collection(AppCollection.Name)]
public class TierModalLayoutTests : AdminTestBase
{
    public TierModalLayoutTests(AppFixture app) : base(app, "ad-tier-ui") { }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1440, 620)]   // a short screen, where it shows most
    [InlineData(1280, 560)]
    public async Task Модалът_за_тарифа_се_побира_в_рамката(int w, int h)
    {
        using var session = await SignedInAdminAsync();
        await using var ctx = await App.NewBrowserContextAsync();
        await ctx.AddCookiesAsync(session.PlaywrightCookies());

        var page = await ctx.NewPageAsync();
        await page.SetViewportSizeAsync(w, h);
        await page.GotoAsync("/Admin");
        await page.ClickAsync(".admin-tab[data-target=\"tab-attend\"]");
        await page.ClickAsync("#tab-attend button[data-perks-bg]");
        await page.WaitForSelectorAsync("#edit-ticket-modal.active");

        var json = await page.EvaluateAsync<string>(@"() => {
            const box  = document.querySelector('#edit-ticket-modal .modal-box');
            const body = document.querySelector('#edit-ticket-modal .modal-body');
            const acts = document.querySelector('#edit-ticket-modal .modal-actions');
            const perks= document.querySelector('#ticketPerks_bg');
            const b = box.getBoundingClientRect(),
                  a = acts.getBoundingClientRect(),
                  p = perks.getBoundingClientRect();
            return JSON.stringify({
                boxTop: b.top, boxBottom: b.bottom, boxRight: b.right,
                actionsBottom: a.bottom,
                perksRight: p.right,
                bodyScroll: body.scrollHeight - body.clientHeight,
                viewportH: window.innerHeight,
                docOverflowX: document.documentElement.scrollWidth - document.documentElement.clientWidth
            });
        }");

        var m = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(json)!;

        Assert.True(m["actionsBottom"] <= m["boxBottom"] + 1,
            $"Бутоните излизат под рамката: actions={m["actionsBottom"]}, box={m["boxBottom"]}");

        Assert.True(m["boxBottom"] <= m["viewportH"] + 1,
            $"Рамката излиза под екрана: box={m["boxBottom"]}, viewport={m["viewportH"]}");

        Assert.True(m["perksRight"] <= m["boxRight"] + 1,
            $"„Perks (BG)“ излиза вдясно от рамката: perks={m["perksRight"]}, box={m["boxRight"]}");

        Assert.True(m["docOverflowX"] <= 0,
            $"Модалът вкарва хоризонтален скрол на страницата: {m["docOverflowX"]}px");

        // Once the frame has hit the ceiling, the scrolling has to be INSIDE the
        // body.
        if (m["boxBottom"] - m["boxTop"] >= m["viewportH"] - 49)
            Assert.True(m["bodyScroll"] > 0,
                "Съдържанието не се побира, а тялото не скролира — бутоните ще бъдат изтласкани.");
    }
}
