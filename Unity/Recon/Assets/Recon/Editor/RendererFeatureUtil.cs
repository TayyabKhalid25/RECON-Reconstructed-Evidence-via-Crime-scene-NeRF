using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Recon.Editor
{
    /// <summary>
    /// Adds ScriptableRendererFeatures to the active URP renderer asset from script, so the two
    /// features this project needs are never a click someone forgot:
    ///   - AR Foundation's ARCommandBufferSupportRendererFeature (Tayyab's fix for the ARCore black
    ///     screen on Vulkan, PR #14)
    ///   - the splat package's URP feature (Gsplat.GsplatURPFeature for UnitySplats)
    /// Types are looked up by name so this assembly compiles whether or not the package is installed.
    /// Uses the serialized m_RendererFeatures / m_RendererFeatureMap pair exactly as the URP
    /// inspector does; the map entry is the feature's local file id, which URP uses to validate.
    /// </summary>
    public static class RendererFeatureUtil
    {
        public static ScriptableRendererData ActiveRendererData()
        {
            var urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null)
            {
                Debug.LogError("[Recon] Graphics settings do not point at a URP asset.");
                return null;
            }
            var so = new SerializedObject(urp);
            var list = so.FindProperty("m_RendererDataList");
            var index = so.FindProperty("m_DefaultRendererIndex").intValue;
            if (list == null || list.arraySize == 0) return null;
            index = Mathf.Clamp(index, 0, list.arraySize - 1);
            return list.GetArrayElementAtIndex(index).objectReferenceValue as ScriptableRendererData;
        }

        /// <summary>Returns true if the feature was added, false if it was already present or the type is unavailable.</summary>
        public static bool EnsureFeature(ScriptableRendererData data, string assemblyQualifiedTypeName, bool active = true)
        {
            var type = Type.GetType(assemblyQualifiedTypeName);
            if (type == null)
            {
                Debug.LogWarning($"[Recon] Renderer feature type not available: {assemblyQualifiedTypeName} (package not installed?)");
                return false;
            }
            return EnsureFeature(data, type, active);
        }

        public static bool EnsureFeature(ScriptableRendererData data, Type featureType, bool active = true)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (!typeof(ScriptableRendererFeature).IsAssignableFrom(featureType))
                throw new ArgumentException($"{featureType} is not a ScriptableRendererFeature");

            var so = new SerializedObject(data);
            var list = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");

            for (int i = 0; i < list.arraySize; i++)
            {
                var existing = list.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererFeature;
                if (existing != null && existing.GetType() == featureType)
                {
                    if (existing.isActive != active)
                    {
                        existing.SetActive(active);
                        EditorUtility.SetDirty(existing);
                    }
                    return false;
                }
            }

            var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(featureType);
            feature.name = featureType.Name;
            feature.SetActive(active);
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Recon] Added renderer feature {featureType.Name} to {data.name}");
            return true;
        }
    }
}
