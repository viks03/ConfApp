// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Services.RemoteControl
{
    /// <summary>The three modes the control server may ask for, and nothing else.</summary>
    public static class RemoteControlModes
    {
        /// <summary>A planned stop. 503 — search engines keep the site.</summary>
        public const string Maintenance = "maintenance";

        /// <summary>A problem nobody has understood yet. 500.</summary>
        public const string Error = "error";

        /// <summary>
        /// The site is to look as though there is nothing here. 404 — and the
        /// visitor is told nothing beyond that, not even that somebody switched
        /// it off.
        /// </summary>
        public const string NotFound = "notfound";

        /// <summary>
        /// A mode outside these three is treated as an answer that cannot be
        /// understood — <c>403</c> says something else entirely, and so does
        /// anything this contract was not written against.
        /// </summary>
        public static bool IsKnown(string? mode) =>
            string.Equals(mode, Maintenance, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, Error,       StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, NotFound,    StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Maintenance is the fallback rather than a case of its own: a mode that
        /// reaches here is one <see cref="IsKnown"/> has already let through.
        /// </summary>
        public static int StatusCodeFor(string? mode)
        {
            if (string.Equals(mode, Error, StringComparison.OrdinalIgnoreCase))
                return StatusCodes.Status500InternalServerError;

            if (string.Equals(mode, NotFound, StringComparison.OrdinalIgnoreCase))
                return StatusCodes.Status404NotFound;

            return StatusCodes.Status503ServiceUnavailable;
        }
    }

    /// <summary>
    /// What the last understood answer from the control server said. Immutable
    /// on purpose: the background service does not edit this object, it replaces
    /// it, so a request that is reading it never sees a half-written value and
    /// never takes a lock.
    /// </summary>
    /// <param name="LastSuccessAt">
    /// When that answer arrived, in UTC. This is the clock the silence rules are
    /// measured against.
    /// </param>
    public sealed record RemoteControlSnapshot(
        bool     Visible,
        string   Mode,
        string   MessageBg,
        string   MessageEn,
        DateTime LastSuccessAt);

    /// <summary>What the middleware should do with the request in front of it.</summary>
    /// <param name="Hide">Whether to stop the request and show the page instead.</param>
    /// <param name="StatusCode">503, 500 or 404, from <see cref="RemoteControlSnapshot.Mode"/>.</param>
    public readonly record struct RemoteControlDecision(
        bool   Hide,
        int    StatusCode,
        string MessageBg,
        string MessageEn)
    {
        public static readonly RemoteControlDecision Show =
            new(false, StatusCodes.Status200OK, string.Empty, string.Empty);
    }

    /// <summary>
    /// The state in memory: one object, read without a lock.
    /// <para>
    /// Singleton. <see cref="RemoteControlPoller"/> writes it, the middleware
    /// reads it on every request. Both go through <see cref="Volatile"/> on a
    /// single reference field, which is all the synchronisation an immutable
    /// snapshot needs — a reader gets either the old answer or the new one,
    /// never a mixture.
    /// </para>
    /// </summary>
    public sealed class RemoteControlState
    {
        private readonly TimeProvider _clock;

        // Null until the first answer that could be understood. Null means
        // "nobody has ever told us to hide", which is the default the whole
        // feature is built around: visible.
        private RemoteControlSnapshot? _current;

        public RemoteControlState(TimeProvider clock) => _clock = clock;

        /// <summary>The last understood answer, or null if there has not been one.</summary>
        public RemoteControlSnapshot? Current => Volatile.Read(ref _current);

        /// <summary>
        /// Records an answer that parsed. Called only by the background service,
        /// and only for a response that passed every check in
        /// <see cref="RemoteControlResponse"/>.
        /// </summary>
        public RemoteControlSnapshot Report(bool visible, string mode, string messageBg, string messageEn)
        {
            var snapshot = new RemoteControlSnapshot(
                visible, mode, messageBg, messageEn, _clock.GetUtcNow().UtcDateTime);

            Volatile.Write(ref _current, snapshot);
            return snapshot;
        }

        /// <summary>
        /// The whole decision, in one place, so that the middleware holds no
        /// rules of its own and the rules can be tested without an HTTP request.
        /// </summary>
        /// <param name="staleAfter">
        /// How long the last answer stays good. Past it the site is shown again
        /// — deliberately: it is far likelier that the control server has fallen
        /// over than that somebody wants the site hidden and cannot reach it.
        /// </param>
        public RemoteControlDecision Decide(TimeSpan staleAfter)
        {
            var snapshot = Current;

            // Never heard from, or heard "visible" — either way the site works.
            if (snapshot is null || snapshot.Visible)
                return RemoteControlDecision.Show;

            var silence = _clock.GetUtcNow().UtcDateTime - snapshot.LastSuccessAt;
            if (silence > staleAfter)
                return RemoteControlDecision.Show;

            return new RemoteControlDecision(
                true,
                RemoteControlModes.StatusCodeFor(snapshot.Mode),
                snapshot.MessageBg,
                snapshot.MessageEn);
        }
    }
}
