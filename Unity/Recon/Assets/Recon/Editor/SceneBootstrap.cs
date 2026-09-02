using System;
using System.IO;
using System.Linq;
using Recon.Alignment;
using Recon.Config;
using Recon.Diagnostics;
using Recon.Scenes;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.ARSubsystems;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Recon.Editor
{
    /// <summary>
    /// Creates Assets/Recon/Scenes/ReconAR.unity from script, so the app scene is reproducible and
    /// never hand-edited YAML with package GUIDs in a PR. Idempotent: rerunning updates the assets
    /// it owns and rewrites the scene. Menu: Recon → Bootstrap ReconAR scene, or headless:
    ///   Unity.exe -batchmode -projectPath Unity/Recon -executeMethod Recon.Editor.SceneBootstrap.CreateReconArSceneBatch
    ///
    /// SampleScene.unity is Tayyab's and is never touched here.
    /// </summary>
    public static class SceneBootstrap
    {
        public const string ReconFolder = "Assets/Recon";
        public const string ScenePath = ReconFolder + "/Scenes/ReconAR.unity";
        public const string SettingsPath = ReconFolder + "/Config/Resources/ReconSettings.asset";
        public const string LibraryPath = ReconFolder + "/Markers/ReconReferenceImageLibrary.asset";
        // Named so the repo-wide `marker-*` ignore rules for generator output do not swallow it (FTW-72).
        public const string MarkerTexturePath = ReconFolder + "/Markers/recon-marker-170mm.png";
        public const string PlanePrefabPath = ReconFolder + "/Prefabs/ArPlane.prefab";
        public const string PlaneMaterialPath = ReconFolder + "/Prefabs/ArPlane.mat";

        const string UnitySplatsAdapterType = "Recon.Rendering.UnitySplats.UnitySplatsRenderer, Recon.Rendering.UnitySplats";
        const string ArCommandBufferFeature = "UnityEngine.XR.ARFoundation.ARCommandBufferSupportRendererFeature, Unity.XR.ARFoundation";
        const string GsplatUrpFeature = "Gsplat.GsplatURPFeature, Gsplat";

        [MenuItem("Recon/Bootstrap ReconAR scene")]
        public static void CreateReconArScene()
        {
            EnsureFolders();
            var settings = EnsureSettings();
            EnsureLayers(settings);
            EnsureRendererFeatures();
            var library = EnsureReferenceImageLibrary(settings);
            var planePrefab = EnsurePlanePrefab(settings);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateArSession();
            var origin = CreateXrOrigin();
            ConfigureTrackables(origin.gameObject, library, planePrefab);

            var root = new GameObject("SceneRoot").AddComponent<SceneRoot>();
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var recon = new GameObject("Recon");
            recon.AddComponent<FpsCounter>();
            recon.AddComponent<PerfLog>();
            recon.AddComponent<DevSplatLoader>();
            AddAdapter(recon);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Recon] Bootstrapped {ScenePath}. Build with Unity/build-android.ps1; push a dev .ply with Unity/push-dev-scene.ps1.");
        }

        public static void CreateReconArSceneBatch()
        {
            try
            {
                CreateReconArScene();
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[Recon] Bootstrap failed: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        static void EnsureFolders()
        {
            foreach (var f in new[] { "Scenes", "Prefabs", "Config", "Config/Resources", "Markers" })
            {
                var path = ReconFolder + "/" + f;
                if (!AssetDatabase.IsValidFolder(path))
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
            }
        }

        static ReconSettings EnsureSettings()
        {
            var s = AssetDatabase.LoadAssetAtPath<ReconSettings>(SettingsPath);
            if (s == null)
            {
                s = ScriptableObject.CreateInstance<ReconSettings>();
                AssetDatabase.CreateAsset(s, SettingsPath);
                Debug.Log($"[Recon] Created {SettingsPath}");
            }
            return s;
        }

        static void EnsureLayers(ReconSettings s)
        {
            foreach (var layer in new[] { s.arPlanesLayer, s.splatMeshLayer })
                if (LayerMask.NameToLayer(layer) < 0)
                    Debug.LogError($"[Recon] Layer '{layer}' is missing from ProjectSettings/TagManager.asset (user layers 8 and 9 in FTW-69).");
        }

        static void EnsureRendererFeatures()
        {
            var data = RendererFeatureUtil.ActiveRendererData();
            if (data == null) return;
            RendererFeatureUtil.EnsureFeature(data, ArCommandBufferFeature);
            RendererFeatureUtil.EnsureFeature(data, GsplatUrpFeature);
        }

        static XRReferenceImageLibrary EnsureReferenceImageLibrary(ReconSettings s)
        {
            var lib = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<XRReferenceImageLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(MarkerTexturePath);
            bool hasEntry = false;
            for (int i = 0; i < lib.count; i++) if (lib[i].name == s.markerReferenceName) hasEntry = true;
            // ARCore's build processor fails the whole build on an entry without a texture
            // (ArCoreImg.MissingTextureException), so an entry is only ever created with one.
            if (texture == null)
            {
                Debug.LogWarning($"[Recon] No marker texture at {MarkerTexturePath}; no reference image entry created (ARCore refuses textureless entries at build time). FTW-72 supplies the PNG.");
            }
            else if (!hasEntry)
            {
                lib.Add();
                int idx = lib.count - 1;
                lib.SetName(idx, s.markerReferenceName);
                lib.SetSpecifySize(idx, true);
                lib.SetSize(idx, new Vector2(s.markerPhysicalSizeMetres, s.markerPhysicalSizeMetres));
                lib.SetTexture(idx, texture, true);
            }
            else
            {
                for (int i = 0; i < lib.count; i++)
                    if (lib[i].name == s.markerReferenceName && lib[i].texture == null) lib.SetTexture(i, texture, true);
            }
            EditorUtility.SetDirty(lib);
            return lib;
        }

        static GameObject EnsurePlanePrefab(ReconSettings s)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(PlaneMaterialPath);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                mat = new Material(shader) { name = "ArPlane" };
                mat.SetFloat("_Surface", 1f);     // transparent
                mat.SetFloat("_Blend", 0f);       // alpha
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)RenderQueue.Transparent;
                mat.SetColor("_BaseColor", new Color(0.2f, 0.9f, 1f, 0.18f));
                AssetDatabase.CreateAsset(mat, PlaneMaterialPath);
            }

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PlanePrefabPath);
            if (existing != null) return existing;

            var go = new GameObject("ArPlane");
            int layer = LayerMask.NameToLayer(s.arPlanesLayer);
            if (layer >= 0) go.layer = layer;
            go.AddComponent<ARPlane>();
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.AddComponent<ARPlaneMeshVisualizer>();
            go.AddComponent<MeshCollider>();   // the ArPlanes baseline collider set for ballistics
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PlanePrefabPath);
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        static void CreateArSession()
        {
            if (UnityEngine.Object.FindFirstObjectByType<ARSession>() != null) return;
            if (EditorApplication.ExecuteMenuItem("GameObject/XR/AR Session")) return;
            var go = new GameObject("AR Session");
            go.AddComponent<ARSession>();
            var inputManager = Type.GetType("UnityEngine.XR.ARFoundation.ARInputManager, Unity.XR.ARFoundation");
            if (inputManager != null) go.AddComponent(inputManager);
        }

        static XROrigin CreateXrOrigin()
        {
            var existing = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
            if (existing != null) return existing;

            if (EditorApplication.ExecuteMenuItem("GameObject/XR/XR Origin (Mobile AR)"))
            {
                var created = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
                if (created != null) return created;
            }

            // Manual fallback, equivalent to AR Foundation's "XR Origin (Mobile AR)" menu item.
            var originGo = new GameObject("XR Origin (Mobile AR)");
            var origin = originGo.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originGo.transform, false);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(offset.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 30f;
            camGo.AddComponent<ARCameraManager>();
            camGo.AddComponent<ARCameraBackground>();
            var tpd = camGo.AddComponent<TrackedPoseDriver>();
            tpd.positionInput = new InputActionProperty(new InputAction("Position", binding: "<HandheldARInputDevice>/devicePosition"));
            tpd.rotationInput = new InputActionProperty(new InputAction("Rotation", binding: "<HandheldARInputDevice>/deviceRotation"));
            origin.Camera = cam;
            origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            return origin;
        }

        static void ConfigureTrackables(GameObject originGo, XRReferenceImageLibrary library, GameObject planePrefab)
        {
            var images = originGo.GetComponent<ARTrackedImageManager>() ?? originGo.AddComponent<ARTrackedImageManager>();
            images.referenceLibrary = library;
            images.requestedMaxNumberOfMovingImages = 1;
            images.trackedImagePrefab = null;

            var planes = originGo.GetComponent<ARPlaneManager>() ?? originGo.AddComponent<ARPlaneManager>();
            planes.planePrefab = planePrefab;
            planes.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;

            if (originGo.GetComponent<ARRaycastManager>() == null) originGo.AddComponent<ARRaycastManager>();
            if (originGo.GetComponent<ARAnchorManager>() == null) originGo.AddComponent<ARAnchorManager>();
        }

        static void AddAdapter(GameObject recon)
        {
            var adapter = Type.GetType(UnitySplatsAdapterType);
            if (adapter != null) recon.AddComponent(adapter);
            else Debug.LogWarning("[Recon] UnitySplats adapter not compiled (package missing from manifest?). The scene has no ISplatRenderer.");
        }

        static void AddToBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != scenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
