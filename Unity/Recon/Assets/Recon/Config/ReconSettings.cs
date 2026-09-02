using UnityEngine;

namespace Recon.Config
{
    /// <summary>
    /// Project-wide settings as one asset at Assets/Recon/Config/Resources/ReconSettings.asset
    /// (created by Recon → Bootstrap ReconAR scene). Loaded through <see cref="Instance"/>; a
    /// missing asset falls back to defaults so a fresh clone still runs.
    /// </summary>
    [CreateAssetMenu(fileName = "ReconSettings", menuName = "Recon/Settings")]
    public sealed class ReconSettings : ScriptableObject
    {
        public const string ResourceName = "ReconSettings";

        [Header("Web API")]
        [Tooltip("Base URL of the RECON web app over the tailnet. The dev API runs on the Legion (docs/NETWORK.md).")]
        public string apiBaseUrl = "http://legion:3000";

        [Header("Marker (docs/FRAMES.md)")]
        [Tooltip("Measured outer edge of the printed marker in metres. 0.170 as measured 2026-08-23. Leaving physicalSize unset makes ARCore estimate it and the scale drifts.")]
        public float markerPhysicalSizeMetres = 0.170f;
        public string markerReferenceName = "marker-a4-170mm";

        [Header("Dev scene for FTW-30 (relative to persistentDataPath)")]
        [Tooltip("Folder the FTW-30 measurement reads splat_unity.ply and metadata.json from. Push with Unity/push-dev-scene.ps1.")]
        public string devSceneFolder = "scenes/dev";
        public string plyFileName = "splat_unity.ply";
        public string metadataFileName = "metadata.json";

        [Header("Layers (TagManager)")]
        public string arPlanesLayer = "ArPlanes";
        public string splatMeshLayer = "SplatMesh";

        [Header("Physics and ballistics (FTW-71)")]
        [Tooltip("Collider mesh from tools/splat_to_mesh.py, in the dev scene folder next to the .ply. SCENE UNITS like the splat: SceneRoot applies unitScale to both.")]
        public string colliderMeshFileName = "collider_mesh.ply";

        [Tooltip("Folder under persistentDataPath for the shot logs the Challenge 1 study reads back. Pull with adb, same as perf/.")]
        public string shotLogFolder = "shots";

        static ReconSettings s_instance;

        public static ReconSettings Instance
        {
            get
            {
                if (s_instance != null) return s_instance;
                s_instance = Resources.Load<ReconSettings>(ResourceName);
                if (s_instance == null)
                {
                    Debug.LogWarning("[Recon] No ReconSettings asset in a Resources folder; using defaults. Run Recon → Bootstrap ReconAR scene once.");
                    s_instance = CreateInstance<ReconSettings>();
                }
                return s_instance;
            }
        }
    }
}
