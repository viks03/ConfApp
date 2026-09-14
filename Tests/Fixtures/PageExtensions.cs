// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using Microsoft.Playwright;

namespace ConferenceApp.Tests.Fixtures;

public static class PageExtensions
{
    /// <summary>
    /// Accepts the cookie notice.
    /// <para>
    /// The banner is <c>position: fixed</c> at the bottom of the window, and at
    /// 1440×900 it sits directly over the primary button on <c>/Register</c>. A
    /// person deals with the banner first and presses the button afterwards; the
    /// test does the same.
    /// </para>
    /// </summary>
    public static async Task AcceptCookieNoticeAsync(this IPage page)
    {
        var accept = page.Locator("#dnAcceptBtn");

        if (await accept.IsVisibleAsync())
            await accept.ClickAsync();

        await page.Locator("#dnBanner").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000
        });
    }
}
