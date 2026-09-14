// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
namespace ConferenceApp.Services.RemoteControl
{
    /// <summary>
    /// The <c>RemoteControl</c> section of the configuration — the client half
    /// of the remote accessibility switch.
    /// <para>
    /// An empty <see cref="Url"/> is the default and means the whole thing stays
    /// switched off: no background service is registered, no middleware is added
    /// to the pipeline, and not a single request leaves the machine. The feature
    /// costs nothing until somebody fills in an address.
    /// </para>
    /// <para>
    /// <see cref="Key"/> is the one value that never belongs in
    /// <c>appsettings.json</c>. It comes from the environment variable
    /// <c>RemoteControl__Key</c>, which the default configuration builder maps
    /// onto <c>RemoteControl:Key</c> and which overrides the file.
    /// </para>
    /// </summary>
    public sealed class RemoteControlOptions
    {
        public const string SectionName = "RemoteControl";

        /// <summary>
        /// The address of the control server, for example
        /// <c>https://94.156.92.175.nip.io</c>. Empty means switched off.
        /// </summary>
        public string? Url { get; set; }

        /// <summary>
        /// The shared secret, sent in the <c>X-Control-Key</c> header. From
        /// <c>RemoteControl__Key</c>, never from the file.
        /// </summary>
        public string? Key { get; set; }

        /// <summary>How often to ask the server. Default 15 seconds.</summary>
        public double PollSeconds { get; set; } = 15;

        /// <summary>
        /// How long to wait for an answer before giving up on this cycle.
        /// Default 5 seconds.
        /// </summary>
        public double TimeoutSeconds { get; set; } = 5;

        /// <summary>
        /// After how long without an answer the last known state stops counting
        /// and the site is shown again. Default 60 minutes.
        /// <para>
        /// A <c>double</c> rather than an <c>int</c> so that the same setting can
        /// be given in fractions of a minute — the documented value is 60, the
        /// tests need a window they can cross without waiting an hour.
        /// </para>
        /// </summary>
        public double StaleAfterMinutes { get; set; } = 60;

        /// <summary>Whether an address was given at all, sound or not.</summary>
        public bool HasUrl => !string.IsNullOrWhiteSpace(Url);

        /// <summary>
        /// Whether any of this runs. An address that is not an address does not
        /// count: a typo in a setting must not become a background service that
        /// throws on its first line.
        /// </summary>
        public bool Enabled => TryGetStatusUri(out _);

        /// <summary>
        /// The address of <c>/status</c>. A URL that already points at the
        /// endpoint is left alone, so both spellings in configuration work.
        /// <para>
        /// Returns false instead of throwing. The alternative is an exception
        /// inside <see cref="RemoteControlPoller"/>, and an unhandled exception
        /// in a hosted service stops the host — which would mean a mistyped
        /// address takes down the conference site. That is the exact opposite of
        /// what this whole feature is for.
        /// </para>
        /// </summary>
        public bool TryGetStatusUri(out Uri? statusUri)
        {
            statusUri = null;

            var baseUrl = (Url ?? string.Empty).Trim().TrimEnd('/');
            if (baseUrl.Length == 0) return false;

            if (!baseUrl.EndsWith("/status", StringComparison.OrdinalIgnoreCase))
                baseUrl += "/status";

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed)) return false;

            // Only the two schemes an HttpClient can actually fetch. file:// and
            // the rest are a misunderstanding, not a control server.
            if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
                return false;

            statusUri = parsed;
            return true;
        }

        // The three numbers below are clamped rather than validated into a
        // startup failure. A typo in an interval is not a reason to keep the
        // conference site down, and the switch is meant to be the least
        // dangerous part of the application.

        /// <summary>At least a second between two polls; a nonsense value falls back to 15.</summary>
        public TimeSpan PollInterval =>
            PollSeconds >= 1 ? TimeSpan.FromSeconds(PollSeconds) : TimeSpan.FromSeconds(15);

        /// <summary>At least a second of patience; a nonsense value falls back to 5.</summary>
        public TimeSpan Timeout =>
            TimeoutSeconds >= 1 ? TimeSpan.FromSeconds(TimeoutSeconds) : TimeSpan.FromSeconds(5);

        /// <summary>A negative window would hide the site forever, so it reads as zero.</summary>
        public TimeSpan StaleAfter =>
            StaleAfterMinutes > 0 ? TimeSpan.FromMinutes(StaleAfterMinutes) : TimeSpan.Zero;
    }
}
