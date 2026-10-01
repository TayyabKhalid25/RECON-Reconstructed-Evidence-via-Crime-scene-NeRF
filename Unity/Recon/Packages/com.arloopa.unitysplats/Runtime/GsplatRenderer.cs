// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Gsplat
{
    [ExecuteAlways]
    public class GsplatRenderer : MonoBehaviour, IGsplat
    {
        public enum GsplatSortMode
        {
            Always,
            SortEveryNFrames,
            CutoutsEveryNSorts,
        }

        [Tooltip("The imported or runtime-created Gaussian splat asset rendered by this component.")]
        public GsplatAsset GsplatAsset;

        // Range is enforced by GsplatRendererEditor based on the bound asset's SHBands.
        [Tooltip(
            "Spherical-harmonic degree used for view-dependent color. Higher values preserve more directional color detail but cost more to render. The asset determines the maximum.")]
        public int SHDegree = 3;

        [Tooltip(
            "Transparent render-order offset used when splats are drawn separately. Keep this at 0 when cross-renderer sorting is enabled.")]
        [HideInInspector] public uint RenderOrder = 0;

        [Tooltip("Multiplies the rendered splat color intensity. 1 uses the source brightness.")]
        public float Brightness = 1.0f;

        [Tooltip(
            "Shrinks projected Gaussian splats to improve rendering speed. 0 preserves their original size; higher values are faster but can create gaps or reduce visual quality.")]
        [Range(0, 1)]
        public float SplatDownscaleFactor = 0.0f;

        [Tooltip(
            "Converts source RGB values from gamma to linear space before blending. Leave enabled for most trained 3DGS content; disable when the source colors are already linear.")]
        public bool GammaToLinear = true;
        [SerializeField, HideInInspector] bool m_gammaToLinearDefaultApplied;

        [Tooltip(
            "Uploads splat data over multiple frames instead of blocking until the full asset reaches the GPU. This reduces startup stalls for large runtime assets.")]
        public bool AsyncUpload;

        [Tooltip(
            "Renders the uploaded portion while an asynchronous upload is still running. Disable to keep the renderer hidden until the complete asset is resident.")]
        public bool RenderBeforeUploadComplete = true;

        [Tooltip(
            "Recalculates the renderer bounds after compute cutouts so Unity can cull the surviving splats more tightly. This is costly when cutouts move frequently.")]
        public bool CutoutsUpdateBounds = true;

        // Retained only so scenes authored with the earlier camera-assignment workflow continue
        // to render. New content discovers GsplatProxyRelighting directly on a proxy child.
        [FormerlySerializedAs("RelightingEnabled")]
        [SerializeField, HideInInspector] bool m_legacyRelightingEnabled;
        [FormerlySerializedAs("RelightingProxy")]
        [SerializeField, HideInInspector] Transform m_legacyRelightingProxy;
        [FormerlySerializedAs("RelightingSource")]
        [SerializeField, HideInInspector] GsplatRelighting m_legacyRelightingSource;

        GsplatAsset m_prevAsset;
        GsplatRendererImpl m_renderer;
        GsplatActiveRange[] m_activeRanges;
        IReadOnlyList<GsplatActiveRange> m_activeRangesView;
        uint[] m_activeSourceIds;
        GsplatAsset m_activeRangesAsset;
        GsplatProxyRelighting m_proxyRelighting;

        sealed class ActiveRangeOffsetComparer : IComparer<GsplatActiveRange>
        {
            public static readonly ActiveRangeOffsetComparer Instance = new();
            public int Compare(GsplatActiveRange x, GsplatActiveRange y) => x.Offset.CompareTo(y.Offset);
        }

        public bool Valid => GsplatAsset &&
                             (RenderBeforeUploadComplete ? SplatCount > 0 : SplatCount == GsplatAsset.SplatCount);

        public uint SplatCount => m_renderer != null ? m_renderer.GsplatResource?.UploadedCount ?? 0 : 0;

        /// <summary>True after SetActiveRanges, including when the configured selection is empty.</summary>
        public bool HasActiveRanges => m_activeSourceIds != null;

        /// <summary>The normalized, sorted, non-overlapping half-open source ranges.</summary>
        public IReadOnlyList<GsplatActiveRange> ActiveRanges =>
            m_activeRangesView ?? Array.Empty<GsplatActiveRange>();

        /// <summary>
        /// Configured source-splat count. When ranges are cleared this is the bound asset's full count.
        /// Use ResidentActiveSplatCount for the currently uploaded/drawable intersection.
        /// </summary>
        public uint ActiveSplatCount => HasActiveRanges
            ? (uint)m_activeSourceIds.Length
            : GsplatAsset ? GsplatAsset.SplatCount : 0;

        /// <summary>Number of selected splats currently resident and eligible for drawing.</summary>
        public uint ResidentActiveSplatCount => m_renderer?.m_remainingCount ?? 0;

        public ISorterResource SorterResource => m_renderer.SorterResource;

        // IGsplat global-merge members: expose per-renderer GPU buffers for the global sorter.
        public GsplatResource GsplatResource => m_renderer?.GsplatResource;
        public byte SHBands => GsplatAsset?.SHBands ?? 0;

        /// <summary>
        /// Gets the active camera-level relighting source for this renderer.
        /// </summary>
        public bool TryGetActiveRelighting(out GsplatRelighting relighting)
        {
            return TryGetActiveRelighting(out relighting, out _, out _, out _);
        }

        internal bool TryGetActiveRelighting(
            out GsplatRelighting relighting,
            out float blend,
            out float brightness,
            out float background)
        {
            GsplatProxyRelighting proxy = ResolveProxyRelighting();
            if (proxy && proxy.TryGetRelightingSource(out relighting))
            {
                blend = proxy.Blend;
                brightness = proxy.Brightness;
                background = proxy.Background;
                return true;
            }

            // Compatibility fallback for scenes configured before proxy-child relighting.
            relighting = m_legacyRelightingSource;
            blend = relighting ? relighting.Blend : 0f;
            brightness = relighting ? relighting.Brightness : 0f;
            background = relighting ? relighting.Background : 1f;
            return m_legacyRelightingEnabled && m_legacyRelightingProxy && relighting &&
                   relighting.IsReady;
        }

        GsplatProxyRelighting ResolveProxyRelighting()
        {
            if (m_proxyRelighting && m_proxyRelighting.transform != transform &&
                m_proxyRelighting.transform.IsChildOf(transform))
                return m_proxyRelighting;

            m_proxyRelighting = GetComponentInChildren<GsplatProxyRelighting>(false);
            if (m_proxyRelighting && m_proxyRelighting.transform == transform)
                m_proxyRelighting = null;
            return m_proxyRelighting;
        }

        public uint RemainingCount
        {
            get => m_renderer.m_remainingCount;
            set => m_renderer.m_remainingCount = value;
        }

        public Bounds Bounds
        {
            get => m_renderer.m_bounds;
            set => m_renderer.m_bounds = value;
        }

        /// <summary>
        /// Fits a BoxCollider on this GameObject to the local bounds stored by the splat asset.
        /// Returns true when the collider bounds were changed.
        /// </summary>
        public bool FitBoxColliderToAssetBounds()
        {
            if (!GsplatAsset || !TryGetComponent(out BoxCollider boxCollider))
                return false;

            Bounds assetBounds = GsplatAsset.Bounds;
            if (boxCollider.center == assetBounds.center && boxCollider.size == assetBounds.size)
                return false;

            boxCollider.center = assetBounds.center;
            boxCollider.size = assetBounds.size;
            return true;
        }

        public GsplatCutout[] Cutouts
        {
            get
            {
                var cutouts = GsplatCutout.m_RegisteredCutouts
                    .Where(component => component.enabled)
                    .Where(component =>
                        component.m_Target == GsplatCutout.Target.All ||
                        (component.m_Target == GsplatCutout.Target.Parent && component.transform.parent == transform) ||
                        (component.m_Target == GsplatCutout.Target.Specific && component.m_SpecifcRenderer == this)
                    );
                return cutouts.ToArray();
            }
        }

        public bool ComputeSortRequired => m_renderer.ComputeSortRequired;
        public bool ComputeCutoutsRequired => m_renderer.ComputeCutoutsRequired;
        [Tooltip(
            "Controls refresh frequency. Always sorts every frame; Sort Every N Frames reduces sort frequency; Cutouts Every N Sorts also reduces cutout recomputation frequency.")]
        public GsplatSortMode SortMode = GsplatSortMode.Always;

        [Tooltip("Number of frames between depth sorts when the selected Sort Mode uses a refresh interval.")]
        [HideInInspector] public uint SortRefreshRate = 1;

        [Tooltip("Number of completed depth sorts between compute-cutout refreshes.")]
        [HideInInspector] public uint CutoutsRefreshRate = 1;

        public void ComputeDepth(CommandBuffer cmd, Matrix4x4 matrixMv) => m_renderer.ComputeDepth(cmd, matrixMv);

        /// <summary>Refreshes async-upload/range intersection immediately before centralized drawing.</summary>
        public void PrepareForSortAndDraw()
        {
            if (!Valid)
                return;
            if (HasActiveRanges)
                m_renderer.PrepareActiveRanges();
            else if (GsplatSorter.Instance.CpuFallbackEnabled)
                m_renderer.PrepareCpuFallback();
        }

        public void Draw()
        {
            if (!Valid)
                return;
            if (!TryGetActiveRelighting(out var relighting, out float relightBlend,
                    out float relightBrightness, out float relightBackground))
                relighting = null;
            m_renderer.Render(transform, gameObject.layer, GammaToLinear, SHDegree, Brightness,
                1.0f - SplatDownscaleFactor, RenderOrder, relighting,
                relightBlend, relightBrightness, relightBackground);
        }

        void OnEnable()
        {
            m_proxyRelighting = null;
            GsplatSorter.Instance.RegisterGsplat(this);
            m_prevAsset = null;
        }

        void OnTransformChildrenChanged()
        {
            m_proxyRelighting = null;
            ForceRefresh();
        }

        void OnDisable()
        {
            GsplatSorter.Instance.UnregisterGsplat(this);
            m_renderer?.Dispose();
            m_renderer = null;
        }

        public void ForceRefresh()
        {
            m_renderer?.ForceRefresh();
        }

        /// <summary>
        /// Activates the exact union of the supplied half-open source ranges. Empty input means
        /// draw none. Calls update Unity GraphicsBuffers and therefore must run on the main thread.
        /// </summary>
        public void SetActiveRanges(IReadOnlyList<GsplatActiveRange> ranges)
        {
            if (ranges == null)
                throw new ArgumentNullException(nameof(ranges));
            if (!GsplatAsset)
                throw new InvalidOperationException("Assign a GsplatAsset before setting active ranges.");

            NormalizeActiveRanges(ranges, GsplatAsset.SplatCount, out var normalized, out var sourceIds);
            m_activeRanges = normalized;
            m_activeRangesView = Array.AsReadOnly(normalized);
            m_activeSourceIds = sourceIds;
            m_activeRangesAsset = GsplatAsset;

            if (m_renderer != null && m_prevAsset == GsplatAsset && m_renderer.GsplatResource != null)
                m_renderer.SetActiveSourceIds(sourceIds);
            ForceRefresh();
        }

        /// <summary>Clears interval selection and restores the dense all-resident-splats path.</summary>
        public void ClearActiveRanges()
        {
            ClearActiveRangeState();
            if (m_renderer != null && m_prevAsset == GsplatAsset && m_renderer.GsplatResource != null)
            {
                m_renderer.ClearActiveSourceIds();
                // Restore compute cutouts immediately when ClearActiveRanges is called from
                // LateUpdate, before the centralized PostLateUpdate draw for this same frame.
                if (GsplatSettings.Instance.Valid && GsplatSorter.Instance.GpuSortEnabled)
                    m_renderer.DispatchInitOrder(Cutouts, transform.localToWorldMatrix, CutoutsUpdateBounds);
            }
            ForceRefresh();
        }

        static void NormalizeActiveRanges(IReadOnlyList<GsplatActiveRange> ranges, uint sourceCount,
            out GsplatActiveRange[] normalized, out uint[] sourceIds)
        {
            if (ranges.Count == 0)
            {
                normalized = Array.Empty<GsplatActiveRange>();
                sourceIds = Array.Empty<uint>();
                return;
            }

            var working = new GsplatActiveRange[ranges.Count];
            var workingCount = 0;
            for (var i = 0; i < ranges.Count; ++i)
            {
                var range = ranges[i];
                var end = (ulong)range.Offset + range.Count;
                if (end > uint.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(ranges),
                        $"Range {i} overflows the uint source-ID domain.");
                if (range.Offset > sourceCount || end > sourceCount)
                    throw new ArgumentOutOfRangeException(nameof(ranges),
                        $"Range {i} [{range.Offset}, {end}) exceeds asset count {sourceCount}.");
                if (range.Count != 0)
                    working[workingCount++] = range;
            }

            if (workingCount == 0)
            {
                normalized = Array.Empty<GsplatActiveRange>();
                sourceIds = Array.Empty<uint>();
                return;
            }

            Array.Sort(working, 0, workingCount, ActiveRangeOffsetComparer.Instance);
            var normalizedCount = 0;
            for (var i = 0; i < workingCount; ++i)
            {
                var next = working[i];
                if (normalizedCount == 0)
                {
                    working[normalizedCount++] = next;
                    continue;
                }

                ref var previous = ref working[normalizedCount - 1];
                var previousEnd = (ulong)previous.Offset + previous.Count;
                var nextEnd = (ulong)next.Offset + next.Count;
                if (next.Offset <= previousEnd)
                {
                    var unionEnd = Math.Max(previousEnd, nextEnd);
                    previous.Count = checked((uint)(unionEnd - previous.Offset));
                }
                else
                {
                    working[normalizedCount++] = next;
                }
            }

            ulong selectedCount = 0;
            for (var i = 0; i < normalizedCount; ++i)
                selectedCount += working[i].Count;
            if (selectedCount > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(ranges),
                    "An active selection cannot exceed Int32.MaxValue splats.");

            normalized = new GsplatActiveRange[normalizedCount];
            Array.Copy(working, normalized, normalizedCount);
            sourceIds = new uint[(int)selectedCount];
            var destination = 0;
            for (var i = 0; i < normalized.Length; ++i)
            {
                var range = normalized[i];
                var count = checked((int)range.Count);
                for (var j = 0; j < count; ++j)
                    sourceIds[destination++] = range.Offset + (uint)j;
            }
        }

        void ClearActiveRangeState()
        {
            m_activeRanges = null;
            m_activeRangesView = null;
            m_activeSourceIds = null;
            m_activeRangesAsset = null;
        }

#if UNITY_EDITOR
        public void OnDrawGizmos()
        {
            if (GsplatSettings.Instance.DisplayBoundingBoxes && Valid && isActiveAndEnabled)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(Bounds.center, Bounds.size);
            }
        }

        [SerializeField, HideInInspector] string m_assetGuid;
        public string AssetGuid => m_assetGuid;
#endif // #if UNITY_EDITOR

        void OnValidate()
        {
            // Migrate renderers serialized before GammaToLinear became enabled by default.
            // The marker makes this a one-time upgrade, so users can still disable it later.
            if (!m_gammaToLinearDefaultApplied)
            {
                GammaToLinear = true;
                m_gammaToLinearDefaultApplied = true;
            }

            ForceRefresh();
#if UNITY_EDITOR
            // Keep an attached collider aligned when the selected splat asset changes or is
            // reimported. The custom inspector also handles the moment a BoxCollider is added.
            if (FitBoxColliderToAssetBounds() && TryGetComponent(out BoxCollider boxCollider))
                EditorUtility.SetDirty(boxCollider);
            if (GsplatAsset &&
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(GsplatAsset, out var guid, out long localId))
                m_assetGuid = guid;
#endif // #if UNITY_EDITOR
        }

        public void ReloadAsset()
        {
            m_prevAsset = null;
        }

        public void Update()
        {
            // Unity's destroyed-object equality can make a destroyed assigned asset compare
            // equal to m_prevAsset. Handle null/destroyed assets explicitly and release both
            // the shared asset buffers and this renderer's size-dependent sort buffers.
            if (!GsplatAsset)
            {
                if (m_renderer != null)
                {
                    m_renderer.Dispose();
                    m_renderer = null;
                    GsplatSorter.Instance.MarkGlobalBuffersDirty();
                }
                m_prevAsset = null;
                ClearActiveRangeState();
            }
            else if (m_prevAsset != GsplatAsset)
            {
                if (m_activeSourceIds != null && m_activeRangesAsset != GsplatAsset)
                    ClearActiveRangeState();
                else if (m_activeSourceIds is { Length: > 0 } &&
                         m_activeSourceIds[m_activeSourceIds.Length - 1] >= GsplatAsset.SplatCount)
                {
                    Debug.LogWarning(
                        $"[GsplatRenderer] Active ranges no longer fit reloaded asset '{GsplatAsset.name}' and were cleared.",
                        this);
                    ClearActiveRangeState();
                }
                m_renderer?.ReleaseGsplatAsset();
                m_prevAsset = GsplatAsset;
                if (m_renderer == null)
                    m_renderer = new GsplatRendererImpl(GsplatAsset.SplatCount);
                else
                    m_renderer.RecreateResources(GsplatAsset.SplatCount);
#if UNITY_EDITOR
                var asyncUpload = AsyncUpload && Application.isPlaying;
#else
                var asyncUpload = AsyncUpload;
#endif
                m_renderer.BindGsplatAsset(GsplatAsset, asyncUpload);
                if (m_activeSourceIds != null)
                    m_renderer.SetActiveSourceIds(m_activeSourceIds);
                GsplatSorter.Instance.MarkGlobalBuffersDirty();
            }

            if (Valid && GsplatSettings.Instance.Valid && GsplatSorter.Instance.Valid)
            {
                m_renderer.EvaluateRefreshRequired(SortMode, SortRefreshRate - 1, CutoutsRefreshRate - 1);
                if (HasActiveRanges)
                    m_renderer.PrepareActiveRanges();
                else if (GsplatSorter.Instance.CpuFallbackEnabled)
                    m_renderer.PrepareCpuFallback();
                else
                    m_renderer.DispatchInitOrder(Cutouts, transform.localToWorldMatrix, CutoutsUpdateBounds);
            }
        }
    }
}
