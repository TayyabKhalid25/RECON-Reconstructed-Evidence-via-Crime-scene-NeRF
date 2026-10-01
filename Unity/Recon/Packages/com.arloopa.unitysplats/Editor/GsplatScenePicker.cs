// Copyright (c) 2026 ARLOOPA
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Gsplat.Editor
{
    /// <summary>
    /// Adds bounds-based GsplatRenderer hits to Unity's native Scene-view picker. Unity keeps
    /// ownership of click, cycling, modifier, hierarchy-filter, and ignore-list behavior.
    /// </summary>
    [InitializeOnLoad]
    static class GsplatScenePicker
    {
        const float k_directionEpsilon = 1e-7f;
        const int k_pickerControlHash = 0x4753504C; // 'GSPL'
        const float k_boundsControlDistance = 1f;
        const float k_selectionBoundsPadding = 0.02f;
        const float k_minSelectionBoundsPadding = 1e-4f;
        static readonly int k_selectionId = Shader.PropertyToID("_SelectionId");
        static Mesh s_boundsMesh;
        static Material s_pickingMaterial;

        static GsplatScenePicker()
        {
            // Unity 6 normally resolves Scene clicks through its GPU picking texture. Render an
            // editor-only bounds cube into that texture so a splat behaves like ordinary scene
            // geometry without adding a physics collider to the user's GameObject.
            HandleUtility.UnregisterRenderPickingCallback(RenderPickingBounds);
            HandleUtility.RegisterRenderPickingCallback(RenderPickingBounds);

            // Retain the classic PickGameObject callback for selection APIs and editor paths
            // that do not request a render-picking pass.
            HandleUtility.pickGameObjectCustomPasses -= PickGameObject;
            HandleUtility.pickGameObjectCustomPasses += PickGameObject;

            // Unity 6 has several Scene-view selection paths. Some camera angles can resolve
            // procedural splats through a path that never consumes either custom callback.
            // Keep a direct click fallback so bounds selection remains deterministic.
            SceneView.beforeSceneGui -= OnSceneGUI;
            SceneView.beforeSceneGui += OnSceneGUI;
        }

        static void OnSceneGUI(SceneView sceneView)
        {
            Event current = Event.current;
            if (current == null || current.alt || !sceneView || !sceneView.camera)
                return;

            int controlId = GUIUtility.GetControlID(k_pickerControlHash, FocusType.Passive);
            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            GsplatRenderer renderer = FindClosestBounds(ray, sceneView.camera.cullingMask, null, null);
            if (!renderer)
                return;

            if (current.type == EventType.Layout)
            {
                // A transform handle normally reports distance 0 and therefore wins. The bounds
                // control wins over empty-scene picking while leaving active editor tools usable.
                HandleUtility.AddControl(controlId, k_boundsControlDistance);
                return;
            }

            if (current.type != EventType.MouseDown || current.button != 0 ||
                GUIUtility.hotControl != 0 || HandleUtility.nearestControl != controlId)
                return;

            ApplySelection(renderer.gameObject, current.shift, current.control || current.command);
            current.Use();
            sceneView.Repaint();
        }

        static void ApplySelection(GameObject gameObject, bool additive, bool toggle)
        {
            if (!additive && !toggle)
            {
                Selection.activeGameObject = gameObject;
                return;
            }

            var selected = Selection.objects.ToList();
            bool contains = selected.Contains(gameObject);
            if (toggle && contains)
            {
                selected.Remove(gameObject);
                Selection.objects = selected.ToArray();
                return;
            }

            if (!contains)
                selected.Add(gameObject);
            Selection.objects = selected.ToArray();
            Selection.activeGameObject = gameObject;
        }

        static RenderPickingResult RenderPickingBounds(in RenderPickingArgs args)
        {
            EnsurePickingResources();
            if (!s_boundsMesh || !s_pickingMaterial)
                return RenderPickingResult.NoOperation;

            var pickedObjects = new List<GameObject>();
            var renderers = UnityEngine.Object.FindObjectsByType<GsplatRenderer>(FindObjectsSortMode.None);
            foreach (var renderer in renderers)
            {
                if (!renderer || !renderer.isActiveAndEnabled || !renderer.GsplatAsset ||
                    !args.NeedToRenderForPicking(renderer.gameObject))
                    continue;

                Bounds bounds = GetPickingBounds(renderer.GsplatAsset);
                if (!IsFinite(bounds.center) || !IsFinite(bounds.size))
                    continue;

                int pickingIndex = args.pickingIndex + pickedObjects.Count;
                s_pickingMaterial.SetVector(k_selectionId, HandleUtility.EncodeSelectionId(pickingIndex));
                if (!s_pickingMaterial.SetPass(0))
                    continue;

                Matrix4x4 boundsTransform = renderer.transform.localToWorldMatrix *
                                            Matrix4x4.TRS(bounds.center, Quaternion.identity, bounds.size);
                Graphics.DrawMeshNow(s_boundsMesh, boundsTransform);
                pickedObjects.Add(renderer.gameObject);
            }

            if (pickedObjects.Count == 0)
                return RenderPickingResult.NoOperation;

            return new RenderPickingResult(pickedObjects.Count, localPickingIndex =>
                localPickingIndex >= 0 && localPickingIndex < pickedObjects.Count
                    ? pickedObjects[localPickingIndex]
                    : null);
        }

        static void EnsurePickingResources()
        {
            if (!s_pickingMaterial)
            {
                Shader shader = Shader.Find("Hidden/Gsplat/EditorBoundsPicking");
                if (shader)
                {
                    s_pickingMaterial = new Material(shader)
                    {
                        name = "Gsplat Editor Bounds Picking",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }
            }

            if (s_boundsMesh)
                return;

            s_boundsMesh = new Mesh
            {
                name = "Gsplat Editor Bounds Picking Cube",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, -0.5f),
                    new Vector3( 0.5f, -0.5f, -0.5f),
                    new Vector3( 0.5f,  0.5f, -0.5f),
                    new Vector3(-0.5f,  0.5f, -0.5f),
                    new Vector3(-0.5f, -0.5f,  0.5f),
                    new Vector3( 0.5f, -0.5f,  0.5f),
                    new Vector3( 0.5f,  0.5f,  0.5f),
                    new Vector3(-0.5f,  0.5f,  0.5f)
                },
                triangles = new[]
                {
                    0, 2, 1, 0, 3, 2,
                    1, 2, 6, 1, 6, 5,
                    5, 6, 7, 5, 7, 4,
                    4, 7, 3, 4, 3, 0,
                    3, 7, 6, 3, 6, 2,
                    4, 0, 1, 4, 1, 5
                }
            };
            s_boundsMesh.RecalculateBounds();
            s_boundsMesh.UploadMeshData(true);
        }

        static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        static GameObject PickGameObject(Camera camera, int layers, Vector2 position,
            GameObject[] ignore, GameObject[] filter, out int materialIndex)
        {
            materialIndex = -1;
            if (!camera)
                return null;

            // The callback position is in top-left-origin GUI coordinates. Let Unity apply
            // Scene-view toolbar, viewport, and HiDPI offsets exactly as its built-in picker does.
            Ray ray = HandleUtility.GUIPointToWorldRay(position);

            GsplatRenderer closest = FindClosestBounds(ray, layers, ignore, filter);

            return closest ? closest.gameObject : null;
        }

        static GsplatRenderer FindClosestBounds(Ray ray, int layers, GameObject[] ignore, GameObject[] filter)
        {
            GsplatRenderer closest = null;
            float closestDistance = float.PositiveInfinity;
            var renderers = UnityEngine.Object.FindObjectsByType<GsplatRenderer>(FindObjectsSortMode.None);
            foreach (var renderer in renderers)
            {
                if (!renderer || !renderer.isActiveAndEnabled || !renderer.GsplatAsset)
                    continue;

                GameObject gameObject = renderer.gameObject;
                if ((layers & (1 << gameObject.layer)) == 0 || Contains(ignore, gameObject) ||
                    (filter != null && !Contains(filter, gameObject)))
                    continue;

                var visibility = SceneVisibilityManager.instance;
                if (visibility != null &&
                    (visibility.IsHidden(gameObject, false) || visibility.IsPickingDisabled(gameObject, false)))
                    continue;

                if (!IntersectLocalBounds(ray, renderer.transform.worldToLocalMatrix,
                        GetPickingBounds(renderer.GsplatAsset), out float distance) || distance >= closestDistance)
                    continue;

                closest = renderer;
                closestDistance = distance;
            }

            return closest;
        }

        static Bounds GetPickingBounds(GsplatAsset asset)
        {
            Bounds bounds = asset.Bounds;
            float largestDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            // Asset bounds primarily describe splat centers. Add a small selection-only shell
            // so flat captures and edge Gaussian footprints remain clickable at oblique angles.
            float expansion = Mathf.Max(largestDimension * k_selectionBoundsPadding,
                k_minSelectionBoundsPadding);
            bounds.Expand(expansion);
            return bounds;
        }

        static bool Contains(GameObject[] objects, GameObject target)
        {
            if (objects == null)
                return false;
            return Array.IndexOf(objects, target) >= 0;
        }

        internal static bool IntersectLocalBounds(Ray worldRay, Matrix4x4 worldToLocal,
            Bounds localBounds, out float distance)
        {
            Vector3 origin = worldToLocal.MultiplyPoint3x4(worldRay.origin);
            Vector3 direction = worldToLocal.MultiplyVector(worldRay.direction);
            Vector3 min = localBounds.min;
            Vector3 max = localBounds.max;
            float near = 0f;
            float far = float.PositiveInfinity;

            if (!IntersectAxis(origin.x, direction.x, min.x, max.x, ref near, ref far) ||
                !IntersectAxis(origin.y, direction.y, min.y, max.y, ref near, ref far) ||
                !IntersectAxis(origin.z, direction.z, min.z, max.z, ref near, ref far))
            {
                distance = 0f;
                return false;
            }

            distance = near;
            return far >= 0f;
        }

        static bool IntersectAxis(float origin, float direction, float min, float max,
            ref float near, ref float far)
        {
            if (Mathf.Abs(direction) < k_directionEpsilon)
                return origin >= min && origin <= max;

            float inverse = 1f / direction;
            float first = (min - origin) * inverse;
            float second = (max - origin) * inverse;
            if (first > second)
                (first, second) = (second, first);

            near = Mathf.Max(near, first);
            far = Mathf.Min(far, second);
            return near <= far;
        }
    }
}
