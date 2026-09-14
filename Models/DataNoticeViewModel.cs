// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Models
{
    // Passed from _Layout.cshtml to Pages/Shared/_DataNotice.cshtml with the
    // language already resolved (IsBg), so that the partial does not work out
    // the culture itself, and with only the VISIBLE categories — IsVisible is
    // filtered before it gets here.
    public class DataNoticeViewModel
    {
        public List<CookieCategory> Categories { get; set; } = new();
        public string NoticeHtml { get; set; } = string.Empty;
        public bool IsBg { get; set; }

        // True if the visitor already has a valid saved choice (see the cookie
        // check in _Layout.cshtml). It lets the banner and the relaunch button
        // render in their FINAL state on the server, so a returning visitor
        // never sees the banner flash up and disappear.
        public bool HasExistingConsent { get; set; }
    }
}