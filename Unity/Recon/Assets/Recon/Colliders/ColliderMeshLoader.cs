using System;
using System.IO;
using System.Threading.Tasks;
using Recon.Alignment;
using Recon.Config;
using UnityEngine;
using UnityEngine.Rendering;

namespace Recon.Colliders
{
    /// <summary>
    /// Loads the splat-derived collider mesh produced by tools/splat_to_mesh.py and puts it under
    /// SceneRoot as a MeshCollider. This is the research half of handbook Section 09's collider
    /// table; the AR-plane prefab (FTW-69, ArPlane.prefab with a MeshCollider on the ArPlanes layer)
    /// is the baseline half, and the two exist so shots can be fired into both and compared.
    ///
    /// Two things here are deliberate and easy to get wrong:
    ///
    /// 1. The mesh is refused unless its header declares the Unity frame. A .ply that never went
    ///    through tools/convert_ply_to_unity.py is MIRRORED, and a mirrored collider is worse than
    ///    no collider: physics still runs, the scene still looks plausible, and every impact point
    ///    is quietly wrong. docs/FRAMES.md says the client asserts and refuses; this is that.
    /// 2. The mesh is in SCENE UNITS and is added with an identity local transform, so the single
    ///    unitScale on SceneRoot is the only scale applied. Never scale the mesh here, and never
    ///    flip an axis: two conversions cancel out and cost a day.
    /// </summary>
    [AddComponentMenu("Recon/Colliders/Collider Mesh Loader")]
    [DisallowMultipleComponent]
    public sealed class ColliderMeshLoader : MonoBehaviour
    {
        public const string ChildName = "ColliderMesh";

        [SerializeField, Tooltip("Load on Start from persistentDataPath/<devSceneFolder>/<colliderMeshFileName>.")]
        bool loadOnStart;

        [SerializeField, Tooltip("Draw the collider mesh so it can be seen against the splats. Off on device by default: it is collision geometry, not the visual.")]
        bool showDebugMesh;

        [SerializeField, Tooltip("Colour of the debug mesh when it is shown.")]
        Color debugColour = new Color(0.1f, 0.9f, 0.4f, 0.25f);

        GameObject m_child;
        Mesh m_mesh;
        Material m_debugMaterial;
        MeshRenderer m_renderer;

        public string Status { get; private set; } = "no collider mesh loaded";
        public int VertexCount { get; private set; }
        public int TriangleCount { get; private set; }
        public bool IsLoaded => m_child != null;
        public string LoadedPath { get; private set; }

        /// <summary>Bounds of the loaded mesh, in scene units. Empty when nothing is loaded.</summary>
        public Bounds MeshBounds => m_mesh != null ? m_mesh.bounds : new Bounds();

        /// <summary>The default path: the same dev-scene folder push-dev-scene.ps1 writes to.</summary>
        public static string DefaultPath()
        {
            var settings = ReconSettings.Instance;
            return Path.Combine(Application.persistentDataPath, settings.devSceneFolder, settings.colliderMeshFileName);
        }

        async void Start()
        {
            if (!loadOnStart) return;
            try { await LoadAsync(DefaultPath()); }
            catch (Exception e)
            {
                Status = "FAILED: " + e.Message;
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// Reads and parses the .ply on a background thread (a 50,000-triangle mesh would hitch the
        /// frame otherwise), then builds the Mesh and the collider on the main thread, which is the
        /// only place the Unity API may be touched.
        /// </summary>
        public async Task<bool> LoadAsync(string plyPath)
        {
            if (string.IsNullOrEmpty(plyPath))
            {
                Status = "no path given";
                return false;
            }
            if (!File.Exists(plyPath))
            {
                Status = $"No collider mesh at {plyPath}. Produce one with tools/splat_to_mesh.py and push it with Unity/push-dev-scene.ps1.";
                Debug.LogWarning("[Recon] " + Status);
                return false;
            }

            var root = SceneRoot.Find();
            if (root == null)
            {
                Status = "no SceneRoot in the scene";
                return false;
            }

            Status = "reading " + Path.GetFileName(plyPath);
            PlyMesh ply;
            try
            {
                ply = await Task.Run(() => PlyMeshReader.ReadFile(plyPath));
            }
            catch (Exception e)
            {
                Status = "REFUSED: " + e.Message;
                Debug.LogError("[Recon] Collider mesh " + plyPath + " refused: " + e.Message);
                return false;
            }

            if (!ply.DeclaresUnityFrame)
            {
                // The dangerous case, so it is the loud one. See the class comment.
                Status = "REFUSED: " + Path.GetFileName(plyPath) + " does not declare the Unity frame " +
                         "(comments 'Handedness: left' and 'Vertical Axis: y'). An unconverted mesh is mirrored " +
                         "and physics would be silently wrong. Run tools/convert_ply_to_unity.py, then " +
                         "tools/splat_to_mesh.py.";
                Debug.LogError("[Recon] " + Status);
                return false;
            }

            Build(root, ply, plyPath);
            return true;
        }

        void Build(SceneRoot root, PlyMesh ply, string plyPath)
        {
            Unload();

            m_mesh = new Mesh { name = "ColliderMesh (" + Path.GetFileName(plyPath) + ")" };
            // A splat-derived room mesh routinely passes 65,535 vertices; scene 1 came out at 25,127
            // vertices / 50,000 triangles, but a finer Poisson depth or a lower decimation target is
            // straight over the 16-bit limit, and the failure is a mesh that renders as garbage.
            if (ply.VertexCount > 65535) m_mesh.indexFormat = IndexFormat.UInt32;
            m_mesh.vertices = ply.Vertices.ToUnity();
            if (ply.HasNormals) m_mesh.normals = ply.Normals.ToUnity();
            m_mesh.triangles = ply.Triangles;
            if (!ply.HasNormals) m_mesh.RecalculateNormals();
            m_mesh.RecalculateBounds();

            m_child = new GameObject(ChildName);
            m_child.transform.SetParent(root.transform, false);
            // Identity: the mesh is already in scene units and SceneRoot carries unitScale.
            m_child.transform.localPosition = Vector3.zero;
            m_child.transform.localRotation = Quaternion.identity;
            m_child.transform.localScale = Vector3.one;

            int layer = CollisionLayers.SplatMeshLayer;
            if (layer >= 0) m_child.layer = layer;

            var filter = m_child.AddComponent<MeshFilter>();
            filter.sharedMesh = m_mesh;

            var collider = m_child.AddComponent<MeshCollider>();
            collider.sharedMesh = m_mesh;
            collider.convex = false;                  // a room is not convex; a convex hull would fill it in
            collider.isTrigger = false;

            m_debugMaterial = RuntimeMaterials.CreateUnlitTransparent(debugColour, "ColliderMeshDebug");
            m_renderer = m_child.AddComponent<MeshRenderer>();
            m_renderer.sharedMaterial = m_debugMaterial;
            m_renderer.shadowCastingMode = ShadowCastingMode.Off;
            m_renderer.receiveShadows = false;
            m_renderer.enabled = showDebugMesh;

            VertexCount = ply.VertexCount;
            TriangleCount = ply.TriangleCount;
            LoadedPath = plyPath;
            var size = m_mesh.bounds.size;
            Status = $"{VertexCount:N0} vertices / {TriangleCount:N0} triangles on layer " +
                     $"{(layer >= 0 ? LayerMask.LayerToName(layer) : "DEFAULT (layer missing!)")}, " +
                     $"bounds {size.x:F2} x {size.y:F2} x {size.z:F2} scene units" +
                     (root.IsMetric ? $" (unitScale {root.UnitScale:F6})" : " (scene NOT metric)");
            Debug.Log("[Recon] Collider mesh loaded: " + Status);
        }

        /// <summary>Show or hide the collider geometry. Debug affordance only; the collider stays either way.</summary>
        public void SetDebugVisible(bool visible)
        {
            showDebugMesh = visible;
            if (m_renderer != null) m_renderer.enabled = visible;
        }

        public bool DebugVisible => showDebugMesh;

        public void Unload()
        {
            if (m_child != null) Destroy(m_child);
            if (m_mesh != null) Destroy(m_mesh);
            if (m_debugMaterial != null) Destroy(m_debugMaterial);
            m_child = null;
            m_mesh = null;
            m_debugMaterial = null;
            m_renderer = null;
            VertexCount = 0;
            TriangleCount = 0;
            LoadedPath = null;
        }

        void OnDestroy() => Unload();
    }
}
