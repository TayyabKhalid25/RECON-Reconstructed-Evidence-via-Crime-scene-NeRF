// Copyright (c) 2026 ARLOOPA
// SPDX-License-Identifier: MIT

using System.Linq;
using UnityEngine;

namespace Gsplat
{
    /// <summary>
    /// Marks an aligned child mesh as the lighting proxy for its parent
    /// <see cref="GsplatRenderer"/>. The component automatically shares a camera-level
    /// <see cref="GsplatRelighting"/> capture with other proxy bindings that use the same camera.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Gsplat/Gaussian Relighting")]
    public sealed class GsplatProxyRelighting : MonoBehaviour
    {
        [Tooltip(
            "Game camera whose view is used to capture the proxy lighting. When empty, the Main Camera or first active Game camera is selected automatically.")]
        public Camera SourceCamera;

        [Tooltip(
            "Relighting texture resolution relative to the source camera. Lower values improve performance but soften lighting and shadow detail.")]
        [Range(0.1f, 1f)]
        public float TextureScale = 0.5f;

        [Tooltip("How strongly proxy lighting modulates this Gaussian splat.")]
        [Range(0f, 1f)]
        public float Blend = 1f;

        [Tooltip(
            "Lighting-map multiplier. The default of 2 compensates for the generated proxy material's 0.5 gray base color.")]
        [Range(0f, 5f)]
        public float Brightness = 2f;

        [Tooltip(
            "Multiplier used where this proxy mesh does not cover the screen, such as a splat-based sky or missing proxy geometry.")]
        [Range(0f, 5f)]
        public float Background = 1f;

        [SerializeField, HideInInspector] GsplatRelighting m_relightingSource;

        /// <summary>The parent Gaussian renderer configured by this proxy binding.</summary>
        public GsplatRenderer SplatRenderer
        {
            get
            {
                GsplatRenderer renderer = GetComponentInParent<GsplatRenderer>();
                return renderer && renderer.transform != transform ? renderer : null;
            }
        }

        /// <summary>The shared camera-level capture used by this proxy.</summary>
        public GsplatRelighting RelightingSource => m_relightingSource;

        /// <summary>
        /// Resolves the source camera and its shared relighting capture. Editor configuration also
        /// assigns the dedicated proxy layer and neutral Lit material.
        /// </summary>
        public void Refresh()
        {
            ClampSettings();

            if (!SourceCamera)
                SourceCamera = FindDefaultCamera();
            if (!SourceCamera)
            {
                m_relightingSource = null;
                return;
            }

            if (!m_relightingSource || m_relightingSource.GetComponent<Camera>() != SourceCamera)
                m_relightingSource = SourceCamera.GetComponent<GsplatRelighting>();

            // Runtime-created splats do not pass through the editor setup callback, so create the
            // shared camera capture here while playing. In edit mode the editor utility uses Undo.
            if (!m_relightingSource && Application.isPlaying)
                m_relightingSource = SourceCamera.gameObject.AddComponent<GsplatRelighting>();
            if (!m_relightingSource)
                return;

            int proxyLayer = gameObject.layer;
            if (proxyLayer > 0)
            {
                int proxyMask = 1 << proxyLayer;
                m_relightingSource.ProxyLayers |= proxyMask;
                SourceCamera.cullingMask &= ~proxyMask;
            }

            // One camera capture is shared by all bindings, so use the highest active request.
            // Blend/brightness/background remain local to each Gaussian renderer.
            m_relightingSource.TextureScale = GetSharedTextureScale(SourceCamera);
            m_relightingSource.Refresh();
            SplatRenderer?.ForceRefresh();
        }

        internal bool TryGetRelightingSource(out GsplatRelighting source)
        {
            if (!m_relightingSource || !SourceCamera ||
                m_relightingSource.GetComponent<Camera>() != SourceCamera)
                Refresh();

            source = m_relightingSource;
            return isActiveAndEnabled && SplatRenderer && source && source.IsReady;
        }

        void OnEnable()
        {
            Refresh();
        }

        void OnValidate()
        {
            ClampSettings();
            SplatRenderer?.ForceRefresh();
        }

        void OnDisable()
        {
            SplatRenderer?.ForceRefresh();
        }

        void OnTransformParentChanged()
        {
            SplatRenderer?.ForceRefresh();
        }

        void ClampSettings()
        {
            TextureScale = Mathf.Clamp(TextureScale, 0.1f, 1f);
            Blend = Mathf.Clamp01(Blend);
            Brightness = Mathf.Max(0f, Brightness);
            Background = Mathf.Max(0f, Background);
        }

        static Camera FindDefaultCamera()
        {
            if (Camera.main && Camera.main.cameraType == CameraType.Game)
                return Camera.main;

            return FindObjectsByType<Camera>(FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None)
                .FirstOrDefault(camera => camera.cameraType == CameraType.Game &&
                                          camera.isActiveAndEnabled);
        }

        static float GetSharedTextureScale(Camera sourceCamera)
        {
            float result = 0.1f;
            foreach (GsplatProxyRelighting binding in FindObjectsByType<GsplatProxyRelighting>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (binding.SourceCamera == sourceCamera)
                    result = Mathf.Max(result, binding.TextureScale);
            }
            return result;
        }
    }
}
