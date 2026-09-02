using System.Collections.Generic;
using Recon.Alignment;
using Recon.Ballistics;
using Recon.Colliders;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace Recon.Spatter
{
    /// <summary>
    /// On an impact, emits droplets in a cone around the incoming direction, raycasts each one, and
    /// places an ellipse decal wherever one lands. Handbook Section 09, "Blood spatter, the actual
    /// formula": "emit droplets from an origin with a spread of directions and speeds, raycast each
    /// one, and at the hit point place an ellipse decal whose minor over major axis equals the sine
    /// of the incidence angle, with the major axis aligned to the incoming direction projected onto
    /// the surface."
    ///
    /// Droplets travel in straight lines here. Over the tens of centimetres a stain pattern spans,
    /// gravity moves a droplet by well under the size of the stain, and adding an integrator would
    /// imply a physical model this proxy does not have. That is a scope decision, not an oversight.
    ///
    /// Attach next to a ShotController and it wires itself to that controller's impacts.
    /// </summary>
    [AddComponentMenu("Recon/Spatter/Spatter Emitter")]
    [DisallowMultipleComponent]
    public sealed class SpatterEmitter : MonoBehaviour
    {
        [SerializeField, Tooltip("Emit on every impact from the ShotController on this object.")]
        bool emitOnImpact = true;

        [SerializeField, Range(1, 256), Tooltip("Droplets per impact. Each one is a raycast.")]
        int dropletCount = 48;

        [SerializeField, Range(0f, 90f), Tooltip("Half angle of the emission cone around the incoming direction, degrees.")]
        float coneHalfAngleDegrees = 35f;

        [SerializeField, Tooltip("How far a droplet may travel before it is discarded, metres.")]
        float dropletRangeMetres = 1.5f;

        [SerializeField, Tooltip("Long axis of a head-on stain, metres. 2 cm is a large drop; the minor axis is this times sin(alpha).")]
        float baseSizeMetres = 0.02f;

        [SerializeField, Tooltip("Offset along the surface normal, metres. Splats have no depth surface, so a decal flat on the plane z-fights and vanishes.")]
        float normalOffsetMetres = 0.002f;

        [SerializeField] Color decalColour = new Color(0.45f, 0.03f, 0.05f, 0.75f);

        [SerializeField, Tooltip("Seed for the cone sampling. The same seed gives the same pattern, so a figure in the report is reproducible.")]
        int seed = 20260902;

        [SerializeField, Tooltip("Oldest decals are destroyed past this, so a long session cannot fill the scene.")]
        int maxDecals = 600;

        [SerializeField, Tooltip("Which colliders droplets may land on. Usually the same set the shot was fired at.")]
        ColliderSet dropletColliderSet = ColliderSet.Both;

        readonly List<EllipseDecal> m_decals = new List<EllipseDecal>();
        Mesh m_quad;
        Material m_material;
        PhysicsRaycaster m_raycaster;
        ShotController m_controller;
        int m_emissions;

        public string Status { get; private set; } = "no spatter yet";
        public int DecalCount => m_decals.Count;

        public bool EmitOnImpact
        {
            get => emitOnImpact;
            set => emitOnImpact = value;
        }

        void Awake() => m_raycaster = new PhysicsRaycaster();

        void OnEnable()
        {
            m_controller = GetComponent<ShotController>();
            if (m_controller != null) m_controller.Impacted += HandleImpact;
        }

        void OnDisable()
        {
            if (m_controller != null) m_controller.Impacted -= HandleImpact;
            m_controller = null;
        }

        void HandleImpact(ImpactRecord impact)
        {
            if (emitOnImpact) Emit(impact);
        }

        /// <summary>
        /// Emits one pattern for an impact and returns how many stains landed. Public so a batch
        /// harness (FTW, Phase 5) can drive it without a UI.
        /// </summary>
        public int Emit(ImpactRecord impact)
        {
            if (impact == null) return 0;

            var root = SceneRoot.Find();
            if (root == null)
            {
                Status = "no SceneRoot in the scene";
                return 0;
            }

            int mask = CollisionLayers.MaskFor(dropletColliderSet);
            if (mask == 0)
            {
                Status = $"{dropletColliderSet} has no layer, so droplets cannot land anywhere";
                Debug.LogError("[Recon] " + Status);
                return 0;
            }

            var axis = impact.IncomingVelocity;
            if (axis.Length() <= 0f) axis = -impact.Normal;
            m_emissions++;

            // Deterministic per emission: the same shot in a rerun gives the same pattern, but two
            // impacts in one session do not get identical droplet directions.
            var directions = SpatterMath.DropletDirections(axis, coneHalfAngleDegrees, dropletCount, seed + m_emissions);

            // Start a hair off the surface along its normal, or every droplet immediately reports a
            // hit on the surface it was born on, at zero distance.
            //
            // What this produces is worth knowing before looking at it: a head-on impact throws its
            // droplets straight back into the same surface, so they land within a centimetre or two
            // as near-round stains, while an oblique impact sends them along the surface and they
            // land further away as elongated ones. That spread of ratios IS the sin(alpha)
            // relationship being visible, which is the point of the proxy.
            var origin = impact.Point + impact.Normal * normalOffsetMetres;

            int landed = 0;
            foreach (var direction in directions)
            {
                if (!m_raycaster.Raycast(origin, direction, dropletRangeMetres, mask, out RayHit hit)) continue;
                Place(root.transform, hit, direction);
                landed++;
            }

            Status = $"impact {m_emissions}: {landed} of {dropletCount} droplets landed, " +
                     $"incidence {impact.IncidenceAngleDegrees:F1} deg, cone {coneHalfAngleDegrees:F0} deg";
            return landed;
        }

        void Place(Transform root, RayHit hit, NVector3 direction)
        {
            if (m_quad == null) m_quad = RuntimeMaterials.CreateUnitQuad();
            if (m_material == null) m_material = RuntimeMaterials.CreateUnlitTransparent(decalColour, "EllipseDecal");

            var decal = EllipseDecal.Create(root, hit.Point.ToUnity(), hit.Normal.ToUnity(), direction,
                                            baseSizeMetres, normalOffsetMetres, m_quad, m_material,
                                            $"Stain {m_decals.Count + 1} ({hit.ColliderTag})");
            m_decals.Add(decal);

            while (m_decals.Count > maxDecals)
            {
                var oldest = m_decals[0];
                m_decals.RemoveAt(0);
                if (oldest != null) Destroy(oldest.gameObject);
            }
        }

        public void ClearDecals()
        {
            foreach (var decal in m_decals) if (decal != null) Destroy(decal.gameObject);
            m_decals.Clear();
            Status = "cleared";
        }

        void OnDestroy()
        {
            ClearDecals();
            if (m_quad != null) Destroy(m_quad);
            if (m_material != null) Destroy(m_material);
        }
    }
}
