namespace Recon.Api
{
    /// <summary>
    /// Where the session JWT lives between calls. An interface because the two useful answers are
    /// different: nothing on disk (the default, and what a real forensic device should do), or
    /// PlayerPrefs so a dev does not retype a password on every deploy.
    ///
    /// Token handling on the device is an integration risk from handbook Section 10: a JWT is a
    /// bearer credential for an investigator's whole case list, and anything that persists it is a
    /// credential at rest. This interface is the minimum — it makes the choice explicit and
    /// swappable — not a solution. Encryption at rest is D12 (FTW-59); until then the honest
    /// default is <see cref="InMemorySessionStore"/>, which loses the token when the app closes.
    /// </summary>
    public interface ISessionStore
    {
        /// <summary>The raw JWT, or null when there is no session.</summary>
        string Token { get; }

        bool HasToken { get; }

        /// <summary>Stores a token. A null or blank value clears instead of storing an unusable one.</summary>
        void Set(string token);

        void Clear();
    }

    /// <summary>
    /// Keeps the token in memory only, so closing the app ends the session. The default, and the
    /// right behaviour for a device that leaves the lab.
    /// </summary>
    public sealed class InMemorySessionStore : ISessionStore
    {
        string m_token;

        public string Token => m_token;

        public bool HasToken => !string.IsNullOrWhiteSpace(m_token);

        public void Set(string token)
        {
            // A blank token would go out as "Bearer " and come back 401, which reads like an
            // expired session rather than "we never logged in".
            m_token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        }

        public void Clear() => m_token = null;
    }
}
