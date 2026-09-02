// Models/ConferenceSetting.cs
namespace ConferenceApp.Models
{
    public class ConferenceSetting
    {
        public int Id { get; set; }

        // Стойността по подразбиране е "#", точно както искаш
        public string WatchOnlineLink { get; set; } = "#";

    }
}