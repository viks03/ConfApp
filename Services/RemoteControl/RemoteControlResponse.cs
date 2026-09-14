// ─────────────────────────────────────────────────────────────────────
//  ConferenceApp · Blockchain Education 2026
//  Author: Viktor Georgiev
// ─────────────────────────────────────────────────────────────────────
using System.Text.Json;

namespace ConferenceApp.Services.RemoteControl
{
    /// <summary>
    /// The body of <c>GET /status</c>, and the one place that decides whether an
    /// answer counts.
    /// <para>
    /// The default is "visible". An empty, incomplete, unintelligible or
    /// unexpected body does not become a state — it is dropped, and the site
    /// goes on working. To hide the site somebody has to send a <b>valid</b>
    /// answer, over the right address, with the right secret. A tampered or
    /// mangled one cannot stop the conference.
    /// </para>
    /// <para>
    /// Which is why the reading is strict rather than forgiving: every property
    /// has to be one this contract knows, and <c>visible</c> and <c>mode</c> have
    /// to be there and make sense. A field nobody recognises means the thing on
    /// the other end is not the control server the application was written
    /// against, and a switch for the whole site is the wrong place to guess.
    /// The rejected field is named in the log, so a mismatch between the two
    /// halves is something you read rather than something you hunt for.
    /// </para>
    /// </summary>
    public static class RemoteControlResponse
    {
        // The full contract from the specification. changedAt and changedBy are
        // for the audit on the server; ConfApp does not use them, but they are
        // part of a valid answer and so they are tolerated.
        private static readonly string[] KnownProperties =
        {
            "visible", "mode", "messageBg", "messageEn", "changedAt", "changedBy"
        };

        /// <summary>The biggest body worth reading. A status answer is a few hundred bytes.</summary>
        public const int MaxBodyBytes = 64 * 1024;

        /// <summary>
        /// Reads the body. Returns false — with a reason fit for a log line —
        /// for anything that is not exactly what the control server promises.
        /// Never throws.
        /// </summary>
        public static bool TryParse(
            string? body,
            out bool visible,
            out string mode,
            out string messageBg,
            out string messageEn,
            out string failure)
        {
            visible   = true;
            mode      = RemoteControlModes.Maintenance;
            messageBg = string.Empty;
            messageEn = string.Empty;
            failure   = string.Empty;

            if (string.IsNullOrWhiteSpace(body))
            {
                failure = "празен отговор";
                return false;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(body);
            }
            catch (JsonException ex)
            {
                failure = "отговорът не е JSON: " + ex.Message;
                return false;
            }

            using (document)
            {
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    failure = "отговорът не е обект, а " + root.ValueKind;
                    return false;
                }

                bool?   parsedVisible = null;
                string? parsedMode    = null;

                foreach (var property in root.EnumerateObject())
                {
                    var name = KnownProperties.FirstOrDefault(
                        known => string.Equals(known, property.Name, StringComparison.OrdinalIgnoreCase));

                    if (name is null)
                    {
                        failure = $"непознато поле „{property.Name}“";
                        return false;
                    }

                    switch (name)
                    {
                        case "visible":
                            if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                            {
                                failure = "полето „visible“ не е булево";
                                return false;
                            }
                            parsedVisible = property.Value.GetBoolean();
                            break;

                        case "mode":
                            if (property.Value.ValueKind != JsonValueKind.String)
                            {
                                failure = "полето „mode“ не е низ";
                                return false;
                            }
                            parsedMode = property.Value.GetString();
                            break;

                        case "messageBg":
                            if (!TryReadText(property.Value, out messageBg))
                            {
                                failure = "полето „messageBg“ не е низ";
                                return false;
                            }
                            break;

                        case "messageEn":
                            if (!TryReadText(property.Value, out messageEn))
                            {
                                failure = "полето „messageEn“ не е низ";
                                return false;
                            }
                            break;

                        // changedAt and changedBy belong to the server's audit.
                        // They are accepted and ignored.
                    }
                }

                if (parsedVisible is null)
                {
                    failure = "липсва поле „visible“";
                    return false;
                }

                if (!RemoteControlModes.IsKnown(parsedMode))
                {
                    failure = parsedMode is null
                        ? "липсва поле „mode“"
                        : $"непознат режим „{parsedMode}“";
                    return false;
                }

                visible = parsedVisible.Value;
                mode    = parsedMode!.ToLowerInvariant();
                return true;
            }
        }

        /// <summary>A message may be a string or null; null reads as "not given".</summary>
        private static bool TryReadText(JsonElement element, out string text)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    text = element.GetString() ?? string.Empty;
                    return true;
                case JsonValueKind.Null:
                    text = string.Empty;
                    return true;
                default:
                    text = string.Empty;
                    return false;
            }
        }
    }
}
