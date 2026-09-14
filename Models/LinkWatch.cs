// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Models
{
    // The link to the live stream, shown on /Attend. A single row, created by
    // the admin panel the first time the link is saved — which is why every
    // reader takes FirstOrDefault and copes with there being no row at all.
    public class LinkWatch
    {
        public int Id { get; set; }

        // "#" until an administrator fills it in: a harmless href that goes
        // nowhere rather than a broken link.
        public string WatchOnlineLink { get; set; } = "#";
    }
}