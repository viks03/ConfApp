namespace ConferenceApp.Helpers
{
    // Всичко в базата се пази в UTC (DateTime.UtcNow) — правилно за storage,
    // но грешно за показване директно на човек. Този helper конвертира към
    // българско wall-clock време (автоматично борави с EET/EEST DST смяната),
    // за да не се налага всеки контролер/страница да си copy-paste-ва
    // TimeZoneInfo resolution логиката (виж и BugReportController.cs, който
    // прави абсолютно същото за download filename-а).
    public static class TimeZoneHelper
    {
        private static readonly TimeZoneInfo SofiaTimeZone = ResolveSofiaTimeZone();

        private static TimeZoneInfo ResolveSofiaTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Sofia"); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
            catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
        }

        /// <summary>Конвертира UTC DateTime към българско местно време.</summary>
        public static DateTime ToLocal(DateTime utc)
        {
            var safeUtc = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return TimeZoneInfo.ConvertTimeFromUtc(safeUtc, SofiaTimeZone);
        }

        /// <summary>Същото, но за nullable DateTime — удобно за "?.Value" полета.</summary>
        public static DateTime? ToLocal(DateTime? utc) => utc.HasValue ? ToLocal(utc.Value) : null;
    }
}