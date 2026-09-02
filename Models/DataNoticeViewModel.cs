namespace ConferenceApp.Models
{
    // Подавана от _Layout.cshtml към Pages/Shared/_DataNotice.cshtml — вече
    // резолвнат език (IsBg), за да партиалът не пресмята культура сам, и
    // само ВИДИМИТЕ категории (IsVisible филтрирано преди да стигне тук).
    public class DataNoticeViewModel
    {
        public List<CookieCategory> Categories { get; set; } = new();
        public string NoticeHtml { get; set; } = string.Empty;
        public bool IsBg { get; set; }

        // True if the visitor already has a valid saved choice (see
        // _Layout.cshtml's cookie check) — lets the banner/relaunch button
        // render in their FINAL state server-side, so there's no flash of
        // the banner appearing then disappearing for returning visitors.
        public bool HasExistingConsent { get; set; }
    }
}