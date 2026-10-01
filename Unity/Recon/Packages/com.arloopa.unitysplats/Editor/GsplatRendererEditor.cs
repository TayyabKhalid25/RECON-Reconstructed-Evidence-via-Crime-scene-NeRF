// Copyright (c) 2025 Yize Wu
// Copyright (c) 2026 ARLOOPA
// SPDX-License-Identifier: MIT

using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Gsplat.Editor
{
    [CustomEditor(typeof(GsplatRenderer))]
    [CanEditMultipleObjects]
    public class GsplatRendererEditor : UnityEditor.Editor
    {
        GsplatRenderer[] RendererTargets => targets.Cast<GsplatRenderer>().ToArray();

        public override void OnInspectorGUI()
        {
            var rendererTargets = RendererTargets;
            FitNewBoxCollidersToAssetBounds(rendererTargets);
            serializedObject.Update();

            DrawPropertiesExcluding(serializedObject, "m_Script",
                nameof(GsplatRenderer.SHDegree),
                nameof(GsplatRenderer.AsyncUpload),
                nameof(GsplatRenderer.RenderBeforeUploadComplete),
                nameof(GsplatRenderer.Brightness),
                nameof(GsplatRenderer.SortMode)
            );

            DrawShDegree(rendererTargets);
            DrawBrightness();
            DrawSortingControls(rendererTargets);
            DrawUploadControls();
            DrawColliderControls(rendererTargets);

            if (serializedObject.ApplyModifiedProperties())
            {
                foreach (var renderer in rendererTargets)
                    renderer.ForceRefresh();
            }
        }

        void DrawShDegree(GsplatRenderer[] renderers)
        {
            // Use the lowest available band count so one multi-edit never assigns an unsupported
            // degree to another selected renderer.
            int maxShBands = renderers
                .Where(renderer => renderer.GsplatAsset)
                .Select(renderer => (int)renderer.GsplatAsset.SHBands)
                .DefaultIfEmpty(3)
                .Min();
            var property = serializedObject.FindProperty(nameof(GsplatRenderer.SHDegree));

            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.IntSlider(
                new GUIContent("SH Degree",
                    "Spherical-harmonic degree used for view-dependent color. Higher values preserve more directional color detail but cost more to render."),
                Mathf.Clamp(property.intValue, 0, maxShBands), 0, maxShBands);
            if (EditorGUI.EndChangeCheck())
                property.intValue = value;
            EditorGUI.showMixedValue = false;
        }

        void DrawBrightness()
        {
            var property = serializedObject.FindProperty(nameof(GsplatRenderer.Brightness));
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            float brightness = EditorGUILayout.Slider(
                new GUIContent("Brightness", "Splat color intensity multiplier."),
                Mathf.Clamp(property.floatValue, 0f, 20f), 0f, 20f);
            if (EditorGUI.EndChangeCheck())
                property.floatValue = brightness;
            EditorGUI.showMixedValue = false;
        }

        void DrawSortingControls(GsplatRenderer[] renderers)
        {
            var sortMode = serializedObject.FindProperty(nameof(GsplatRenderer.SortMode));
            EditorGUILayout.PropertyField(sortMode);

            bool mixedMode = sortMode.hasMultipleDifferentValues;
            var selectedModes = renderers.Select(renderer => renderer.SortMode).ToArray();
            bool drawSortRate = mixedMode || selectedModes.Any(mode =>
                mode is GsplatRenderer.GsplatSortMode.SortEveryNFrames or
                    GsplatRenderer.GsplatSortMode.CutoutsEveryNSorts);
            bool drawCutoutRate = mixedMode || selectedModes.Any(mode =>
                mode == GsplatRenderer.GsplatSortMode.CutoutsEveryNSorts);

            if (drawSortRate)
                DrawUIntSlider(serializedObject.FindProperty(nameof(GsplatRenderer.SortRefreshRate)),
                    "Sort Refresh Rate",
                    "Number of frames between depth sorts. Larger values reduce sorting cost but can make ordering react more slowly to camera movement.",
                    1, 60);
            if (drawCutoutRate)
                DrawUIntSlider(serializedObject.FindProperty(nameof(GsplatRenderer.CutoutsRefreshRate)),
                    "Cutouts Refresh Rate",
                    "Number of completed depth sorts between compute-cutout refreshes. Larger values reduce cutout cost but update moving cutouts less often.",
                    1, 60);

            if (GsplatSettings.Instance.MaxRenderOrder > 1)
                DrawUIntSlider(serializedObject.FindProperty(nameof(GsplatRenderer.RenderOrder)),
                    "Render Order",
                    "Transparent render-order offset for separately drawn splats. Keep this at 0 for cross-renderer sorting.",
                    0, (int)GsplatSettings.Instance.MaxRenderOrder - 1);
        }

        static void DrawUIntSlider(SerializedProperty property, string label, string tooltip, int min, int max)
        {
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.IntSlider(new GUIContent(label, tooltip),
                Mathf.Clamp(property.intValue, min, max), min, max);
            if (EditorGUI.EndChangeCheck())
                property.intValue = value;
            EditorGUI.showMixedValue = false;
        }

        void DrawUploadControls()
        {
            var asyncUpload = serializedObject.FindProperty(nameof(GsplatRenderer.AsyncUpload));
            EditorGUILayout.PropertyField(asyncUpload);
            if (!asyncUpload.hasMultipleDifferentValues && !asyncUpload.boolValue)
                return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(GsplatRenderer.RenderBeforeUploadComplete)));
            EditorGUI.indentLevel--;
        }

        static void DrawColliderControls(GsplatRenderer[] renderers)
        {
            var eligible = renderers
                .Where(renderer => renderer.GsplatAsset && renderer.TryGetComponent<BoxCollider>(out _))
                .ToArray();
            if (eligible.Length == 0)
                return;

            string label = eligible.Length == 1
                ? "Fit Box Collider to Splat Bounds"
                : $"Fit {eligible.Length} Box Colliders to Splat Bounds";
            if (!GUILayout.Button(new GUIContent(label,
                    "Copies each selected splat asset's local bounding box into the BoxCollider on the same GameObject.")))
                return;

            var colliders = eligible.Select(renderer => renderer.GetComponent<BoxCollider>()).ToArray();
            Undo.RecordObjects(colliders, "Fit Box Colliders to Splat Bounds");
            foreach (var renderer in eligible)
            {
                renderer.FitBoxColliderToAssetBounds();
                EditorUtility.SetDirty(renderer.GetComponent<BoxCollider>());
            }
        }

        static void FitNewBoxCollidersToAssetBounds(GsplatRenderer[] renderers)
        {
            foreach (var renderer in renderers)
            {
                if (!renderer.GsplatAsset || !renderer.TryGetComponent(out BoxCollider boxCollider))
                    continue;

                // A newly added BoxCollider has Unity's default local bounds. Fit only that state
                // during inspector redraws so intentional collider edits are not continually lost.
                if (boxCollider.center != Vector3.zero || boxCollider.size != Vector3.one)
                    continue;

                Bounds assetBounds = renderer.GsplatAsset.Bounds;
                if (assetBounds.center == boxCollider.center && assetBounds.size == boxCollider.size)
                    continue;

                Undo.RecordObject(boxCollider, "Fit Box Collider to Splat Bounds");
                renderer.FitBoxColliderToAssetBounds();
                EditorUtility.SetDirty(boxCollider);
            }
        }

    }
}
