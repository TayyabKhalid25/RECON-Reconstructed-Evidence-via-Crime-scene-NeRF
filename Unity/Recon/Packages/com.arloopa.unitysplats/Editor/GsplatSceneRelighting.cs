// Copyright (c) 2026 ARLOOPA
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat.Editor
{
    /// <summary>
    /// Maintains unsaved relighting captures for Scene-view cameras. Runtime renderers retain
    /// their configured Game-camera source; GsplatRelighting selects this camera-specific map
    /// immediately before each camera renders.
    /// </summary>
    [InitializeOnLoad]
    internal static class GsplatSceneRelighting
    {
        sealed class SceneCameraState
        {
            internal Camera Camera;
            internal GsplatRelighting Relighting;
            internal bool OwnsRelighting;
            internal bool OriginalRenderOnDemand;
            internal int ManagedProxyLayers;
            internal int OriginalProxyLayerBits;
        }

        struct CaptureSettings
        {
            internal int ProxyLayers;
            internal float TextureScale;
            internal float Blend;
            internal float Brightness;
            internal float Background;

            internal bool IsValid => ProxyLayers != 0;
        }

        static readonly Dictionary<Camera, SceneCameraState> s_states = new();
        static CaptureSettings s_cachedSettings;
        static double s_nextUpdate;

        static GsplatSceneRelighting()
        {
            SceneView.beforeSceneGui += BeforeSceneGui;
            EditorApplication.update += Update;
            Camera.onPreCull += BeforeCameraRendering;
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
            AssemblyReloadEvents.beforeAssemblyReload += CleanupAll;
            EditorApplication.quitting += CleanupAll;
        }

        static void BeforeCameraRendering(Camera camera)
        {
            if (camera && s_states.TryGetValue(camera, out SceneCameraState state))
                camera.cullingMask &= ~state.ManagedProxyLayers;
        }

        static void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            BeforeCameraRendering(camera);
        }

        static void BeforeSceneGui(SceneView sceneView)
        {
            if (!sceneView || EditorApplication.isCompiling)
                return;

            RefreshCaptureSettingsIfDue();
            SynchronizeSceneView(sceneView, s_cachedSettings, true);
        }

        static void Update()
        {
            if (EditorApplication.isCompiling ||
                EditorApplication.timeSinceStartup < s_nextUpdate)
                return;

            RefreshCaptureSettings();
            var activeCameras = new HashSet<Camera>();
            foreach (SceneView sceneView in SceneView.sceneViews)
            {
                if (!sceneView || !sceneView.camera)
                    continue;

                activeCameras.Add(sceneView.camera);
                SynchronizeSceneView(sceneView, s_cachedSettings, false);
            }

            foreach (Camera camera in s_states.Keys.ToArray())
            {
                if (!camera || !activeCameras.Contains(camera) || !s_cachedSettings.IsValid)
                    Cleanup(camera);
            }
        }

        static void RefreshCaptureSettingsIfDue()
        {
            if (EditorApplication.timeSinceStartup >= s_nextUpdate)
                RefreshCaptureSettings();
        }

        static void RefreshCaptureSettings()
        {
            s_nextUpdate = EditorApplication.timeSinceStartup + 0.2;
            s_cachedSettings = GetCaptureSettings();
        }

        static CaptureSettings GetCaptureSettings()
        {
            var result = new CaptureSettings
            {
                TextureScale = 0.5f,
                Blend = 1f,
                Brightness = 2f,
                Background = 1f
            };
            bool found = false;

            foreach (GsplatRenderer renderer in Object.FindObjectsByType<GsplatRenderer>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!renderer.isActiveAndEnabled ||
                    !renderer.TryGetActiveRelighting(out GsplatRelighting source) ||
                    !source.isActiveAndEnabled || source.ProxyLayers.value == 0)
                    continue;

                result.ProxyLayers |= source.ProxyLayers.value;
                result.TextureScale = found
                    ? Mathf.Max(result.TextureScale, source.TextureScale)
                    : source.TextureScale;
                if (!found)
                {
                    result.Blend = source.Blend;
                    result.Brightness = source.Brightness;
                    result.Background = source.Background;
                }

                found = true;
            }

            return result;
        }

        static void SynchronizeSceneView(
            SceneView sceneView,
            CaptureSettings settings,
            bool renderNow)
        {
            Camera camera = sceneView.camera;
            if (!camera)
                return;
            if (!settings.IsValid)
            {
                Cleanup(camera);
                return;
            }

            if (!s_states.TryGetValue(camera, out SceneCameraState state) ||
                !state.Relighting)
            {
                if (state != null)
                    Cleanup(camera);

                GsplatRelighting relighting = camera.GetComponent<GsplatRelighting>();
                bool ownsRelighting = !relighting ||
                                      (relighting.hideFlags & HideFlags.DontSave) != 0;
                if (!relighting)
                    relighting = camera.gameObject.AddComponent<GsplatRelighting>();
                if (!relighting)
                    return;

                if (ownsRelighting)
                    relighting.hideFlags = HideFlags.HideAndDontSave;
                state = new SceneCameraState
                {
                    Camera = camera,
                    Relighting = relighting,
                    OwnsRelighting = ownsRelighting,
                    OriginalRenderOnDemand = relighting.RenderOnDemand
                };
                s_states[camera] = state;
            }

            ApplyProxyLayerExclusion(state, settings.ProxyLayers);
            GsplatRelighting sceneRelighting = state.Relighting;
            sceneRelighting.ProxyLayers = settings.ProxyLayers;
            sceneRelighting.TextureScale = settings.TextureScale;
            sceneRelighting.Blend = settings.Blend;
            sceneRelighting.Brightness = settings.Brightness;
            sceneRelighting.Background = settings.Background;
            sceneRelighting.RenderOnDemand = true;
            if (renderNow)
                sceneRelighting.RenderNow();
            else
                sceneRelighting.Refresh();
        }

        static void ApplyProxyLayerExclusion(SceneCameraState state, int proxyLayers)
        {
            Camera camera = state.Camera;
            if (!camera)
                return;

            int addedLayers = proxyLayers & ~state.ManagedProxyLayers;
            state.OriginalProxyLayerBits =
                (state.OriginalProxyLayerBits & ~addedLayers) |
                (camera.cullingMask & addedLayers);

            int removedLayers = state.ManagedProxyLayers & ~proxyLayers;
            camera.cullingMask =
                (camera.cullingMask & ~removedLayers) |
                (state.OriginalProxyLayerBits & removedLayers);
            state.OriginalProxyLayerBits &= ~removedLayers;
            state.ManagedProxyLayers = proxyLayers;
            camera.cullingMask &= ~proxyLayers;
        }

        static void Cleanup(Camera camera)
        {
            if (!s_states.TryGetValue(camera, out SceneCameraState state))
                return;

            if (state.Camera)
            {
                state.Camera.cullingMask =
                    (state.Camera.cullingMask & ~state.ManagedProxyLayers) |
                    (state.OriginalProxyLayerBits & state.ManagedProxyLayers);
            }

            if (state.OwnsRelighting && state.Relighting)
                Object.DestroyImmediate(state.Relighting);
            else if (state.Relighting)
            {
                state.Relighting.RenderOnDemand = state.OriginalRenderOnDemand;
                state.Relighting.Refresh();
            }
            s_states.Remove(camera);
        }

        static void CleanupAll()
        {
            foreach (Camera camera in s_states.Keys.ToArray())
                Cleanup(camera);
            s_states.Clear();
        }
    }
}
