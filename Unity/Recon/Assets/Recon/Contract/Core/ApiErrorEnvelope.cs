using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Recon.Contract
{
    /// <summary>
    /// The one error shape every route returns (web/src/lib/api.ts):
    /// <code>{ "error": { "code": "NOT_FOUND", "message": "...", "details"?: ... } }</code>
    /// Success bodies are the raw object instead, so the presence of an `error` object is what
    /// tells the two apart — the client never has to special-case per endpoint.
    /// </summary>
    public sealed class ApiErrorBody
    {
        /// <summary>One of BAD_REQUEST, UNAUTHORIZED, FORBIDDEN, NOT_FOUND, CONFLICT, PAYLOAD_TOO_LARGE, INTERNAL.</summary>
        [JsonProperty("code")] public string Code { get; set; }

        /// <summary>Human-readable, safe to put on screen. This is what the user sees when a scene is refused.</summary>
        [JsonProperty("message")] public string Message { get; set; }

        /// <summary>
        /// Free-form extra context; today it is the Zod issue list from
        /// web/src/lib/api.ts zodError(). Kept as a JToken because the client only ever logs it.
        /// </summary>
        [JsonProperty("details")] public JToken Details { get; set; }

        public override string ToString()
        {
            var text = (Code ?? "ERROR") + ": " + (Message ?? "(no message)");
            if (Details != null) text += " | details: " + Details.ToString(Formatting.None);
            return text;
        }
    }

    /// <summary>
    /// Detects and parses the error envelope. <see cref="TryParse"/> never throws: it is called on
    /// whatever bytes came back, which on a bad day is a proxy's HTML page or the first chunk of a
    /// .ply, and a parser crash there would hide the real failure.
    /// </summary>
    public static class ApiErrorEnvelope
    {
        public static bool TryParse(string json, out ApiErrorBody error)
        {
            error = null;

            var root = ContractJson.TryObject(json);
            if (root == null) return false;

            // `error` must be an object. A body with a string `error` is some other service's
            // convention, not ours, and misreading it would invent a code that does not exist.
            if (!(root["error"] is JObject errorObject)) return false;

            try
            {
                error = errorObject.ToObject<ApiErrorBody>();
            }
            catch (JsonException)
            {
                return false;
            }

            if (error == null) return false;
            // An empty object is not an error envelope in any useful sense.
            if (string.IsNullOrEmpty(error.Code) && string.IsNullOrEmpty(error.Message))
            {
                error = null;
                return false;
            }
            return true;
        }

        /// <summary>
        /// The message to show a user for a non-2xx body, falling back to the raw body when the
        /// response did not use the envelope at all (a crash before the handler, a reverse proxy).
        /// </summary>
        public static string DescribeFailure(long httpStatus, string body)
        {
            if (TryParse(body, out var err)) return err.ToString();
            var snippet = string.IsNullOrWhiteSpace(body)
                ? "(empty body)"
                : body.Trim().Substring(0, body.Trim().Length > 200 ? 200 : body.Trim().Length);
            return $"HTTP {httpStatus} with no error envelope: {snippet}";
        }
    }
}
