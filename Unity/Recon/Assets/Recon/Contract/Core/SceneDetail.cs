using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Recon.Contract
{
    /// <summary>
    /// One row of Scene.assets as <c>GET /api/scenes/:id</c> selects it
    /// (web/src/app/api/scenes/[id]/route.ts). Kinds are the AssetKind enum from
    /// prisma/schema.prisma: SOURCE_VIDEO, SPLAT_PLY, METADATA_JSON.
    /// </summary>
    public sealed class AssetInfo
    {
        public const string SourceVideo = "SOURCE_VIDEO";
        public const string SplatPly = "SPLAT_PLY";
        public const string MetadataJson = "METADATA_JSON";

        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; }

        /// <summary>
        /// Custody hash written at upload, never backfilled. The asset route returns the same
        /// value in the <c>X-Asset-SHA256</c> header, so the downloaded bytes can be checked
        /// against what custody recorded.
        /// </summary>
        [JsonProperty("sha256")] public string Sha256 { get; set; }

        /// <summary>
        /// Asset.byteSize is a Prisma BigInt, sent as a JSON number by
        /// web/src/lib/api.ts serialiseAsset(). A <c>long</c>, not an int: source walkthrough
        /// videos are already hundreds of megabytes.
        /// </summary>
        [JsonProperty("byteSize")] public long ByteSize { get; set; }

        [JsonProperty("mimeType")] public string MimeType { get; set; }
        [JsonProperty("createdAt")] public string CreatedAt { get; set; }

        public double MegaBytes => ByteSize / (1024.0 * 1024.0);

        public override string ToString() => $"{Kind} {MegaBytes:F1} MB sha {Sha256}";
    }

    /// <summary>
    /// <c>GET /api/scenes/:id</c> — the summary fields plus job.error, the asset list, and
    /// <see cref="AssetUrl"/>. Storage keys are deliberately not exposed: the asset route
    /// resolves them, so disk-versus-object-storage stays invisible to Unity.
    /// </summary>
    public sealed class SceneDetail : SceneSummary
    {
        [JsonProperty("assets")] public List<AssetInfo> Assets { get; set; }

        /// <summary>Relative path, e.g. <c>/api/scenes/&lt;id&gt;/asset</c>. Resolve against the API base URL.</summary>
        [JsonProperty("assetUrl")] public string AssetUrl { get; set; }

        /// <summary>The newest reconstructed .ply row, or null when the job has not produced one.</summary>
        public AssetInfo SplatPly => NewestOfKind(AssetInfo.SplatPly);

        /// <summary>
        /// The metadata.json row, when one exists. Present in the data model and written by
        /// <c>POST /api/jobs/:id/result</c>, but as of 2026-09-02 no route serves its bytes —
        /// see Recon.Api.MetadataUnavailableException.
        /// </summary>
        public AssetInfo MetadataJson => NewestOfKind(AssetInfo.MetadataJson);

        AssetInfo NewestOfKind(string kind)
        {
            if (Assets == null) return null;
            AssetInfo newest = null;
            foreach (var a in Assets)
            {
                if (a == null || !string.Equals(a.Kind, kind, StringComparison.Ordinal)) continue;
                // Assets come back ordered createdAt asc, so the last match is the newest; the
                // string compare keeps that true even if the order ever changes.
                if (newest == null || string.CompareOrdinal(a.CreatedAt ?? "", newest.CreatedAt ?? "") >= 0)
                    newest = a;
            }
            return newest;
        }
    }

    /// <summary>Success body of <c>GET /api/scenes/:id</c>: <c>{ "scene": { ... } }</c>.</summary>
    public sealed class SceneDetailResponse
    {
        [JsonProperty("scene")] public SceneDetail Scene { get; set; }

        public static SceneDetailResponse FromJson(string json)
        {
            var r = ContractJson.Deserialize<SceneDetailResponse>(json, "scene detail");
            ContractJson.Require(r.Scene, "scene detail", "scene");
            return r;
        }
    }
}
