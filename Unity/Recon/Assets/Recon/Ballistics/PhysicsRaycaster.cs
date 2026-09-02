using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace Recon.Ballistics
{
    /// <summary>
    /// The Unity end of <see cref="IRaycaster"/>: the integrator's per-substep sweep, done by PhysX.
    /// This is the only place the ballistics code touches UnityEngine.Physics, which is what lets
    /// the integrator be tested against the analytic parabola with no scene and no device.
    ///
    /// Triggers are ignored. An AR plane or a splat mesh collider is real geometry; a trigger volume
    /// in the scene is a game-logic construct and a bullet stopping in mid-air on one would look
    /// exactly like the tunnelling bug this whole design exists to avoid.
    /// </summary>
    public sealed class PhysicsRaycaster : IRaycaster
    {
        readonly QueryTriggerInteraction m_triggers;

        public PhysicsRaycaster(QueryTriggerInteraction triggers = QueryTriggerInteraction.Ignore)
        {
            m_triggers = triggers;
        }

        /// <summary>How many sweeps this raycaster has performed; one per substep, so it grows fast.</summary>
        public int Sweeps { get; private set; }

        /// <summary>The collider the last reported hit belongs to, for callers that need the object.</summary>
        public Collider LastCollider { get; private set; }

        public bool Raycast(NVector3 origin, NVector3 direction, float maxDistance, int layerMask, out RayHit hit)
        {
            Sweeps++;
            hit = default;

            var from = origin.ToUnity();
            var along = direction.ToUnity();
            if (along.sqrMagnitude <= 0f) return false;

            if (!Physics.Raycast(from, along, out RaycastHit unityHit, maxDistance, layerMask, m_triggers))
                return false;

            LastCollider = unityHit.collider;
            hit = new RayHit(
                unityHit.point.ToNumerics(),
                unityHit.normal.ToNumerics(),
                unityHit.distance,
                TagOf(unityHit.collider));
            return true;
        }

        /// <summary>
        /// The collider's layer name, so an impact says which collider SET produced it. That is the
        /// distinction the Challenge 1 comparison is made of (AR planes against the splat-derived
        /// mesh), and a layer name survives into the shot log as readable text.
        /// </summary>
        static string TagOf(Collider collider)
        {
            if (collider == null) return "unknown";
            var layerName = LayerMask.LayerToName(collider.gameObject.layer);
            return string.IsNullOrEmpty(layerName) ? collider.gameObject.name : layerName;
        }
    }
}
