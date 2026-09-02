using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Recon.Contract
{
    /// <summary>
    /// One row of <c>GET /api/scenes?status=READY</c>, exactly the fields that route's Prisma
    /// select returns (web/src/app/api/scenes/route.ts): id, name, createdAt, unitScale, caseId
    /// and a nested job. Newest first; the server filters by owner, so the client shows what it
    /// is given.
    /// </summary>
    public class SceneSummary
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }

        /// <summary>ISO-8601 as Next serialises a Date. Kept as a string: the client only displays and sorts it.</summary>
        [JsonProperty("createdAt")] public string CreatedAt { get; set; }

        /// <summary>
        /// Metres per scene unit, from the marker. <b>Nullable</b>: Scene.unitScale is
        /// <c>Float?</c> in prisma/schema.prisma and is null until the GPU track computes it.
        /// Null means "not measured yet", which is a different fact from 0.0 ("measured, and the
        /// scene is not metric") — deserialising one into the other would hide a missing
        /// measurement behind an alarming one. Neither is usable: docs/FRAMES.md.
        /// </summary>
        [JsonProperty("unitScale")] public double? UnitScale { get; set; }

        [JsonProperty("caseId")] public string CaseId { get; set; }
        [JsonProperty("job")] public JobInfo Job { get; set; }

        public bool HasUnitScale => UnitScale.HasValue;

        /// <summary>True only when the reconstruction finished. Everything else is not loadable yet.</summary>
        public bool IsReady => Job != null && string.Equals(Job.Status, JobInfo.Ready, StringComparison.Ordinal);

        public override string ToString()
        {
            var scale = UnitScale.HasValue ? UnitScale.Value.ToString("F6") : "not measured";
            return $"{Name} [{Job?.Status ?? "no job"}] unitScale {scale}";
        }
    }

    /// <summary>
    /// The reconstruction job. The list route selects id/status/progress; the detail route also
    /// selects <see cref="Error"/>, so it is null on a summary and may be null on a detail too.
    /// Status strings are the JobStatus enum from docs/API.md's state machine.
    /// </summary>
    public sealed class JobInfo
    {
        public const string Pending = "PENDING";
        public const string Processing = "PROCESSING";
        public const string Ready = "READY";
        public const string Failed = "FAILED";
        public const string Cancelled = "CANCELLED";

        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("progress")] public int Progress { get; set; }

        /// <summary>Readable text when status is FAILED; only the detail route returns it.</summary>
        [JsonProperty("error")] public string Error { get; set; }

        public override string ToString() =>
            Status + (Progress > 0 && Progress < 100 ? $" {Progress}%" : "") +
            (string.IsNullOrEmpty(Error) ? "" : ": " + Error);
    }

    /// <summary>
    /// <c>GET /api/scenes</c> success body. Wrapped as <c>{ "scenes": [...] }</c>: the raw object,
    /// not an envelope (web/src/lib/api.ts apiOk).
    /// </summary>
    public sealed class SceneListResponse
    {
        [JsonProperty("scenes")] public List<SceneSummary> Scenes { get; set; }

        public static SceneListResponse FromJson(string json)
        {
            var r = ContractJson.Deserialize<SceneListResponse>(json, "scene list");
            ContractJson.Require(r.Scenes, "scene list", "scenes");
            return r;
        }
    }
}
