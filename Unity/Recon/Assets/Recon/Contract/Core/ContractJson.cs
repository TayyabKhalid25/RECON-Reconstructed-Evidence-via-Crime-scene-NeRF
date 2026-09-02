using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Recon.Contract
{
    /// <summary>
    /// Thrown when a response parses as JSON but is not the shape docs/API.md promises — a
    /// missing wrapper key, a login without a token. Distinct from Newtonsoft's
    /// <see cref="JsonException"/> (malformed JSON) and from Recon.Api.ApiException (the server
    /// said no, in the standard envelope), because the three have different fixes: fix the
    /// client, fix the server, or show the user the server's message.
    /// </summary>
    public sealed class ReconContractException : Exception
    {
        public ReconContractException(string message) : base(message) { }
        public ReconContractException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Shared deserialisation for the DTOs in this folder. One place that turns "the body was
    /// not what the contract says" into a readable message, so every DTO fails the same way.
    /// Pure C#: no UnityEngine, so it compiles under `dotnet test Unity/Recon.Core.Tests`.
    /// </summary>
    internal static class ContractJson
    {
        /// <summary>
        /// Parses <paramref name="json"/> as a JSON object. Returns null rather than throwing when
        /// the text is empty or is not a JSON object at all, which is what callers that probe a
        /// response (the error envelope, the metadata endpoint) need.
        /// </summary>
        internal static JObject TryObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            // Cheap guard before handing binary or HTML to the parser: a .ply body or a proxy's
            // error page is a common thing to receive where JSON was expected.
            var trimmed = json.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] != '{') return null;
            try
            {
                return JObject.Parse(trimmed);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Deserialises to <typeparamref name="T"/>, turning any parse failure into a readable throw.</summary>
        internal static T Deserialize<T>(string json, string what) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ReconContractException($"{what}: response body was empty.");
            T value;
            try
            {
                value = JsonConvert.DeserializeObject<T>(json);
            }
            catch (JsonException e)
            {
                throw new ReconContractException($"{what}: body is not valid JSON ({e.Message}).", e);
            }
            if (value == null)
                throw new ReconContractException($"{what}: body did not parse to an object.");
            return value;
        }

        internal static T Require<T>(T value, string what, string key) where T : class
        {
            if (value == null)
                throw new ReconContractException(
                    $"{what}: response has no '{key}'. docs/API.md says the success body is the raw object, " +
                    "so either the route changed or this is an error envelope that was not checked first.");
            return value;
        }
    }
}
