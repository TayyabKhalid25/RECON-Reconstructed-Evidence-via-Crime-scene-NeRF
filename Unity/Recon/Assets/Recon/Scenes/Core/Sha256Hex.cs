using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Recon.Scenes
{
    /// <summary>
    /// SHA-256 as lowercase hex, the format Asset.sha256 stores and the asset route returns in
    /// <c>X-Asset-SHA256</c>. Custody is not a November feature: the client checks the bytes it
    /// downloaded against the hash the server recorded, and refuses the scene on a mismatch rather
    /// than rendering evidence it cannot vouch for.
    ///
    /// Pure C#: no UnityEngine, so it is tested under `dotnet test Unity/Recon.Core.Tests`.
    /// </summary>
    public static class Sha256Hex
    {
        /// <summary>All-zeros: the placeholder in docs/samples/metadata.example.json.</summary>
        public const string Placeholder = "0000000000000000000000000000000000000000000000000000000000000000";

        /// <summary>64 KB: big enough that a 45 MB .ply is ~700 reads, small enough to stay off the large object heap.</summary>
        const int BufferSize = 64 * 1024;

        public static string ComputeHex(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(bytes));
            }
        }

        /// <summary>
        /// Hashes from the stream's current position to the end, in chunks, so a multi-hundred-MB
        /// asset never has to be resident in memory.
        /// </summary>
        public static string ComputeHex(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            using (var sha = SHA256.Create())
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    sha.TransformBlock(buffer, 0, read, null, 0);
                sha.TransformFinalBlock(buffer, 0, 0);
                return ToHex(sha.Hash);
            }
        }

        /// <summary>Opens the file read-only and shared, so a hash never blocks another reader.</summary>
        public static string ComputeFileHex(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize))
            {
                return ComputeHex(stream);
            }
        }

        /// <summary>
        /// True when both sides are usable hashes of the same value, compared case- and
        /// whitespace-insensitively because one side is an HTTP header the client did not write.
        /// A placeholder (all zeros) never matches: treating it as equal would silently turn the
        /// custody check into a no-op on any scene that was never hashed.
        /// </summary>
        public static bool Matches(string a, string b)
        {
            if (!IsUsableHash(a) || !IsUsableHash(b)) return false;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>64 hex characters and not the all-zeros placeholder.</summary>
        public static bool IsUsableHash(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash)) return false;
            var h = hash.Trim();
            if (h.Length != 64) return false;
            bool allZero = true;
            foreach (var c in h)
            {
                bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex) return false;
                if (c != '0') allZero = false;
            }
            return !allZero;
        }

        static string ToHex(byte[] hash)
        {
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
