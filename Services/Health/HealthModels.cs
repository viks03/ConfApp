// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text.Json.Serialization;

namespace ConferenceApp.Services.Health
{
    /// <summary>
    /// The state of one service.
    /// <para>
    /// <see cref="Unconfigured"/> is deliberately separate from
    /// <see cref="Fail"/>: an empty key in the configuration is not the same as
    /// a service that is down. The administrator has to be able to tell a
    /// forgotten setting from a fault.
    /// </para>
    /// </summary>
    public enum HealthState
    {
        Ok,
        Warn,
        Fail,
        Unconfigured
    }

    public sealed class HealthDetail
    {
        [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
        [JsonPropertyName("value")] public string Value { get; init; } = string.Empty;

        public HealthDetail() { }
        public HealthDetail(string label, string value) { Label = label; Value = value; }
    }

    /// <summary>
    /// The result for one service. The JSON field names are fixed by the
    /// contract in INTEGRATION.md — adminPanelHealth.js reads exactly these
    /// keys. They are therefore spelled out rather than left to the
    /// serializer's naming convention.
    /// </summary>
    public sealed class HealthResult
    {
        [JsonPropertyName("key")]        public string Key { get; init; } = string.Empty;
        [JsonPropertyName("name")]       public string Name { get; init; } = string.Empty;

        /// <summary>ok | warn | fail | unconfigured — always lower case, which
        /// is what the panel's CSS classes are keyed on.</summary>
        [JsonPropertyName("status")]     public string Status { get; init; } = "unknown";

        /// <summary>One sentence in plain language. Never a stack trace: the
        /// panel shows this text as it is.</summary>
        [JsonPropertyName("message")]    public string Message { get; init; } = string.Empty;

        /// <summary>What to do about it, or the technical reason.</summary>
        [JsonPropertyName("hint")]       public string? Hint { get; init; }

        [JsonPropertyName("responseMs")] public long? ResponseMs { get; init; }

        [JsonPropertyName("checkedAt")]  public DateTime CheckedAt { get; init; } = DateTime.UtcNow;

        [JsonPropertyName("details")]    public List<HealthDetail>? Details { get; init; }

        // ── A factory, so that the "ok"/"warn" strings are written once ────

        public static HealthResult Create(
            string key, string name, HealthState state, string message,
            string? hint = null, long? responseMs = null, List<HealthDetail>? details = null)
            => new()
            {
                Key = key,
                Name = name,
                Status = state switch
                {
                    HealthState.Ok           => "ok",
                    HealthState.Warn         => "warn",
                    HealthState.Fail         => "fail",
                    HealthState.Unconfigured => "unconfigured",
                    _                        => "unknown"
                },
                Message = message,
                Hint = hint,
                ResponseMs = responseMs,
                CheckedAt = DateTime.UtcNow,
                Details = details is { Count: > 0 } ? details : null
            };
    }

    /// <summary>The response when every service is checked at once.</summary>
    public sealed class HealthReport
    {
        [JsonPropertyName("checkedAt")] public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
        [JsonPropertyName("services")]  public List<HealthResult> Services { get; init; } = new();
    }
}
