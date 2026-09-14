// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Models
{
    // A settings table with exactly ONE row (Id = 1, seeded in
    // ApplicationDbContext). The fields start empty, and the icon for a network
    // is simply not shown on the site until an administrator fills its link
    // in.
    public class SocialLinksSetting
    {
        public int Id { get; set; }

        public string? LinkedInUrl { get; set; }
        public string? XUrl { get; set; }
        public string? InstagramUrl { get; set; }
        public string? FacebookUrl { get; set; }
        public string? TikTokUrl { get; set; }
        public string? YouTubeUrl { get; set; }
    }
}