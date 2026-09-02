namespace ConferenceApp.Models
{
    // Singleton настройки таблица — винаги съществува точно ЕДИН ред
    // (Id = 1, seed-нат в ApplicationDbContext). Полетата по подразбиране
    // са null/празни — иконата на съответната мрежа просто не се
    // показва на фронтенда, докато admin не попълни линка.
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