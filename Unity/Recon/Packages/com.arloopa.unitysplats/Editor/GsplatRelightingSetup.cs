// Copyright (c) 2026 ARLOOPA
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat.Editor
{
    [InitializeOnLoad]
    internal static class GsplatRelightingSetup
    {
        internal const string ProxyLayerName = "UnitySplatsRelighting";
        const string k_generatedRoot = "Assets/UnitySplats";
        const string k_generatedFolder = "Assets/UnitySplats/GeneratedRelighting";

        static GsplatRelightingSetup()
        {
            ObjectFactory.componentWasAdded += OnComponentWasAdded;
        }

        static void OnComponentWasAdded(Component component)
        {
            if (component is not GsplatProxyRelighting relighting)
                return;

            // Component creation callbacks run while Unity is still finalizing the Add Component
            // operation. Configure on the next editor tick so parenting and Undo are stable.
            EditorApplication.delayCall += () =>
            {
                if (relighting)
                    Configure(relighting);
            };
        }

        internal static bool Configure(
            GsplatProxyRelighting relighting,
            bool logMissingRequirements = true)
        {
            if (!relighting)
                return false;

            GsplatRenderer renderer = relighting.SplatRenderer;
            if (!renderer)
            {
                if (logMissingRequirements)
                    Debug.LogError(
                        $"[UnitySplats] Gaussian Relighting must be added to a proxy mesh child of a GameObject with GsplatRenderer. Move '{relighting.name}' under the Gaussian renderer.",
                        relighting);
                return false;
            }

            Camera camera = relighting.SourceCamera;
            if (!camera)
            {
                camera = FindDefaultCamera();
                if (camera)
                {
                    Undo.RecordObject(relighting, "Assign Gaussian Relighting Camera");
                    relighting.SourceCamera = camera;
                    EditorUtility.SetDirty(relighting);
                }
            }

            if (!camera)
            {
                if (logMissingRequirements)
                    Debug.LogWarning(
                        $"[UnitySplats] '{relighting.name}' needs an active Game camera. Assign Source Camera on Gaussian Relighting.",
                        relighting);
                return false;
            }

            GsplatRelighting source = camera.GetComponent<GsplatRelighting>();
            if (!source)
                source = Undo.AddComponent<GsplatRelighting>(camera.gameObject);
            Undo.RecordObject(source, "Configure Gaussian Relighting Capture");
            source.hideFlags |= HideFlags.HideInInspector;

            int layer = FindOrCreateProxyLayer();
            if (layer < 0)
            {
                Debug.LogError(
                    "[UnitySplats] No free user layer is available for Gaussian Relighting. Create a user layer for the proxy mesh.",
                    relighting);
                return false;
            }

            source.ProxyLayers |= 1 << layer;
            EditorUtility.SetDirty(source);

            if ((camera.cullingMask & (1 << layer)) != 0)
            {
                Undo.RecordObject(camera, "Hide Gaussian Relighting Proxy");
                camera.cullingMask &= ~(1 << layer);
                EditorUtility.SetDirty(camera);
            }

            Transform proxy = relighting.transform;

            Material material = GetOrCreateProxyMaterial();
            if (!material)
                return false;

            SetLayerRecursively(proxy, layer);
            var proxyRenderers = proxy.GetComponentsInChildren<Renderer>(true)
                .Where(value => value is MeshRenderer or SkinnedMeshRenderer)
                .ToArray();
            if (proxyRenderers.Length == 0)
            {
                Debug.LogError(
                    $"[UnitySplats] Relighting Proxy '{proxy.name}' contains no MeshRenderer or SkinnedMeshRenderer.",
                    proxy);
                return false;
            }

            Undo.RecordObjects(proxyRenderers, "Configure Gsplat Relighting Proxy");
            foreach (var proxyRenderer in proxyRenderers)
            {
                int materialCount = Mathf.Max(1, proxyRenderer.sharedMaterials.Length);
                var materials = new Material[materialCount];
                Array.Fill(materials, material);
                proxyRenderer.sharedMaterials = materials;
                proxyRenderer.shadowCastingMode = ShadowCastingMode.On;
                proxyRenderer.receiveShadows = true;
                EditorUtility.SetDirty(proxyRenderer);
            }

            source.Refresh();
            relighting.Refresh();
            renderer.ForceRefresh();
            return true;
        }

        internal static void ExcludeProxyLayersFromSourceCamera(GsplatRelighting relighting)
        {
            if (!relighting)
                return;
            Camera camera = relighting.GetComponent<Camera>();
            if (!camera)
                return;

            Undo.RecordObject(camera, "Hide Gsplat Relighting Proxy");
            camera.cullingMask &= ~relighting.ProxyLayers.value;
            EditorUtility.SetDirty(camera);
            relighting.Refresh();
        }

        static Camera FindDefaultCamera()
        {
            if (Camera.main && Camera.main.cameraType == CameraType.Game)
                return Camera.main;

            return UnityEngine.Object.FindObjectsByType<Camera>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None)
                .FirstOrDefault(camera => camera.cameraType == CameraType.Game &&
                                          camera.isActiveAndEnabled);
        }

        static int FindOrCreateProxyLayer()
        {
            int existing = LayerMask.NameToLayer(ProxyLayerName);
            if (existing >= 0)
                return existing;

            UnityEngine.Object tagManagerAsset =
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")
                    .FirstOrDefault();
            if (!tagManagerAsset)
                return -1;

            var tagManager = new SerializedObject(tagManagerAsset);
            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layers == null || !layers.isArray)
                return -1;

            for (int index = Mathf.Min(31, layers.arraySize - 1); index >= 8; --index)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(index);
                if (!string.IsNullOrEmpty(layer.stringValue))
                    continue;

                layer.stringValue = ProxyLayerName;
                tagManager.ApplyModifiedProperties();
                return index;
            }

            return -1;
        }

        static void SetLayerRecursively(Transform root, int layer)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                Undo.RecordObject(child.gameObject, "Set Gsplat Relighting Proxy Layer");
                child.gameObject.layer = layer;
                EditorUtility.SetDirty(child.gameObject);
            }
        }

        static Material GetOrCreateProxyMaterial()
        {
            Shader shader = FindLitShader();
            if (!shader)
            {
                Debug.LogError(
                    "[UnitySplats] Could not find the active render pipeline's Lit shader for the relighting proxy.");
                return null;
            }

            EnsureGeneratedFolder();
            string shaderKey = string.Concat(shader.name.Select(character =>
                char.IsLetterOrDigit(character) ? character : '_'));
            string path = $"{k_generatedFolder}/UnitySplatsRelightingProxy_{shaderKey}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(shader)
                {
                    name = $"UnitySplats Relighting Proxy ({shader.name})",
                    enableInstancing = true
                };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            var gray = new Color(0.5f, 0.5f, 0.5f, 1f);
            SetColorIfPresent(material, "_BaseColor", gray);
            SetColorIfPresent(material, "_Color", gray);
            SetFloatIfPresent(material, "_Surface", 0f);
            SetFloatIfPresent(material, "_SurfaceType", 0f);
            SetFloatIfPresent(material, "_AlphaClip", 0f);
            SetFloatIfPresent(material, "_Metallic", 0f);
            SetFloatIfPresent(material, "_Smoothness", 0f);
            SetFloatIfPresent(material, "_Glossiness", 0f);
            SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.One);
            SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.Zero);
            SetFloatIfPresent(material, "_ZWrite", 1f);
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = -1;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        static Shader FindLitShader()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline
                ? GraphicsSettings.currentRenderPipeline
                : GraphicsSettings.defaultRenderPipeline;
            string pipelineName = pipeline
                ? pipeline.GetType().FullName ?? string.Empty
                : string.Empty;
            if (pipelineName.Contains("HighDefinition", StringComparison.OrdinalIgnoreCase))
                return Shader.Find("HDRP/Lit");
            if (pipelineName.Contains("Universal", StringComparison.OrdinalIgnoreCase))
                return Shader.Find("Universal Render Pipeline/Lit");
            return Shader.Find("Standard");
        }

        static void EnsureGeneratedFolder()
        {
            if (!AssetDatabase.IsValidFolder(k_generatedRoot))
                AssetDatabase.CreateFolder("Assets", "UnitySplats");
            if (!AssetDatabase.IsValidFolder(k_generatedFolder))
                AssetDatabase.CreateFolder(k_generatedRoot, "GeneratedRelighting");
        }

        static void SetColorIfPresent(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
                material.SetColor(property, value);
        }

        static void SetFloatIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property))
                material.SetFloat(property, value);
        }
    }

    [CustomEditor(typeof(GsplatRelighting))]
    internal sealed class GsplatRelightingEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            bool changed = serializedObject.ApplyModifiedProperties();

            var relighting = (GsplatRelighting)target;
            Camera camera = relighting.GetComponent<Camera>();
            int overlap = camera ? camera.cullingMask & relighting.ProxyLayers.value : 0;
            if (relighting.ProxyLayers.value == 0)
            {
                EditorGUILayout.HelpBox(
                    "This camera capture has no proxy layer. Add Gaussian Relighting to an aligned proxy child under a GsplatRenderer.",
                    MessageType.Warning);
            }
            else if (overlap != 0)
            {
                EditorGUILayout.HelpBox(
                    "The source camera still renders part of the proxy layer, so the proxy mesh can appear over the splats.",
                    MessageType.Warning);
                if (GUILayout.Button("Exclude Proxy Layers from Source Camera"))
                    GsplatRelightingSetup.ExcludeProxyLayersFromSourceCamera(relighting);
            }

            if (changed)
                relighting.Refresh();
        }
    }

    [CustomEditor(typeof(GsplatProxyRelighting))]
    [CanEditMultipleObjects]
    internal sealed class GsplatProxyRelightingEditor : UnityEditor.Editor
    {
        void OnEnable()
        {
            GsplatProxyRelighting[] bindings = targets
                .OfType<GsplatProxyRelighting>()
                .ToArray();
            EditorApplication.delayCall += () =>
            {
                foreach (GsplatProxyRelighting binding in bindings)
                {
                    if (binding)
                        GsplatRelightingSetup.Configure(binding, false);
                }
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "m_relightingSource");
            bool changed = serializedObject.ApplyModifiedProperties();

            foreach (GsplatProxyRelighting relighting in targets.OfType<GsplatProxyRelighting>())
            {
                if (!relighting.SplatRenderer)
                {
                    EditorGUILayout.HelpBox(
                        "Place this component on the aligned proxy mesh child of a GameObject with GsplatRenderer.",
                        MessageType.Error);
                    continue;
                }

                if (!relighting.SourceCamera)
                    EditorGUILayout.HelpBox(
                        "No active Game camera was found. Assign Source Camera.",
                        MessageType.Warning);
                else if (!relighting.RelightingSource ||
                         relighting.gameObject.layer !=
                         LayerMask.NameToLayer(GsplatRelightingSetup.ProxyLayerName))
                    EditorGUILayout.HelpBox(
                        "Finishing automatic proxy configuration…",
                        MessageType.Info);
            }

            if (!changed)
                return;

            foreach (GsplatProxyRelighting relighting in targets.OfType<GsplatProxyRelighting>())
            {
                if (relighting)
                    GsplatRelightingSetup.Configure(relighting);
            }
        }
    }
}
