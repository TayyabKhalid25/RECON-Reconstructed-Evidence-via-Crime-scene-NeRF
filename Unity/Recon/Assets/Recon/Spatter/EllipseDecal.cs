using UnityEngine;
using UnityEngine.Rendering;
using NVector3 = System.Numerics.Vector3;

namespace Recon.Spatter
{
    /// <summary>
    /// One bloodstain, as a flat quad whose minor over major axis equals the sine of the incidence
    /// angle. This is the directional proxy from handbook Section 09 and nothing more: the shape
    /// encodes the direction the droplet came from, not any fluid behaviour.
    ///
    /// Geometry, spelled out because it is the part that goes wrong silently:
    ///
    ///   The quad is built in its own XY plane facing +Z (RuntimeMaterials.CreateUnitQuad), so the
    ///   rotation that lays it flat on the surface is LookRotation(surfaceNormal, majorAxis): the
    ///   quad's forward becomes the surface normal, which puts its plane IN the surface, and its up
    ///   becomes the major axis. Local scale is then (baseSize * sin(alpha), baseSize, 1) - X across
    ///   the stain, Y along the major axis, Z flat - so minor / major = sin(alpha) exactly as
    ///   SpatterMath.DecalScale computes it.
    ///
    ///   The decal sits 2 mm off the surface along the normal. Splats have no depth-tested surface,
    ///   so a decal exactly on the plane z-fights with the AR plane and disappears into the splat
    ///   cloud (the last row of the gotcha table in the simulating-ballistics-and-spatter skill).
    /// </summary>
    [AddComponentMenu("")]
    public sealed class EllipseDecal : MonoBehaviour
    {
        /// <summary>minor / major, which is sin(incidence angle).</summary>
        public float AxisRatio { get; private set; }

        /// <summary>Incidence angle in degrees, the arcsine of the ratio. What a report figure quotes.</summary>
        public float IncidenceAngleDegrees { get; private set; }

        /// <summary>Stain position in SceneRoot-local coordinates, scene units.</summary>
        public Vector3 SceneLocalPoint { get; private set; }

        /// <summary>
        /// The quad mesh and the material are passed in and shared by every stain; this object does
        /// not own them. A pattern is dozens of decals per impact, and a mesh and a material each
        /// would be hundreds of objects and hundreds of draw calls on a phone.
        /// </summary>
        public static EllipseDecal Create(Transform sceneRoot, Vector3 worldPoint, Vector3 worldNormal,
                                          NVector3 incomingDirection, float baseSizeMetres,
                                          float normalOffsetMetres, Mesh sharedQuad,
                                          Material sharedMaterial, string name)
        {
            var normal = worldNormal.sqrMagnitude > 0f ? worldNormal.normalized : Vector3.up;
            var major = SpatterMath.MajorAxisDirection(incomingDirection, normal.ToNumerics()).ToUnity();
            if (major.sqrMagnitude <= 0f) major = Vector3.Cross(normal, Vector3.up).normalized;

            var scale = SpatterMath.DecalScale(incomingDirection, normal.ToNumerics(), baseSizeMetres).ToUnity();

            var go = new GameObject(name);
            go.transform.SetParent(sceneRoot, false);
            go.transform.position = worldPoint + normal * normalOffsetMetres;
            go.transform.rotation = Quaternion.LookRotation(normal, major);

            // Metres in the room, on a transform that SceneRoot scales by unitScale. Sizing an
            // overlay, not scaling geometry: the stain POSITION is untouched.
            float unitsPerMetre = Ballistics.ImpactMarker.LocalScaleFactor(sceneRoot);
            go.transform.localScale = new Vector3(scale.x * unitsPerMetre, scale.y * unitsPerMetre, 1f);

            go.AddComponent<MeshFilter>().sharedMesh = sharedQuad;
            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = sharedMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            var decal = go.AddComponent<EllipseDecal>();
            decal.AxisRatio = SpatterMath.EllipseAxisRatio(incomingDirection, normal.ToNumerics());
            decal.IncidenceAngleDegrees = SpatterMath.IncidenceAngleDegrees(incomingDirection, normal.ToNumerics());
            decal.SceneLocalPoint = go.transform.localPosition;
            return decal;
        }
    }
}
