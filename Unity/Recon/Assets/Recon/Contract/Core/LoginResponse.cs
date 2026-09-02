using Newtonsoft.Json;

namespace Recon.Contract
{
    /// <summary>
    /// Body of <c>POST /api/auth/login</c> (web/src/app/api/auth/login/route.ts):
    /// <code>{ "token": "...", "user": { "id", "email", "role" } }</code>
    /// The token is a 12 h HS256 JWT and goes in <c>Authorization: Bearer &lt;jwt&gt;</c> on every
    /// other call (web/src/lib/auth.ts sessionFromRequest).
    /// </summary>
    public sealed class LoginResponse
    {
        [JsonProperty("token")] public string Token { get; set; }
        [JsonProperty("user")] public UserInfo User { get; set; }

        public static LoginResponse FromJson(string json)
        {
            var r = ContractJson.Deserialize<LoginResponse>(json, "login response");
            // Refuse here rather than three calls later with an empty Authorization header,
            // where the symptom is a 401 that looks like wrong credentials.
            if (string.IsNullOrEmpty(r.Token))
                throw new ReconContractException("login response has no 'token'.");
            return r;
        }
    }

    /// <summary>The authenticated user as the login route selects it. Role gates the UI, never the data (the server does that).</summary>
    public sealed class UserInfo
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("email")] public string Email { get; set; }

        /// <summary>ADMIN | INVESTIGATOR | VIEWER (prisma Role). A string, so a new role does not crash an old build.</summary>
        [JsonProperty("role")] public string Role { get; set; }

        public override string ToString() => $"{Email} ({Role})";
    }
}
