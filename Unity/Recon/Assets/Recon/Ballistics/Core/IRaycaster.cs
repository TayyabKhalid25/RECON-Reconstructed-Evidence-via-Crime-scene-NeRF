using System.Numerics;

namespace Recon.Ballistics
{
    /// <summary>
    /// The seam between the trajectory integrator and PhysX. The integrator sweeps a ray on every
    /// substep (handbook Section 09: the sweep IS the tunnelling guard), and it does so through
    /// this interface so the integration maths can be tested against the analytic parabola with no
    /// Unity Editor, no scene and no device.
    ///
    /// Vectors are <see cref="System.Numerics.Vector3"/>; Recon.Ballistics.PhysicsRaycaster is the
    /// Unity implementation and does the conversion at the boundary.
    /// </summary>
    public interface IRaycaster
    {
        /// <param name="origin">Start of the sweep, world space.</param>
        /// <param name="direction">Unit direction of the sweep.</param>
        /// <param name="maxDistance">Length of the sweep; the integrator passes the substep length.</param>
        /// <param name="layerMask">Which collider set to test against (see Recon.Colliders.CollisionLayers).</param>
        bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, int layerMask, out RayHit hit);
    }

    /// <summary>What the sweep found. Deliberately smaller than UnityEngine.RaycastHit.</summary>
    public readonly struct RayHit
    {
        public RayHit(Vector3 point, Vector3 normal, float distance, string colliderTag)
        {
            Point = point;
            Normal = normal;
            Distance = distance;
            ColliderTag = colliderTag;
        }

        public Vector3 Point { get; }

        /// <summary>Surface normal at the hit, unit length.</summary>
        public Vector3 Normal { get; }

        /// <summary>Distance from the sweep origin to the hit.</summary>
        public float Distance { get; }

        /// <summary>
        /// Which collider was hit, as a short string. The Unity implementation uses the layer name
        /// ("ArPlanes" or "SplatMesh"), because that is the distinction the Challenge 1 comparison
        /// of AR-plane against splat-derived colliders is made of.
        /// </summary>
        public string ColliderTag { get; }
    }

    /// <summary>
    /// A world with no colliders in it. Used by the parabola test (nothing may interrupt the arc)
    /// and by the app before a collider set exists, where the alternative is a silent no-op.
    /// </summary>
    public sealed class NullRaycaster : IRaycaster
    {
        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, int layerMask, out RayHit hit)
        {
            hit = default;
            return false;
        }
    }
}
