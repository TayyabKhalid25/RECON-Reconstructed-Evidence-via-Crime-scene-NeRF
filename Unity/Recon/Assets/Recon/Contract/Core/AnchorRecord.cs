using System;
using Newtonsoft.Json;

namespace Recon.Contract
{
    /// <summary>
    /// A 3D vector on the wire. Nested rather than eleven flat floats so the client reads
    /// position/rotation instead of reassembling them in the right order.
    /// Named Vec3, not Vector3, so Unity glue can use both without an alias.
    /// </summary>
    public sealed class Vec3
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("z")] public double Z { get; set; }

        public override string ToString() => $"({X:F4}, {Y:F4}, {Z:F4})";
    }

    /// <summary>A quaternion on the wire. The server validates it is unit length within 1e-3.</summary>
    public sealed class Quat
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
        [JsonProperty("z")] public double Z { get; set; }
        [JsonProperty("w")] public double W { get; set; }

        public override string ToString() => $"({X:F6}, {Y:F6}, {Z:F6}, {W:F6})";
    }

    /// <summary>
    /// The frame block the server states in every anchor response, so nobody has to remember it
    /// from a doc and a client that starts flipping axes is contradicting the response it was
    /// handed (docs/FRAMES.md).
    /// </summary>
    public sealed class AnchorFrame
    {
        [JsonProperty("handedness")] public string Handedness { get; set; }
        [JsonProperty("upAxis")] public string UpAxis { get; set; }

        /// <summary>Always "anchor->scene": the transform maps anchor space into scene space.</summary>
        [JsonProperty("space")] public string Space { get; set; }

        public bool MatchesUnityConvention =>
            string.Equals(Handedness, "left", StringComparison.Ordinal) &&
            string.Equals(UpAxis, "y", StringComparison.Ordinal);

        public override string ToString() => $"{Handedness}/{UpAxis} {Space}";
    }

    /// <summary>
    /// An anchor as PR #18's web/src/lib/anchor.ts serialiseAnchor() writes it, returned by both
    /// <c>POST</c> and <c>GET /api/scenes/:id/anchor</c> wrapped in <c>{ "anchor": ... }</c>.
    ///
    /// The transform is <b>decomposed</b> — position, unit quaternion, uniform scale — and never a
    /// 4x4 matrix: Unity's Matrix4x4 is column-major and a transposed matrix in JSON misplaces the
    /// twin subtly instead of failing visibly, which is the failure class docs/FRAMES.md exists to
    /// prevent.
    ///
    /// <see cref="Scale"/> is the anchor-to-scene fit and is normally 1. It is <b>not</b>
    /// Scene.unitScale, which converts scene units to metres and comes from the marker. Applying
    /// one where the other belongs is the double-scale bug (docs/ANCHORING.md).
    /// </summary>
    public sealed class AnchorRecord
    {
        /// <summary>Our row id.</summary>
        [JsonProperty("id")] public string Id { get; set; }

        /// <summary>The provider's opaque handle, e.g. an ARCore Cloud Anchor id.</summary>
        [JsonProperty("anchorId")] public string AnchorId { get; set; }

        [JsonProperty("provider")] public string Provider { get; set; }
        [JsonProperty("position")] public Vec3 Position { get; set; }
        [JsonProperty("rotation")] public Quat Rotation { get; set; }
        [JsonProperty("scale")] public double Scale { get; set; }

        /// <summary>ISO-8601, or null. Null means no expiry was recorded, <b>not</b> that it never expires.</summary>
        [JsonProperty("expiresAt")] public string ExpiresAt { get; set; }

        /// <summary>
        /// Computed server side. Read it, never recompute it from the device clock: a resolve that
        /// silently returns a dead Cloud Anchor looks exactly like broken tracking on the phone.
        /// </summary>
        [JsonProperty("expired")] public bool Expired { get; set; }

        [JsonProperty("deviceLabel")] public string DeviceLabel { get; set; }
        [JsonProperty("createdAt")] public string CreatedAt { get; set; }
        [JsonProperty("frame")] public AnchorFrame Frame { get; set; }

        public bool HasExpiry => !string.IsNullOrEmpty(ExpiresAt);

        public override string ToString() =>
            $"{Provider}:{AnchorId} pos {Position} rot {Rotation} scale {Scale:F4}" +
            (Expired ? " EXPIRED" : "");
    }

    /// <summary>Success body of both anchor endpoints: <c>{ "anchor": { ... } }</c>.</summary>
    public sealed class AnchorResponse
    {
        [JsonProperty("anchor")] public AnchorRecord Anchor { get; set; }

        public static AnchorResponse FromJson(string json)
        {
            var r = ContractJson.Deserialize<AnchorResponse>(json, "anchor response");
            ContractJson.Require(r.Anchor, "anchor response", "anchor");
            return r;
        }
    }

    /// <summary>
    /// Body for <c>POST /api/scenes/:id/anchor</c>, matching web/src/lib/anchor.ts AnchorInput.
    ///
    /// Optional fields are omitted when unset rather than sent as null: Zod's <c>.optional()</c>
    /// accepts absent, not null, so <c>"scale": null</c> would fail validation where sending
    /// nothing lets the server default apply.
    ///
    /// The POST takes <c>expiresAt</c> (an ISO datetime), not a TTL in days. FTW-54 is the ticket
    /// that would change that; until it lands, a <c>ttlDays</c> field would be dropped silently by
    /// the schema and the anchor would get no expiry at all.
    /// </summary>
    public sealed class AnchorInput
    {
        [JsonProperty("anchorId")] public string AnchorId { get; set; }
        [JsonProperty("provider", NullValueHandling = NullValueHandling.Ignore)] public string Provider { get; set; }
        [JsonProperty("position")] public Vec3 Position { get; set; }
        [JsonProperty("rotation")] public Quat Rotation { get; set; }

        /// <summary>Anchor-to-scene fit, normally 1. Omitted when null so the server's default applies.</summary>
        [JsonProperty("scale", NullValueHandling = NullValueHandling.Ignore)] public double? Scale { get; set; }

        [JsonProperty("expiresAt", NullValueHandling = NullValueHandling.Ignore)] public string ExpiresAt { get; set; }
        [JsonProperty("deviceLabel", NullValueHandling = NullValueHandling.Ignore)] public string DeviceLabel { get; set; }

        public string ToJson() => JsonConvert.SerializeObject(this, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
        });

        public static AnchorInput FromJson(string json) =>
            ContractJson.Deserialize<AnchorInput>(json, "anchor input");
    }
}
