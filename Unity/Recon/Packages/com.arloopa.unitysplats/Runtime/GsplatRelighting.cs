// Copyright (c) 2026 PlayCanvas Ltd
// Copyright (c) 2026 ARLOOPA
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if GSPLAT_ENABLE_URP
using UnityEngine.Rendering.Universal;
#endif
#if GSPLAT_ENABLE_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

namespace Gsplat
{
    /// <summary>
    /// Managed camera service that renders lit proxy geometry into the screen-space texture used
    /// by Gaussian renderers. Add <see cref="GsplatProxyRelighting"/> to a proxy child instead of
    /// adding this capture service manually.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("")]
    public sealed class GsplatRelighting : MonoBehaviour
    {
        static readonly HashSet<Camera> s_proxyCameras = new();
        static readonly Dictionary<Camera, GsplatRelighting> s_sourceCameras = new();
        static readonly List<GsplatRelighting> s_gameSynchronizationScratch = new();
        static readonly int s_cameraRelightMap =
            Shader.PropertyToID("_GsplatCameraRelightMap");
        static readonly int s_cameraRelightOverride =
            Shader.PropertyToID("_GsplatCameraRelightOverride");
        static readonly int s_cameraRelightScreen =
            Shader.PropertyToID("_GsplatCameraRelightScreen");
        static readonly int s_cameraRelightView =
            Shader.PropertyToID("_GsplatCameraRelightView");
        static readonly int s_cameraRelightPosition =
            Shader.PropertyToID("_GsplatCameraRelightPosition");
        static readonly int s_cameraRelightProjection =
            Shader.PropertyToID("_GsplatCameraRelightProjection");
        static bool s_cameraCallbacksRegistered;

        [Tooltip(
            "Layers rendered into the relighting texture. Keep these layers excluded from the source camera so the proxy mesh is not directly visible.")]
        public LayerMask ProxyLayers;

        [Tooltip(
            "Relighting texture resolution relative to the source camera. Lower values improve performance but soften lighting and shadow detail.")]
        [Range(0.1f, 1f)]
        public float TextureScale = 0.5f;

        [Tooltip("How strongly proxy lighting modulates the opted-in Gaussian splats.")]
        [Range(0f, 1f)]
        public float Blend = 1f;

        [Tooltip(
            "Lighting-map multiplier. The default of 2 compensates for the generated proxy material's 0.5 gray base color.")]
        [Range(0f, 5f)]
        public float Brightness = 2f;

        [Tooltip(
            "Multiplier used where no proxy mesh covers the screen, such as a splat-based sky or geometry missing from the proxy.")]
        [Range(0f, 5f)]
        public float Background = 1f;

        // Keep these names compatible with the earlier experimental implementation so scenes
        // authored with it can recover their camera-level relighting settings.
        [SerializeField, HideInInspector] Camera m_sourceCamera;
        [SerializeField, HideInInspector] Camera m_proxyCamera;
        [SerializeField, HideInInspector] RenderTexture m_texture;
        [System.NonSerialized] Camera m_registeredSourceCamera;

        public Camera SourceCamera => m_sourceCamera ? m_sourceCamera : GetComponent<Camera>();
        public RenderTexture RelightingTexture => m_texture;
        internal Vector4 SourceScreen => SourceCamera
            ? GetCameraScreen(SourceCamera)
            : Vector4.zero;
        public bool IsReady => isActiveAndEnabled && m_texture && m_proxyCamera &&
                               ProxyLayers.value != 0;

        /// <summary>
        /// Disables automatic rendering by the hidden proxy camera. Call <see cref="RenderNow"/>
        /// when the capture must be synchronized with a camera rendered outside Unity's normal
        /// player camera loop, such as an Editor Scene view.
        /// </summary>
        public bool RenderOnDemand { get; set; }

        internal static bool IsProxyCamera(Camera camera) => camera && s_proxyCameras.Contains(camera);

        /// <summary>
        /// Synchronizes active Game-camera relighting after all behaviour LateUpdate calls and
        /// before Unity begins camera rendering. The proxy camera then renders through Unity's
        /// normal camera loop, matching the path used by a Game view outside Play mode.
        /// </summary>
        internal static void SynchronizeGameCameras()
        {
            if (!Application.isPlaying || s_sourceCameras.Count == 0)
                return;

            s_gameSynchronizationScratch.Clear();
            s_gameSynchronizationScratch.AddRange(s_sourceCameras.Values);
            foreach (GsplatRelighting relighting in s_gameSynchronizationScratch)
            {
                if (!relighting || !relighting.isActiveAndEnabled)
                    continue;

                Camera source = relighting.SourceCamera;
                if (!source || source.cameraType != CameraType.Game ||
                    !source.isActiveAndEnabled)
                    continue;

                relighting.Refresh();
            }
            s_gameSynchronizationScratch.Clear();
        }

        /// <summary>Recreates or resynchronizes the hidden proxy camera and lighting texture.</summary>
        public void Refresh()
        {
            if (!isActiveAndEnabled)
                return;
            m_sourceCamera = GetComponent<Camera>();
            RegisterSourceCamera();
            EnsureProxyCamera();
            Synchronize();
        }

        /// <summary>
        /// Immediately renders the synchronized proxy camera into the relighting texture.
        /// </summary>
        /// <returns>True when the render request was submitted successfully.</returns>
        public bool RenderNow()
        {
            if (!isActiveAndEnabled)
                return false;

            Refresh();
            if (!IsReady)
                return false;

            bool wasEnabled = m_proxyCamera.enabled;
            m_proxyCamera.enabled = false;
            try
            {
                RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline
                    ? GraphicsSettings.currentRenderPipeline
                    : GraphicsSettings.defaultRenderPipeline;
                if (!pipeline)
                {
                    m_proxyCamera.Render();
                    return true;
                }

                var request = new RenderPipeline.StandardRequest
                {
                    destination = m_texture,
                    mipLevel = 0,
                    slice = 0,
                    face = CubemapFace.Unknown
                };
                if (!RenderPipeline.SupportsRenderRequest(m_proxyCamera, request))
                    return false;

                RenderPipeline.SubmitRenderRequest(m_proxyCamera, request);
                return true;
            }
            finally
            {
                m_proxyCamera.enabled = wasEnabled && !RenderOnDemand;
            }
        }

        void OnEnable()
        {
            Refresh();
        }

        void LateUpdate()
        {
            Synchronize();
        }

        void OnValidate()
        {
            TextureScale = Mathf.Clamp(TextureScale, 0.1f, 1f);
            Blend = Mathf.Clamp01(Blend);
            Brightness = Mathf.Max(0f, Brightness);
            Background = Mathf.Max(0f, Background);

            if (!isActiveAndEnabled)
                return;

            m_sourceCamera = GetComponent<Camera>();
            // Unity forbids creating, parenting, or adding components from OnValidate.
            // ExecuteAlways LateUpdate (or an explicit Refresh from the inspector) performs
            // the synchronization immediately afterward.
        }

        void OnDisable()
        {
            UnregisterSourceCamera();
            DestroyResources();
        }

        void OnDestroy()
        {
            UnregisterSourceCamera();
            DestroyResources();
        }

        void RegisterSourceCamera()
        {
            if (!m_sourceCamera)
                return;
            if (m_registeredSourceCamera == m_sourceCamera &&
                s_sourceCameras.TryGetValue(m_sourceCamera, out var registered) &&
                registered == this)
                return;

            UnregisterSourceCamera();
            m_registeredSourceCamera = m_sourceCamera;
            s_sourceCameras[m_sourceCamera] = this;
            RegisterCameraCallbacks();
        }

        void UnregisterSourceCamera()
        {
            if (m_registeredSourceCamera &&
                s_sourceCameras.TryGetValue(m_registeredSourceCamera, out var registered) &&
                registered == this)
                s_sourceCameras.Remove(m_registeredSourceCamera);
            m_registeredSourceCamera = null;

            if (s_sourceCameras.Count != 0 || !s_cameraCallbacksRegistered)
                return;

            Camera.onPreRender -= OnCameraPreRender;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            s_cameraCallbacksRegistered = false;
            Shader.SetGlobalTexture(s_cameraRelightMap, Texture2D.blackTexture);
            Shader.SetGlobalFloat(s_cameraRelightOverride, 0f);
        }

        static void RegisterCameraCallbacks()
        {
            if (s_cameraCallbacksRegistered)
                return;

            Camera.onPreRender += OnCameraPreRender;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            s_cameraCallbacksRegistered = true;
        }

        static void OnCameraPreRender(Camera camera)
        {
            BindForCamera(camera);
        }

        static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            BindForCamera(camera);
        }

        static void BindForCamera(Camera camera)
        {
            if (camera && s_sourceCameras.TryGetValue(camera, out var relighting) &&
                relighting && relighting.IsReady)
            {
                Shader.SetGlobalTexture(s_cameraRelightMap, relighting.m_texture);
                // Normal Game draws already carry their configured source texture in their
                // MaterialPropertyBlock. Keep that binding for Game cameras so a Scene-view
                // global can never replace it. Transient Scene cameras need the override
                // because those draws otherwise carry the configured Game-camera texture.
                Shader.SetGlobalFloat(s_cameraRelightOverride,
                    camera.cameraType == CameraType.SceneView ? 1f : 0f);
                Shader.SetGlobalVector(s_cameraRelightScreen, GetCameraScreen(camera));
                Shader.SetGlobalMatrix(s_cameraRelightView, camera.worldToCameraMatrix);
                Shader.SetGlobalVector(s_cameraRelightPosition, camera.transform.position);
                Shader.SetGlobalVector(s_cameraRelightProjection,
                    GetCameraProjectionSignature(camera));
                return;
            }

            // -1 tells the shader not to reuse a map captured for a different camera.
            // Zero is reserved for the renderer-bound fallback before any camera callback.
            Shader.SetGlobalTexture(s_cameraRelightMap, Texture2D.blackTexture);
            Shader.SetGlobalFloat(s_cameraRelightOverride, -1f);
        }

        /// <summary>
        /// Records the relighting selection into one camera's command stream. Unlike the
        /// immediate shader-global callback, this binding cannot be overwritten while SRP
        /// records another Game, Scene, reflection, or preview camera in the same frame.
        /// </summary>
        internal static void BindForCamera(CommandBuffer commandBuffer, Camera camera)
        {
            if (commandBuffer == null)
                return;

            if (camera && s_sourceCameras.TryGetValue(camera, out var relighting) &&
                relighting && relighting.IsReady)
            {
                commandBuffer.SetGlobalTexture(s_cameraRelightMap, relighting.m_texture);
                commandBuffer.SetGlobalFloat(s_cameraRelightOverride,
                    camera.cameraType == CameraType.SceneView ? 1f : 0f);
                commandBuffer.SetGlobalVector(s_cameraRelightScreen, GetCameraScreen(camera));
                commandBuffer.SetGlobalMatrix(s_cameraRelightView, camera.worldToCameraMatrix);
                commandBuffer.SetGlobalVector(s_cameraRelightPosition, camera.transform.position);
                commandBuffer.SetGlobalVector(s_cameraRelightProjection,
                    GetCameraProjectionSignature(camera));
                return;
            }

            commandBuffer.SetGlobalTexture(s_cameraRelightMap, Texture2D.blackTexture);
            commandBuffer.SetGlobalFloat(s_cameraRelightOverride, -1f);
        }

        static Vector4 GetCameraScreen(Camera camera)
        {
            float pipelineScale = GetPipelineRenderScale(camera);
            // Camera.scaledPixelWidth/Height include Unity's dynamic-resolution scale, but URP's
            // asset Render Scale is applied separately. The splats render into that pre-upscale
            // target, so omitting the URP scale stretches the relighting map by 1 / renderScale.
            float width = Mathf.Max(1, (int)(camera.scaledPixelWidth * pipelineScale));
            float height = Mathf.Max(1, (int)(camera.scaledPixelHeight * pipelineScale));
            return new Vector4(width, height, 1f / width, 1f / height);
        }

        static float GetPipelineRenderScale(Camera camera)
        {
#if GSPLAT_ENABLE_URP
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline
                ? GraphicsSettings.currentRenderPipeline
                : GraphicsSettings.defaultRenderPipeline;
            if (pipeline is UniversalRenderPipelineAsset universal &&
                camera.cameraType != CameraType.SceneView &&
                camera.cameraType != CameraType.Preview &&
                camera.cameraType != CameraType.Reflection)
            {
                // Mirror URP's kRenderScaleThreshold behavior so our screen coordinates use the
                // same dimensions as UniversalCameraData.scaledWidth/scaledHeight.
                float renderScale = universal.renderScale;
                return Mathf.Abs(1f - renderScale) < 0.05f ? 1f : renderScale;
            }
#endif
            return 1f;
        }

        static Vector4 GetCameraProjectionSignature(Camera camera)
        {
            Matrix4x4 projection = camera.projectionMatrix;
            // Absolute diagonal values are stable across render-target Y flips and reversed-Z
            // conversion while still identifying the camera's FOV/orthographic scale and aspect.
            return new Vector4(
                Mathf.Abs(projection.m00),
                Mathf.Abs(projection.m11),
                projection.m02,
                projection.m12);
        }

        void EnsureProxyCamera()
        {
            if (m_proxyCamera)
            {
                // HideAndDontSave objects can survive a managed domain reload while statics do
                // not, so rebuild the exclusion registry whenever the camera is recovered.
                s_proxyCameras.Add(m_proxyCamera);
                return;
            }

            var cameraObject = new GameObject($"{name} Relighting Camera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            cameraObject.transform.SetParent(transform, false);
            m_proxyCamera = cameraObject.AddComponent<Camera>();
            s_proxyCameras.Add(m_proxyCamera);
            m_proxyCamera.enabled = false;
        }

        void Synchronize()
        {
            if (!m_sourceCamera)
                m_sourceCamera = GetComponent<Camera>();
            if (!m_sourceCamera)
                return;

            EnsureProxyCamera();
            EnsureRenderTexture();
            if (!m_proxyCamera || !m_texture)
                return;

            // Copy the complete projection/camera state first, then restrict the proxy camera.
            // Keeping it as a child also makes its world transform follow the source camera
            // between updates.
            m_proxyCamera.CopyFrom(m_sourceCamera);
            m_proxyCamera.transform.SetPositionAndRotation(
                m_sourceCamera.transform.position,
                m_sourceCamera.transform.rotation);
            m_proxyCamera.transform.localScale = Vector3.one;
            m_proxyCamera.cullingMask = ProxyLayers.value;
            m_proxyCamera.clearFlags = CameraClearFlags.SolidColor;
            m_proxyCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            m_proxyCamera.targetTexture = m_texture;
            m_proxyCamera.depth = m_sourceCamera.depth - 1f;
            m_proxyCamera.allowHDR = m_texture.format == RenderTextureFormat.ARGBHalf ||
                                     m_texture.format == RenderTextureFormat.ARGBFloat;
            m_proxyCamera.allowMSAA = false;
            m_proxyCamera.useOcclusionCulling = false;

            ConfigurePipelineCamera();

            // Target-texture assignment and render-pipeline camera setup can recalculate a
            // camera's projection. Reapply the source matrices last so custom projections,
            // Play-mode camera controllers, and physical-camera settings remain pixel aligned.
            m_proxyCamera.worldToCameraMatrix = m_sourceCamera.worldToCameraMatrix;
            m_proxyCamera.projectionMatrix = m_sourceCamera.projectionMatrix;

            bool sourceCameraRenders = m_sourceCamera.isActiveAndEnabled ||
                                       m_sourceCamera.cameraType == CameraType.SceneView;
            m_proxyCamera.enabled = !RenderOnDemand && isActiveAndEnabled &&
                                    sourceCameraRenders &&
                                    ProxyLayers.value != 0;
        }

        void ConfigurePipelineCamera()
        {
            // A two-dimensional lighting map intentionally follows PlayCanvas's screen-space
            // implementation. In XR it is rendered once from the camera's center-eye transform.
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline
                ? GraphicsSettings.currentRenderPipeline
                : GraphicsSettings.defaultRenderPipeline;
            if (!pipeline)
            {
                m_proxyCamera.stereoTargetEye = StereoTargetEyeMask.None;
                return;
            }

#if GSPLAT_ENABLE_URP
            if (pipeline is UniversalRenderPipelineAsset)
            {
                UniversalAdditionalCameraData cameraData =
                    m_proxyCamera.GetUniversalAdditionalCameraData();
                cameraData.allowXRRendering = false;
                cameraData.renderPostProcessing = false;
                cameraData.volumeLayerMask = 0;
                return;
            }
#endif

#if GSPLAT_ENABLE_HDRP
            if (pipeline is HDRenderPipelineAsset)
            {
                HDAdditionalCameraData cameraData =
                    m_proxyCamera.GetComponent<HDAdditionalCameraData>();
                if (!cameraData)
                    cameraData = m_proxyCamera.gameObject.AddComponent<HDAdditionalCameraData>();
                cameraData.xrRendering = false;
                cameraData.volumeLayerMask = 0;
                cameraData.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
                cameraData.backgroundColorHDR = Color.clear;
                cameraData.clearDepth = true;
                cameraData.customRenderingSettings = true;
                cameraData.renderingPathCustomFrameSettings.SetEnabled(
                    FrameSettingsField.Postprocess, false);
                cameraData.renderingPathCustomFrameSettingsOverrideMask.mask[
                    (uint)FrameSettingsField.Postprocess] = true;
                cameraData.renderingPathCustomFrameSettings.SetEnabled(
                    FrameSettingsField.ExposureControl, false);
                cameraData.renderingPathCustomFrameSettingsOverrideMask.mask[
                    (uint)FrameSettingsField.ExposureControl] = true;
            }
#endif
        }

        void EnsureRenderTexture()
        {
            int sourceWidth = Mathf.Max(1, m_sourceCamera.pixelWidth);
            int sourceHeight = Mathf.Max(1, m_sourceCamera.pixelHeight);
            int width = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * TextureScale));
            int height = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * TextureScale));
            var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)
                ? RenderTextureFormat.ARGBHalf
                : RenderTextureFormat.ARGB32;

            if (m_texture && m_texture.width == width && m_texture.height == height &&
                m_texture.format == format)
                return;

            DestroyTexture();
            m_texture = new RenderTexture(width, height, 24, format, RenderTextureReadWrite.Linear)
            {
                name = $"{name} Relighting Texture",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1
            };
            m_texture.Create();
        }

        void DestroyResources()
        {
            if (m_proxyCamera)
            {
                var cameraObject = m_proxyCamera.gameObject;
                s_proxyCameras.Remove(m_proxyCamera);
                m_proxyCamera = null;
                DestroyUnityObject(cameraObject);
            }

            DestroyTexture();
            m_sourceCamera = null;
        }

        void DestroyTexture()
        {
            if (!m_texture)
                return;

            if (m_proxyCamera && m_proxyCamera.targetTexture == m_texture)
                m_proxyCamera.targetTexture = null;
            m_texture.Release();
            DestroyUnityObject(m_texture);
            m_texture = null;
        }

        static void DestroyUnityObject(Object value)
        {
            if (!value)
                return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(value);
                return;
            }
#endif
            Destroy(value);
        }
    }
}
