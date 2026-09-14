// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Services.Schedule
{
    /// <summary>
    /// The single source for the session types in the programme.
    ///
    /// <para>
    /// <b>The values here are also the keys in</b>
    /// <c>Resources/Pages.Schedule.bg.resx</c> <b>and</b>
    /// <c>Pages.Schedule.en.resx</c>. The public programme translates the type
    /// with <c>@Localizer[item.SessionType]</c> — the only dynamic resource
    /// lookup in the project. It goes through
    /// <c>ResourceManager.GetString(name, culture)</c> without
    /// <c>ignoreCase</c>, so the key has to match <b>character for
    /// character</b>, capitals included. On a mismatch the localizer returns the
    /// key itself and the Bulgarian page shows the English string.
    /// </para>
    ///
    /// <para>
    /// So: a new type is added here <b>and</b> to both resx files. The dropdown
    /// in the admin panel (<c>Areas/Admin/Pages/Index.cshtml</c>,
    /// <c>#sessionType</c>) is built from <see cref="All"/> and must not be
    /// written out by hand.
    /// </para>
    /// </summary>
    public static class SessionTypes
    {
        public static readonly IReadOnlyList<string> All = new[]
        {
            "Registration",
            "Opening Ceremony",
            "Plenary Session",
            "Plenary",
            "Panel Discussion",
            "Panel",
            "Workshop",
            "Roundtable",
            "Networking",
            "Coffee Break",
            "Refreshment Break",
            "Lunch",
            "Dinner",
            "Farewell Lunch",
            "Closing Session"
        };

        /// <summary>
        /// The types the panel colours as a break rather than as a session
        /// proper. A subset of <see cref="All"/>; the comparison ignores case,
        /// because the value comes from the database and not necessarily from
        /// the dropdown.
        /// </summary>
        private static readonly HashSet<string> BreakTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Registration",
                "Coffee Break",
                "Refreshment Break",
                "Lunch",
                "Dinner",
                "Farewell Lunch"
            };

        public static bool IsBreak(string? sessionType) =>
            !string.IsNullOrEmpty(sessionType) && BreakTypes.Contains(sessionType);
    }
}
