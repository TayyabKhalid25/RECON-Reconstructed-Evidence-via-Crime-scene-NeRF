using System;
using System.IO;
using Recon.Contract;

namespace Recon.Scenes
{
    /// <summary>
    /// Two decisions on the load path, pulled out of the MonoBehaviour so they are unit tested
    /// instead of observed on a phone:
    ///
    /// 1. <b>Which hash the bytes are checked against.</b> The Asset row and metadata.json both
    ///    claim to describe the same .ply. Custody is only meaningful if a disagreement between
    ///    them stops the load rather than being silently resolved in favour of one.
    /// 2. <b>Whether a cached file can be reused.</b> Cache by content hash, never by file name:
    ///    a scene re-exported server side under the same id, and a download cut off by a dropped
    ///    tailnet connection, both look identical to a name-based cache.
    ///
    /// Pure C#: no UnityEngine, so it runs under `dotnet test Unity/Recon.Core.Tests`.
    /// </summary>
    public static class SceneAssetCache
    {
        /// <summary>Subfolder of persistentDataPath that holds downloaded scenes.</summary>
        public const string CacheFolderName = "scenes";

        /// <summary>
        /// The hash the downloaded bytes must equal, or null when nobody stated one. Prefers the
        /// Asset row (what custody recorded at upload) and falls back to metadata.json.
        /// </summary>
        /// <exception cref="CustodyMismatchException">
        /// The two records disagree, which means at least one of them describes bytes nobody has.
        /// </exception>
        public static string ExpectedSha256(SceneDetail detail, SceneMetadata metadata)
        {
            var fromAsset = detail?.SplatPly?.Sha256;
            var fromMetadata = metadata?.Sha256;

            bool assetUsable = Sha256Hex.IsUsableHash(fromAsset);
            bool metadataUsable = Sha256Hex.IsUsableHash(fromMetadata);

            if (assetUsable && metadataUsable && !Sha256Hex.Matches(fromAsset, fromMetadata))
                throw new CustodyMismatchException(
                    $"The scene's asset row and its metadata.json disagree about the .ply: the database says " +
                    $"{fromAsset} and metadata.json says {fromMetadata}. One of them describes bytes nobody has, " +
                    "so the scene is refused rather than loaded against a guess.");

            if (assetUsable) return fromAsset;
            if (metadataUsable) return fromMetadata;
            return null;
        }

        /// <summary>
        /// True when the .ply has to be fetched. False only when a file is already at
        /// <paramref name="path"/> and hashes to <paramref name="expectedSha256"/> — that is the
        /// cache hit that keeps a second launch off the network.
        ///
        /// With no expected hash the answer is always true: there is no way to tell a complete
        /// file from a truncated one, and re-fetching is cheaper than rendering the wrong scene.
        /// </summary>
        public static bool ShouldDownload(string path, string expectedSha256)
        {
            if (string.IsNullOrEmpty(path)) return true;
            if (!Sha256Hex.IsUsableHash(expectedSha256)) return true;
            if (!File.Exists(path)) return true;

            var info = new FileInfo(path);
            if (info.Length == 0) return true;

            try
            {
                return !Sha256Hex.Matches(Sha256Hex.ComputeFileHex(path), expectedSha256);
            }
            catch (IOException)
            {
                // Unreadable cache entry: treat it as absent rather than failing the load.
                return true;
            }
        }

        /// <summary>
        /// Hashes the file that was just written and compares it against everything the server
        /// said about it. Refuses on any disagreement; reports an unverified load when the server
        /// said nothing at all, which is the FTW-30 dev-scene case.
        /// </summary>
        /// <param name="expectedSha256">From the Asset row or metadata.json; may be null.</param>
        /// <param name="headerSha256">The <c>X-Asset-SHA256</c> response header; may be null.</param>
        public static AssetVerification Verify(string path, string expectedSha256, string headerSha256)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new FileNotFoundException("Downloaded .ply is not on disk.", path ?? "(null)");

            bool haveExpected = Sha256Hex.IsUsableHash(expectedSha256);
            bool haveHeader = Sha256Hex.IsUsableHash(headerSha256);

            // Both come from the same Asset row server side, so a disagreement means the response
            // did not come from the scene that was asked for.
            if (haveExpected && haveHeader && !Sha256Hex.Matches(expectedSha256, headerSha256))
                throw new CustodyMismatchException(
                    $"The X-Asset-SHA256 header ({headerSha256}) contradicts the sha256 recorded for this scene " +
                    $"({expectedSha256}). Both come from the same Asset row, so the bytes served are not the " +
                    "bytes this scene's record describes.");

            var computed = Sha256Hex.ComputeFileHex(path);

            if (haveExpected && !Sha256Hex.Matches(computed, expectedSha256))
                throw new CustodyMismatchException(
                    $"The downloaded .ply does not match what custody recorded. Expected {expectedSha256}, " +
                    $"computed {computed}, file {path}. Refusing the scene: evidence that cannot be verified " +
                    "must not be rendered as if it were.");

            if (!haveExpected && haveHeader && !Sha256Hex.Matches(computed, headerSha256))
                throw new CustodyMismatchException(
                    $"The downloaded .ply does not match the X-Asset-SHA256 header. Header {headerSha256}, " +
                    $"computed {computed}, file {path}.");

            if (!haveExpected && !haveHeader)
                return new AssetVerification(false, computed,
                    "loaded with no sha256 to check against, so custody is unverified for this scene");

            return new AssetVerification(true, computed, "sha256 verified against " +
                (haveExpected ? "the scene record" : "the X-Asset-SHA256 header"));
        }

        /// <summary>
        /// Where a scene's files are cached: <c>&lt;persistentDataPath&gt;/scenes/&lt;sceneId&gt;</c>.
        /// The id is reduced to a safe single segment, because it reaches here from a text field
        /// as well as from a listing.
        /// </summary>
        public static string SceneFolder(string persistentDataPath, string sceneId) =>
            Path.Combine(persistentDataPath ?? "", CacheFolderName, SafeSegment(sceneId));

        /// <summary>Strips anything that could walk out of the cache folder or upset a filesystem.</summary>
        public static string SafeSegment(string sceneId)
        {
            if (string.IsNullOrWhiteSpace(sceneId)) return "unnamed";
            var chars = sceneId.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                          c == '-' || c == '_';
                if (!ok) chars[i] = '_';
            }
            return new string(chars);
        }
    }

    /// <summary>Outcome of the post-download custody check.</summary>
    public readonly struct AssetVerification
    {
        public AssetVerification(bool verified, string computedSha256, string reason)
        {
            Verified = verified;
            ComputedSha256 = computedSha256;
            Reason = reason;
        }

        public bool Verified { get; }
        public string ComputedSha256 { get; }

        /// <summary>What to log or put on screen, in either case.</summary>
        public string Reason { get; }

        public override string ToString() => (Verified ? "verified: " : "UNVERIFIED: ") + Reason;
    }

    /// <summary>
    /// The bytes on the device are not the bytes the record describes. A hard refusal: the whole
    /// point of hashing every asset at upload is that this cannot be waved through.
    /// </summary>
    public sealed class CustodyMismatchException : Exception
    {
        public CustodyMismatchException(string message) : base("Custody check failed: " + message) { }
    }
}
