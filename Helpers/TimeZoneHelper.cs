// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Helpers
{
    // Everything in the database is stored in UTC (DateTime.UtcNow), which is
    // right for storage and wrong for showing to a person. This helper converts
    // to Bulgarian wall-clock time — the EET/EEST daylight-saving switch is
    // handled by the time zone itself — so that no controller or page has to
    // repeat the time zone lookup. SendInvitations.cshtml.cs still resolves the
    // zone itself, for the timestamp in a downloaded file name.
    public static class TimeZoneHelper
    {
        private static readonly TimeZoneInfo SofiaTimeZone = ResolveSofiaTimeZone();

        // Falls back to UTC rather than throwing: a host without the IANA time
        // zone database would otherwise take the whole site down at startup,
        // and a wrong-by-two-hours timestamp is the lesser failure.
        private static TimeZoneInfo ResolveSofiaTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Sofia"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
            catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
        }

        /// <summary>Converts a UTC DateTime to Bulgarian local time.</summary>
        public static DateTime ToLocal(DateTime utc)
        {
            var safeUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return TimeZoneInfo.ConvertTimeFromUtc(safeUtc, SofiaTimeZone);
        }

        /// <summary>The same for a nullable DateTime.</summary>
        public static DateTime? ToLocal(DateTime? utc) => utc.HasValue ? ToLocal(utc.Value) : null;
    }
}