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

        [Tooltip("Seconds before a JSON call gives up. Asset downloads ignore this: a 45 MB .ply over a home uplink outlives any sane timeout, so they run untimed with a progress bar instead. (FTW-70)")]
        [Min(1)] public int apiTimeoutSeconds = 30;

        [Tooltip("Folder under persistentDataPath that FileSceneSource lists and SceneLoader caches downloads into, as <folder>/<sceneId>/. Keeps Unity working before the web DB exists. (FTW-70)")]
        public string sceneCacheFolder = "scenes";

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
