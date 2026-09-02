using UnityEngine;

namespace Recon.Api
{
    /// <summary>
    /// Keeps the session JWT in PlayerPrefs so a dev does not retype a password after every
    /// <c>adb install</c>. A convenience, and the weaker of the two <see cref="ISessionStore"/>
    /// implementations.
    ///
    /// <b>Integration risk, handbook Section 10.</b> A RECON JWT is a bearer credential for an
    /// investigator's whole case list, valid 12 h (web/src/lib/auth.ts). PlayerPrefs on Android is
    /// a plain XML file in the app's private data directory: safe from other apps on a
    /// non-rooted device, readable by anyone with the device unlocked and adb enabled, and not
    /// encrypted at rest. This class is the minimum that makes the choice explicit and swappable —
    /// it is not the answer. Encryption at rest is D12 (FTW-59); until then
    /// <see cref="InMemorySessionStore"/> is the honest default for anything leaving the lab, and
    /// this is opt-in.
    /// </summary>
    public sealed class PlayerPrefsSessionStore : ISessionStore
    {
        const string Key = "recon.session.token";

        public string Token
        {
            get
            {
                var stored = PlayerPrefs.GetString(Key, null);
                return string.IsNullOrWhiteSpace(stored) ? null : stored;
            }
        }

        public bool HasToken => Token != null;

        public void Set(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                Clear();
                return;
            }
            PlayerPrefs.SetString(Key, token.Trim());
            PlayerPrefs.Save();
        }

        public void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
