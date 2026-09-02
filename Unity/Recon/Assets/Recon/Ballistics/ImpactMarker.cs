using Recon.Colliders;
using UnityEngine;
using UnityEngine.Rendering;

namespace Recon.Ballistics
{
    /// <summary>
    /// The little sphere left where a shot landed, and the record of that landing. Created at runtime
    /// so the physics overlay needs no prefab (one person owns a prefab at a time, and this way there
    /// is nothing to own).
    ///
    /// It carries the impact in BOTH frames, because the Challenge 1 divergence study compares impact
    /// points between collider sets and must not depend on where the phone happened to start:
    /// SceneLocalPoint is SceneRoot-local in scene units, Impact.Point is world metres.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class ImpactMarker : MonoBehaviour
    {
        /// <summary>Impact as the integrator found it: world metres.</summary>
        public ImpactRecord Impact { get; private set; }

        /// <summary>The same point in SceneRoot-local coordinates, in scene units.</summary>
        public Vector3 SceneLocalPoint { get; private set; }

        /// <summary>Which collider set the shot was aimed at.</summary>
        public ColliderSet Set { get; private set; }

        /// <summary>Sequence number within the session, so a log line and a marker can be matched up.</summary>
        public int ShotNumber { get; private set; }

        /// <summary>
        /// A sphere of <paramref name="diameterMetres"/> (2 cm by default: a bullet hole, not a
        /// beach ball), parented under SceneRoot so it moves with the twin when the marker realigns.
        ///
        /// The local scale divides out unitScale on purpose. That is not a second scale applied to
        /// the scene: the marker is an overlay gizmo that must read 2 cm in the real room, and
        /// SceneRoot scales scene units to metres. The impact POSITION is never touched.
        ///
        /// The material is passed in and shared by every marker, and this object does not own it: a
        /// session with a few hundred impacts should not create a few hundred materials on a phone.
        /// </summary>
        public static ImpactMarker Create(Transform sceneRoot, ImpactRecord impact, ColliderSet set,
                                          int shotNumber, float diameterMetres, Material sharedMaterial)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"ImpactMarker {shotNumber} ({set})";

            // A collider on the marker would be hit by the next shot and by every spatter droplet.
            var stray = go.GetComponent<Collider>();
            if (stray != null) Object.Destroy(stray);

            go.transform.SetParent(sceneRoot, false);
            go.transform.localPosition = sceneRoot.InverseTransformPoint(impact.Point.ToUnity());
            go.transform.localRotation = Quaternion.identity;

            float unitsPerMetre = LocalScaleFactor(sceneRoot);
            go.transform.localScale = Vector3.one * (diameterMetres * unitsPerMetre);

            var meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = sharedMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            var marker = go.AddComponent<ImpactMarker>();
            marker.Impact = impact;
            marker.SceneLocalPoint = go.transform.localPosition;
            marker.Set = set;
            marker.ShotNumber = shotNumber;
            return marker;
        }

        /// <summary>
        /// Scene units per metre for a child of SceneRoot: 1 / unitScale when the scene is metric,
        /// 1 when it is not. Used to size overlay gizmos, never to move geometry.
        /// </summary>
        public static float LocalScaleFactor(Transform sceneRoot)
        {
            float scale = sceneRoot != null ? sceneRoot.lossyScale.x : 1f;
            return scale > 1e-6f ? 1f / scale : 1f;
        }
    }
}
