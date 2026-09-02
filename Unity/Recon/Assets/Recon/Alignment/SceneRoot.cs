using UnityEngine;

namespace Recon.Alignment
{
    /// <summary>
    /// The one origin of the digital twin. Everything the user sees — the splat renderer, the
    /// collider mesh, impact markers, spatter decals, POIs — is a child of this transform, in
    /// scene units. Aligners (marker, cloud anchor, manual) move this object; nothing else does.
    ///
    /// <see cref="ApplyUnitScale"/> is the ONLY place scene units become metres
    /// (docs/FRAMES.md). If the twin is the wrong size, the bug is in metadata.unitScale, not here.
    /// </summary>
    [AddComponentMenu("Recon/Alignment/Scene Root")]
    [DisallowMultipleComponent]
    public sealed class SceneRoot : MonoBehaviour
    {
        [SerializeField, Tooltip("Metres per scene unit, from metadata.json. 0 means not metric and nothing has been applied.")]
        float unitScale;

        public float UnitScale => unitScale;
        public bool IsMetric => unitScale > 0f;

        /// <summary>Applies unitScale from validated metadata. Refuses non-positive values loudly.</summary>
        public void ApplyUnitScale(double metresPerSceneUnit)
        {
            if (!(metresPerSceneUnit > 0) || double.IsInfinity(metresPerSceneUnit) || double.IsNaN(metresPerSceneUnit))
            {
                Debug.LogError($"[Recon] SceneRoot refused unitScale {metresPerSceneUnit}: a non-metric scene must not be scaled by guesswork (docs/FRAMES.md).");
                return;
            }
            unitScale = (float)metresPerSceneUnit;
            transform.localScale = Vector3.one * unitScale;
        }

        /// <summary>Marks the scene non-metric (manual placement, or metadata rejected). Children stay in scene units, unscaled.</summary>
        public void ClearUnitScale()
        {
            unitScale = 0f;
            transform.localScale = Vector3.one;
        }

        /// <summary>Set by an aligner: world pose of the scene origin.</summary>
        public void SetPose(Pose worldPose)
        {
            transform.SetPositionAndRotation(worldPose.position, worldPose.rotation);
        }

        static SceneRoot s_instance;

        /// <summary>The scene's SceneRoot, or null with a loud log. Aligners and loaders use this.</summary>
        public static SceneRoot Find()
        {
            if (s_instance != null) return s_instance;
            s_instance = FindFirstObjectByType<SceneRoot>(FindObjectsInactive.Exclude);
            if (s_instance == null) Debug.LogError("[Recon] No SceneRoot in the scene. Run Recon → Bootstrap ReconAR scene.");
            return s_instance;
        }

        void OnDestroy() { if (s_instance == this) s_instance = null; }
    }
}
