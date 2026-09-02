using System.Text.Json.Serialization;

namespace ConferenceApp.Services.Health
{
    /// <summary>
    /// Състоянието на една услуга.
    /// <para>
    /// <see cref="Unconfigured"/> е отделно от <see cref="Fail"/> нарочно:
    /// празен ключ в конфигурацията не е същото като паднала услуга.
    /// Администраторът трябва да различи „забравена настройка“ от „проблем“.
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
    /// Резултат за една услуга. Имената на полетата в JSON са фиксирани от
    /// договора в INTEGRATION.md — adminPanelHealth.js чете точно тези ключове.
    /// Затова са изрично указани, а не оставени на конвенцията на сериализатора.
    /// </summary>
    public sealed class HealthResult
    {
        [JsonPropertyName("key")]        public string Key { get; init; } = string.Empty;
        [JsonPropertyName("name")]       public string Name { get; init; } = string.Empty;

        /// <summary>ok | warn | fail | unconfigured — винаги с малки букви.</summary>
        [JsonPropertyName("status")]     public string Status { get; init; } = "unknown";

        /// <summary>Едно изречение на човешки език. Без стек трейс.</summary>
        [JsonPropertyName("message")]    public string Message { get; init; } = string.Empty;

        /// <summary>Какво да се направи, или техническата причина.</summary>
        [JsonPropertyName("hint")]       public string? Hint { get; init; }

        [JsonPropertyName("responseMs")] public long? ResponseMs { get; init; }

        [JsonPropertyName("checkedAt")]  public DateTime CheckedAt { get; init; } = DateTime.UtcNow;

        [JsonPropertyName("details")]    public List<HealthDetail>? Details { get; init; }

        // ── Фабрики, за да не се пишат низовете "ok"/"warn" на ръка ────────

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

    /// <summary>Отговорът при проверка на всичките осем наведнъж (bulk режим).</summary>
    public sealed class HealthReport
    {
        [JsonPropertyName("checkedAt")] public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
        [JsonPropertyName("services")]  public List<HealthResult> Services { get; init; } = new();
    }
}
