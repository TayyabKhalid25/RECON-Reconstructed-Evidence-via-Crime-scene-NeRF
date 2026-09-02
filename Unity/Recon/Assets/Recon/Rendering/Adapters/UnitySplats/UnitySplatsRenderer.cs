using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gsplat;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Recon.Rendering.UnitySplats
{
    /// <summary>
    /// ISplatRenderer over arloopa/UnitySplats (package com.arloopa.unitysplats, assembly Gsplat).
    /// Only this file knows the package exists. Compiled only when the package is present, via
    /// the RECON_UNITYSPLATS version define on this assembly.
    ///
    /// The .ply is loaded with SourceCoordinates.RUF ("Unity, no conversion"): the GPU track has
    /// already converted it (docs/FRAMES.md) and a second flip here would cancel the first.
    /// </summary>
    [AddComponentMenu("Recon/Rendering/UnitySplats Renderer")]
    public sealed class UnitySplatsRenderer : MonoBehaviour, ISplatRenderer
    {
        [Tooltip("Sort every N frames. 1 = every frame. gsplat-unity users report 1-in-60 is invisible on a phone; measure before trusting that.")]
        [SerializeField, Min(1)] uint sortEveryNFrames = 1;

        [Tooltip("UnitySplats default. 3DGS colours are gamma-space and this project renders in Linear.")]
        [SerializeField] bool gammaToLinear = true;

        GameObject m_holder;
        GsplatRenderer m_renderer;
        GsplatAsset m_asset;

        public string RendererName => "arloopa/UnitySplats 1.2.0 @ 6c02581";
        public bool IsLoaded => m_asset != null && m_renderer != null;
        public int SplatCount => m_renderer != null ? (int)m_renderer.SplatCount : 0;
        public Bounds LocalBounds => m_asset != null ? m_asset.Bounds : default;

        public async Task<SplatLoadResult> LoadAsync(string plyPath, Transform parent,
            IProgress<float> progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(plyPath)) throw new SplatLoadException("No .ply path given.");
            if (!File.Exists(plyPath)) throw new SplatLoadException($"No file at {plyPath}");
            if (parent == null) throw new SplatLoadException("LoadAsync needs a parent transform (SceneRoot).");

            Unload();

            var sw = Stopwatch.StartNew();
            long fileBytes;
            byte[] bytes;
            try
            {
                // File IO off the main thread; the asset itself must be created on it.
                await Awaitable.BackgroundThreadAsync();
                bytes = File.ReadAllBytes(plyPath);
                fileBytes = bytes.LongLength;
            }
            finally
            {
                await Awaitable.MainThreadAsync();
            }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(0.2f);

            try
            {
                m_asset = GsplatRuntimeLoader.Load(
                    bytes,
                    GsplatFileFormat.Ply,
                    CompressionMode.Spark,
                    SourceCoordinates.RUF,
                    (info, p) => progress?.Report(0.2f + 0.7f * Mathf.Clamp01(p)));
            }
            catch (Exception e)
            {
                m_asset = null;
                throw new SplatLoadException($"UnitySplats could not parse {Path.GetFileName(plyPath)}: {e.Message}", e);
            }
            if (m_asset == null || m_asset.SplatCount == 0)
            {
                Unload();
                throw new SplatLoadException($"UnitySplats produced an empty asset from {Path.GetFileName(plyPath)}.");
            }

            m_holder = new GameObject("Splat (UnitySplats)");
            m_holder.transform.SetParent(parent, false);
            m_holder.transform.localPosition = Vector3.zero;
            m_holder.transform.localRotation = Quaternion.identity;
            m_holder.transform.localScale = Vector3.one; // SceneRoot owns unitScale, never this object.

            m_renderer = m_holder.AddComponent<GsplatRenderer>();
            m_renderer.GammaToLinear = gammaToLinear;
            if (sortEveryNFrames > 1)
            {
                m_renderer.SortMode = GsplatRenderer.GsplatSortMode.SortEveryNFrames;
                m_renderer.SortRefreshRate = sortEveryNFrames;
            }
            m_renderer.GsplatAsset = m_asset;

            // Give the renderer one frame to upload before reporting a count.
            await Awaitable.NextFrameAsync(cancellationToken);
            progress?.Report(1f);
            sw.Stop();

            var result = new SplatLoadResult((int)m_asset.SplatCount, m_asset.Bounds, sw.Elapsed.TotalSeconds, fileBytes);
            Debug.Log($"[Recon] {RendererName}: {result} from {plyPath}");
            return result;
        }

        public void Unload()
        {
            if (m_renderer != null)
            {
                m_renderer.GsplatAsset = null;
                m_renderer = null;
            }
            if (m_holder != null)
            {
                Destroy(m_holder);
                m_holder = null;
            }
            if (m_asset != null)
            {
                Destroy(m_asset);
                m_asset = null;
            }
        }

        void OnDestroy() => Unload();
    }
}
