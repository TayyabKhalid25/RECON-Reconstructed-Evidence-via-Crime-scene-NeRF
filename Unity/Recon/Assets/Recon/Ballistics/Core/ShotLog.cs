using System;
using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json;
using Recon.Colliders;

namespace Recon.Ballistics
{
    /// <summary>
    /// One fired shot as it goes to disk. The Challenge 1 study ("how much mesh approximation error
    /// is tolerable before trajectories diverge") reads these files back and compares impact points
    /// between collider sets, so an entry must be enough to reproduce the shot and to place the
    /// impact independently of where the phone happened to start.
    ///
    /// That is why the impact is stored TWICE (docs/FRAMES.md, and the frame rule in
    /// docs/UNITY-TRACK-PLAN.md):
    ///
    ///   ImpactPointSceneUnitsLocal  SceneRoot-local, SCENE UNITS. Fixed to the reconstruction, so
    ///                               two runs that aligned the marker differently are comparable.
    ///   ImpactPointWorldMetres      Unity world space, METRES. What the physics actually ran in.
    ///
    /// unitScale relates them and is recorded with every shot; unitScale 0 means the scene is not
    /// metric and no distance from that run is an accuracy claim.
    /// </summary>
    public sealed class ShotLogEntry
    {
        [JsonProperty("utc")] public string Utc { get; set; }
        [JsonProperty("sceneId")] public string SceneId { get; set; }

        /// <summary>"ArPlanes", "SplatMesh" or "Both". A string so a log stays readable.</summary>
        [JsonProperty("colliderSet")] public string ColliderSet { get; set; }

        [JsonProperty("parameters")] public ShotParameters Parameters { get; set; }

        /// <summary>"Impact", "MaxFlightTime" or "MaxSubsteps".</summary>
        [JsonProperty("stoppedReason")] public string StoppedReason { get; set; }

        [JsonProperty("substeps")] public int Substeps { get; set; }
        [JsonProperty("flightTimeSeconds")] public float FlightTimeSeconds { get; set; }
        [JsonProperty("arcPointCount")] public int ArcPointCount { get; set; }

        /// <summary>Null when nothing was hit, which is a result too.</summary>
        [JsonProperty("impact")] public ImpactRecord Impact { get; set; }

        /// <summary>Impact in SceneRoot-local coordinates, SCENE UNITS. Null when there was no impact.</summary>
        [JsonProperty("impactPointSceneUnitsLocal")] public Vector3? ImpactPointSceneUnitsLocal { get; set; }

        /// <summary>Impact in Unity world space, METRES. Null when there was no impact.</summary>
        [JsonProperty("impactPointWorldMetres")] public Vector3? ImpactPointWorldMetres { get; set; }

        /// <summary>Metres per scene unit at the time of the shot. 0 means the scene is not metric.</summary>
        [JsonProperty("unitScale")] public float UnitScale { get; set; }

        [JsonProperty("sceneIsMetric")] public bool SceneIsMetric { get; set; }

        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)] public string Note { get; set; }

        public static ShotLogEntry From(ShotParameters parameters, TrajectoryResult result,
                                        ColliderSet colliderSet, Vector3? sceneLocalPoint,
                                        float unitScale, string sceneId, string note = null)
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            if (result == null) throw new ArgumentNullException(nameof(result));

            return new ShotLogEntry
            {
                Utc = DateTime.UtcNow.ToString("o"),
                SceneId = sceneId,
                ColliderSet = colliderSet.ToString(),
                Parameters = parameters,
                StoppedReason = result.StoppedReason.ToString(),
                Substeps = result.Substeps,
                FlightTimeSeconds = result.FlightTime,
                ArcPointCount = result.Points.Count,
                Impact = result.Impact,
                ImpactPointWorldMetres = result.Impact?.Point,
                ImpactPointSceneUnitsLocal = result.Impact != null ? sceneLocalPoint : null,
                UnitScale = unitScale,
                SceneIsMetric = unitScale > 0f,
                Note = note,
            };
        }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

        public static ShotLogEntry FromJson(string json) => JsonConvert.DeserializeObject<ShotLogEntry>(json);
    }

    /// <summary>
    /// A run of shots, written to persistentDataPath/shots/shots-&lt;utc&gt;.json the same way PerfLog
    /// writes its samples: a file pulled with adb, so the numbers in docs/RESULTS.md come from data
    /// and not from reading a screen.
    /// </summary>
    public sealed class ShotLog
    {
        [JsonProperty("startedUtc")] public string StartedUtc { get; set; } = DateTime.UtcNow.ToString("o");
        [JsonProperty("sceneId")] public string SceneId { get; set; }
        [JsonProperty("device")] public string Device { get; set; }
        [JsonProperty("unitScale")] public float UnitScale { get; set; }
        [JsonProperty("shots")] public List<ShotLogEntry> Shots { get; set; } = new List<ShotLogEntry>();

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

        public static ShotLog FromJson(string json) => JsonConvert.DeserializeObject<ShotLog>(json);
    }
}
