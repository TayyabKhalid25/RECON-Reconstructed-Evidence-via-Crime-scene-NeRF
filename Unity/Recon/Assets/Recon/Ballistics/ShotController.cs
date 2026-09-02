using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Recon.Alignment;
using Recon.Colliders;
using Recon.Config;
using UnityEngine;
using UnityEngine.Rendering;
using NVector3 = System.Numerics.Vector3;

namespace Recon.Ballistics
{
    /// <summary>
    /// Fires a shot from the camera (or an assigned muzzle transform), integrates it with
    /// <see cref="TrajectoryIntegrator"/> through <see cref="PhysicsRaycaster"/>, draws the arc,
    /// marks the impact, and appends the shot to a JSON log.
    ///
    /// Handbook Section 09 step 5: "Parameter input, trajectory integration, impact point, persistent
    /// overlay." The overlay is persistent on purpose: the arcs and markers stay until ClearImpacts,
    /// because the demo is "one shot fired, impact drawn" and the study is many shots compared.
    ///
    /// Everything it creates is a child of SceneRoot, so re-aligning the marker moves the room, the
    /// arcs and the impacts together. Impacts are stored in SceneRoot-local scene units AND world
    /// metres (see ShotLogEntry) so the Challenge 1 comparison does not depend on where the phone
    /// started.
    /// </summary>
    [AddComponentMenu("Recon/Ballistics/Shot Controller")]
    [DisallowMultipleComponent]
    public sealed class ShotController : MonoBehaviour
    {
        [Header("Projectile")]
        [SerializeField, Range(1f, 500f), Tooltip("Muzzle speed, m/s. 300 m/s is the handbook's example; 20 m/s makes the arc visible in a room.")]
        float speed = 120f;

        [SerializeField, Tooltip("Projectile mass, kg. 0.008 is an 8 g / 9 mm round.")]
        float mass = ShotParameters.DefaultMassKg;

        [SerializeField, Tooltip("Drag coefficient. 0.3 for a blunt pistol round, order of magnitude only.")]
        float dragCoefficient = ShotParameters.DefaultDragCoefficient;

        [SerializeField, Tooltip("Cross-section area, m^2. 6.39e-5 is a 9.02 mm diameter.")]
        float crossSectionArea = ShotParameters.DefaultCrossSectionAreaM2;

        [SerializeField] bool useDrag = true;

        [Header("Integration")]
        [SerializeField, Tooltip("Substep, seconds. speed * dt must stay under maxStepLength or the shot is refused: that is the tunnelling guard (handbook Section 09).")]
        float dt = 0.0001f;

        [SerializeField, Tooltip("Longest substep in metres. 0.05 m is under the thickness of anything in a room.")]
        float maxStepLength = ShotParameters.DefaultMaxStepLengthMetres;

        [SerializeField, Range(0.1f, 20f)] float maxFlightTime = 3f;

        [Header("Colliders")]
        [SerializeField, Tooltip("Which collision geometry to shoot at. Firing the same shot into both, separately, is the Challenge 1 measurement.")]
        ColliderSet colliderSet = ColliderSet.Both;

        [Header("Muzzle")]
        [SerializeField, Tooltip("Leave empty to fire from the main camera pose, which is what the phone in your hand is.")]
        Transform muzzle;

        [Header("Overlay")]
        [SerializeField] bool drawArc = true;
        [SerializeField, Tooltip("Arc width in metres.")] float arcWidthMetres = 0.004f;
        [SerializeField] Color arcColour = new Color(1f, 0.85f, 0.1f, 1f);
        [SerializeField, Tooltip("Impact marker diameter in metres. 0.02 = 2 cm.")] float markerDiameterMetres = 0.02f;
        [SerializeField] Color markerColour = new Color(0.9f, 0.1f, 0.1f, 1f);
        [SerializeField, Tooltip("Arc points are decimated to this many before drawing; a 30,000 substep arc does not need 30,000 line vertices.")]
        int maxArcPoints = 256;

        [Header("Logging")]
        [SerializeField, Tooltip("Append every shot to persistentDataPath/shots/shots-<utc>.json, the same pattern PerfLog uses.")]
        bool logShots = true;

        [SerializeField, Tooltip("Recorded with every shot so a log file can be matched to a scene. Set by the loader when one exists.")]
        string sceneId;

        readonly List<ImpactMarker> m_markers = new List<ImpactMarker>();
        readonly List<GameObject> m_arcs = new List<GameObject>();
        readonly List<ImpactRecord> m_impacts = new List<ImpactRecord>();
        readonly Dictionary<ColliderSet, int> m_impactsPerSet = new Dictionary<ColliderSet, int>();

        PhysicsRaycaster m_raycaster;
        Material m_arcMaterial;
        Material m_markerMaterial;
        ShotLog m_log;
        string m_logPath;
        int m_shotNumber;

        /// <summary>Raised after every shot that hit something. SpatterEmitter listens to this.</summary>
        public event Action<ImpactRecord> Impacted;

        public string Status { get; private set; } = "no shots fired";
        public IReadOnlyList<ImpactRecord> Impacts => m_impacts;
        public int ShotsFired => m_shotNumber;
        public string LogPath => m_logPath;

        public ColliderSet TargetSet
        {
            get => colliderSet;
            set => colliderSet = value;
        }

        public float Speed
        {
            get => speed;
            set => speed = Mathf.Max(1f, value);
        }

        public bool UseDrag
        {
            get => useDrag;
            set => useDrag = value;
        }

        public float Dt => dt;

        public int ImpactCountFor(ColliderSet set) => m_impactsPerSet.TryGetValue(set, out int n) ? n : 0;

        void Awake()
        {
            m_raycaster = new PhysicsRaycaster();
            if (string.IsNullOrEmpty(sceneId)) sceneId = null;
        }

        /// <summary>
        /// Builds the shot from the current pose and integrates it. Returns the result, or null if the
        /// parameters were refused, in which case Status carries the reason (a refused shot is not an
        /// exception the app should die on: on a phone the only feedback is the overlay).
        /// </summary>
        public TrajectoryResult Fire()
        {
            var root = SceneRoot.Find();
            if (root == null)
            {
                Status = "no SceneRoot in the scene";
                return null;
            }

            var pose = MuzzlePose();
            if (pose == null)
            {
                Status = "no muzzle: assign one, or give the scene a Main Camera";
                return null;
            }

            int mask = CollisionLayers.MaskFor(colliderSet);
            if (mask == 0)
            {
                Status = $"{colliderSet} has no layer, so this shot could not hit anything. See the layer error in the log.";
                Debug.LogError("[Recon] " + Status);
                return null;
            }

            var parameters = new ShotParameters(
                pose.Value.position.ToNumerics(),
                (pose.Value.rotation * Vector3.forward).ToNumerics(),
                speed,
                mass: mass,
                dragCoefficient: dragCoefficient,
                crossSectionArea: crossSectionArea,
                gravity: Physics.gravity.ToNumerics(),
                dt: dt,
                maxFlightTime: maxFlightTime,
                layerMask: mask,
                useDrag: useDrag,
                maxStepLength: maxStepLength);

            TrajectoryResult result;
            try
            {
                result = TrajectoryIntegrator.Simulate(parameters, m_raycaster);
            }
            catch (ArgumentException e)
            {
                // The tunnelling guard, almost always. Say what to change rather than throwing at the user.
                Status = "SHOT REFUSED: " + e.Message;
                Debug.LogError("[Recon] " + Status);
                return null;
            }

            m_shotNumber++;
            if (drawArc) DrawArc(root.transform, result);

            NVector3? sceneLocal = null;
            if (result.Impact != null)
            {
                var marker = ImpactMarker.Create(root.transform, result.Impact, colliderSet, m_shotNumber,
                                                 markerDiameterMetres, MarkerMaterial());
                m_markers.Add(marker);
                m_impacts.Add(result.Impact);
                var hitSet = SetFromTag(result.Impact.ColliderTag);
                m_impactsPerSet[hitSet] = ImpactCountFor(hitSet) + 1;
                sceneLocal = marker.SceneLocalPoint.ToNumerics();
            }

            Status = DescribeShot(result, root);
            Debug.Log("[Recon] " + Status);

            if (logShots) AppendToLog(parameters, result, sceneLocal, root);
            if (result.Impact != null) Impacted?.Invoke(result.Impact);
            return result;
        }

        string DescribeShot(TrajectoryResult result, SceneRoot root)
        {
            if (result.Impact == null)
                return $"shot {m_shotNumber}: no impact ({result.StoppedReason}) after {result.Substeps} substeps, " +
                       $"{result.FlightTime:F3} s. Nothing on {CollisionLayers.Describe(colliderSet)} was in the way.";

            var impact = result.Impact;
            return $"shot {m_shotNumber}: hit {impact.ColliderTag} at {impact.FlightTime * 1000f:F1} ms, " +
                   $"{impact.Speed:F0} m/s, incidence {impact.IncidenceAngleDegrees:F1} deg, " +
                   $"{result.Substeps} substeps" + (root.IsMetric ? "" : " | scene NOT metric, distances are not measurements");
        }

        /// <summary>
        /// Which collider set actually answered, from the layer name the raycaster recorded. With
        /// ColliderSet.Both aimed at two layers, this is the only way to know which one was hit, and
        /// that is exactly the number the Challenge 1 comparison needs.
        /// </summary>
        static ColliderSet SetFromTag(string colliderTag)
        {
            var settings = ReconSettings.Instance;
            if (string.Equals(colliderTag, settings.splatMeshLayer, StringComparison.Ordinal)) return ColliderSet.SplatMesh;
            if (string.Equals(colliderTag, settings.arPlanesLayer, StringComparison.Ordinal)) return ColliderSet.ArPlanes;
            return ColliderSet.Both;
        }

        Pose? MuzzlePose()
        {
            if (muzzle != null) return new Pose(muzzle.position, muzzle.rotation);
            var camera = Camera.main;
            if (camera == null) return null;
            return new Pose(camera.transform.position, camera.transform.rotation);
        }

        void DrawArc(Transform root, TrajectoryResult result)
        {
            var points = Decimate(result.Points, maxArcPoints);
            if (points.Length < 2) return;

            var go = new GameObject($"ShotArc {m_shotNumber}");
            go.transform.SetParent(root, false);

            var line = go.AddComponent<LineRenderer>();
            // Local space, so the arc stays put in the room when an aligner moves SceneRoot.
            line.useWorldSpace = false;
            line.positionCount = points.Length;
            for (int i = 0; i < points.Length; i++)
                line.SetPosition(i, root.InverseTransformPoint(points[i].ToUnity()));

            // Width is a metres-in-the-room quantity, and this transform is scaled by unitScale.
            line.widthMultiplier = arcWidthMetres * ImpactMarker.LocalScaleFactor(root);
            line.numCapVertices = 2;
            line.sharedMaterial = ArcMaterial();
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.View;

            m_arcs.Add(go);
        }

        /// <summary>One unlit material for every arc, and one for every marker: a shot should not cost two materials.</summary>
        Material ArcMaterial() => m_arcMaterial != null
            ? m_arcMaterial
            : m_arcMaterial = RuntimeMaterials.CreateUnlit(arcColour, "ShotArc");

        Material MarkerMaterial() => m_markerMaterial != null
            ? m_markerMaterial
            : m_markerMaterial = RuntimeMaterials.CreateUnlit(markerColour, "ImpactMarker");

        /// <summary>
        /// Keeps the first and last point (the muzzle and the impact) and evenly samples between.
        /// A 300 m/s shot at dt = 0.1 ms produces tens of thousands of substeps; a line renderer with
        /// that many vertices costs frames on a phone and looks identical.
        /// </summary>
        static NVector3[] Decimate(List<NVector3> points, int max)
        {
            if (points == null || points.Count == 0) return new NVector3[0];
            if (max < 2 || points.Count <= max) return points.ToArray();

            var result = new NVector3[max];
            for (int i = 0; i < max; i++)
            {
                int index = (int)((long)i * (points.Count - 1) / (max - 1));
                result[i] = points[index];
            }
            return result;
        }

        void AppendToLog(ShotParameters parameters, TrajectoryResult result, NVector3? sceneLocal, SceneRoot root)
        {
            try
            {
                if (m_log == null)
                {
                    var dir = Path.Combine(Application.persistentDataPath, ReconSettings.Instance.shotLogFolder);
                    Directory.CreateDirectory(dir);
                    m_logPath = Path.Combine(dir, $"shots-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
                    m_log = new ShotLog
                    {
                        SceneId = sceneId,
                        Device = $"{SystemInfo.deviceModel} ({SystemInfo.operatingSystem})",
                        UnitScale = root.UnitScale,
                    };
                    Debug.Log($"[Recon] Shot log -> {m_logPath}");
                }

                m_log.UnitScale = root.UnitScale;
                m_log.Shots.Add(ShotLogEntry.From(parameters, result, colliderSet,
                                                  sceneLocal, root.UnitScale, sceneId,
                                                  root.IsMetric ? null : "scene NOT metric: no distance here is an accuracy claim"));
                File.WriteAllText(m_logPath, JsonConvert.SerializeObject(m_log, Formatting.Indented));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Recon] Shot log write failed: " + e.Message);
            }
        }

        /// <summary>Removes every arc and impact marker, and forgets the impacts. The log file stays.</summary>
        public void ClearImpacts()
        {
            foreach (var marker in m_markers) if (marker != null) Destroy(marker.gameObject);
            foreach (var arc in m_arcs) if (arc != null) Destroy(arc);
            m_markers.Clear();
            m_arcs.Clear();
            m_impacts.Clear();
            m_impactsPerSet.Clear();
            Status = "cleared";
        }

        void OnDestroy()
        {
            ClearImpacts();
            if (m_arcMaterial != null) Destroy(m_arcMaterial);
            if (m_markerMaterial != null) Destroy(m_markerMaterial);
        }
    }
}
