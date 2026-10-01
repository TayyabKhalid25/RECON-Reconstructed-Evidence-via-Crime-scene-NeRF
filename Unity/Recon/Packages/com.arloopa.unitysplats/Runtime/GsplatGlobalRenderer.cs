// Copyright (c) 2026 Keir Rice, Yize Wu
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gsplat
{
    public class GsplatGlobalRenderer
    {
        GsplatGlobalMaterial m_globalMaterial;

        int m_kernelPackOrders = -1;
        int m_kernelMergeTwo = -1;
        int m_kernelMergeTwoWithDepth = -1;
        int m_kernelCopyUint4 = -1;
        int m_kernelCopyUint2 = -1;

        // CPU-side per-renderer metadata (rebuilt when renderers change).
        uint[] m_rendererOffsets; // splat start index in global buffers, per active renderer
        IGsplat[] m_builtRenderers; // renderer identity/order captured when global buffers were last built
        uint[] m_builtSplatCounts; // per-renderer SplatCount captured when the global buffers were last built
        uint m_totalSplatCount;
        uint m_totalRemainingCount; // sum of RemainingCounts; valid entries in GlobalOrderBuffer after merge
        byte m_globalSHBands;
        int m_globalLayer;
        bool m_globalBuffersDirty = true;

        // Global packed data buffers (sub-ranges owned by each renderer).
        GraphicsBuffer m_globalPackedBuffer;
        GraphicsBuffer m_globalSH1Buffer;
        GraphicsBuffer m_globalSH2Buffer;
        GraphicsBuffer m_globalSH3Buffer;
        GraphicsBuffer m_globalSH4Buffer;
        GraphicsBuffer m_globalOrderBuffer;
        GraphicsBuffer m_rendererOffsetsBuffer;
        GraphicsBuffer m_rendererTransformsBuffer;
        GraphicsBuffer m_rendererParamsBuffer;
        GsplatRelighting m_relightingSource;

        // Per-renderer visual settings, looked up by renderer_id during the merged draw.
        // Matches the RendererParams struct in GsplatSparkGlobal.hlsl (48-byte stride).
        [StructLayout(LayoutKind.Sequential)]
        struct RendererParams
        {
            public float Brightness;
            public float ScaleFactor;
            public uint GammaToLinear;
            public uint SHDegree;
            public uint Antialiased;
            public uint RelightEnabled;
            public float RelightBlend;
            public float RelightBrightness;
            public float RelightBackground;
            public uint Padding0;
            public uint Padding1;
            public uint Padding2;
        }

        const int k_rendererParamsStride = 48;

        // Scratch buffers for merge passes.
        // m_packScratch*   : immutable, concatenated per-renderer orders and depths.
        // m_mergeScratchA/B: ping-pong destinations for cascaded intermediate merges.
        // Keeping both merge destinations separate from m_packScratch avoids a GPU
        // read/write overlap once four or more renderers are merged.
        GraphicsBuffer m_packScratchOrders;
        GraphicsBuffer m_packScratchDepths;
        GraphicsBuffer m_mergeScratchAOrders;
        GraphicsBuffer m_mergeScratchADepths;
        GraphicsBuffer m_mergeScratchBOrders;
        GraphicsBuffer m_mergeScratchBDepths;
        Matrix4x4[] m_rendererTransformsCache;
        RendererParams[] m_rendererParamsCache;
        MaterialPropertyBlock m_globalPropertyBlock;

        public bool Valid => m_globalMaterial
                             && m_globalMaterial.Valid()
                             && m_kernelPackOrders >= 0
                             && m_kernelMergeTwo >= 0
                             && m_kernelMergeTwoWithDepth >= 0
                             && m_kernelCopyUint4 >= 0
                             && m_kernelCopyUint2 >= 0;

        const string k_mergePassName = "Gsplat.MergeKWay";

        // -----------------------------------------------------------------------
        // Shader property IDs — merge / pack
        // -----------------------------------------------------------------------
        static readonly int k_rawOrders = Shader.PropertyToID("_RawOrders");
        static readonly int k_rawDepths = Shader.PropertyToID("_RawDepths");
        static readonly int k_packedOrders = Shader.PropertyToID("_PackedOrders");
        static readonly int k_packedDepths = Shader.PropertyToID("_PackedDepths");
        static readonly int k_rendererIdx = Shader.PropertyToID("_RendererIdx");
        static readonly int k_srcCount = Shader.PropertyToID("_SrcCount");
        static readonly int k_dstOffset = Shader.PropertyToID("_DstOffset");

        static readonly int k_depthsA = Shader.PropertyToID("_DepthsA");
        static readonly int k_ordersA = Shader.PropertyToID("_OrdersA");
        static readonly int k_depthsB = Shader.PropertyToID("_DepthsB");
        static readonly int k_ordersB = Shader.PropertyToID("_OrdersB");
        static readonly int k_offsetA = Shader.PropertyToID("_OffsetA");
        static readonly int k_offsetB = Shader.PropertyToID("_OffsetB");
        static readonly int k_countA = Shader.PropertyToID("_CountA");
        static readonly int k_countB = Shader.PropertyToID("_CountB");
        static readonly int k_outputOffset = Shader.PropertyToID("_OutputOffset");
        static readonly int k_outputOrders = Shader.PropertyToID("_OutputOrders");
        static readonly int k_outputDepths = Shader.PropertyToID("_OutputDepths");

        // Copy kernel IDs
        static readonly int k_srcUint4 = Shader.PropertyToID("_SrcUint4");
        static readonly int k_dstUint4 = Shader.PropertyToID("_DstUint4");
        static readonly int k_srcUint2 = Shader.PropertyToID("_SrcUint2");
        static readonly int k_dstUint2 = Shader.PropertyToID("_DstUint2");
        static readonly int k_srcElementCount = Shader.PropertyToID("_SrcElementCount");
        static readonly int k_dstElementOffset = Shader.PropertyToID("_DstElementOffset");

        // Global draw material properties
        static readonly int k_globalOrderBuffer = Shader.PropertyToID("_GlobalOrderBuffer");
        static readonly int k_globalPackedBuffer = Shader.PropertyToID("_GlobalPackedBuffer");
        static readonly int k_globalSH1Buffer = Shader.PropertyToID("_GlobalSH1Buffer");
        static readonly int k_globalSH2Buffer = Shader.PropertyToID("_GlobalSH2Buffer");
        static readonly int k_globalSH3Buffer = Shader.PropertyToID("_GlobalSH3Buffer");
        static readonly int k_globalSH4Buffer = Shader.PropertyToID("_GlobalSH4Buffer");
        static readonly int k_rendererOffsetsProp = Shader.PropertyToID("_RendererOffsets");
        static readonly int k_rendererTransformsProp = Shader.PropertyToID("_RendererTransforms");
        static readonly int k_rendererParamsProp = Shader.PropertyToID("_RendererParams");
        static readonly int k_totalSplatCount = Shader.PropertyToID("_TotalSplatCount");
        static readonly int k_splatInstanceSize = Shader.PropertyToID("_SplatInstanceSize");
        static readonly int k_relightMap = Shader.PropertyToID("_GsplatRelightMap");
        static readonly int k_relightScreen = Shader.PropertyToID("_GsplatRelightScreen");

        public void InitGlobal(GsplatGlobalMaterial globalMaterial)
        {
            m_globalMaterial = globalMaterial;
            m_kernelPackOrders = -1;
            m_kernelMergeTwo = -1;
            m_kernelMergeTwoWithDepth = -1;
            m_kernelCopyUint4 = -1;
            m_kernelCopyUint2 = -1;

            if (!SystemInfo.supportsComputeShaders)
            {
                m_globalMaterial = null;
                DisposeGlobalBuffers();
                m_globalBuffersDirty = true;
                return;
            }

            var mergeShader = globalMaterial ? globalMaterial.MergeShader : null;
            var copyShader = globalMaterial ? globalMaterial.CopyBufferShader : null;

            if (mergeShader)
            {
                if (!mergeShader.HasKernel("PackRendererOrders"))
                    Debug.LogError(
                        $"[GsplatSorter] MergeShader '{mergeShader.name}' is missing kernel 'PackRendererOrders' — assign GsplatMergeOrderBuffers.compute.");
                else
                {
                    m_kernelPackOrders = mergeShader.FindKernel("PackRendererOrders");
                    m_kernelMergeTwo = mergeShader.FindKernel("MergeTwo");
                    m_kernelMergeTwoWithDepth = mergeShader.FindKernel("MergeTwoWithDepth");
                }
            }

            if (copyShader)
            {
                if (!copyShader.HasKernel("CopyUint4"))
                    Debug.LogError(
                        $"[GsplatSorter] CopyBufferShader '{copyShader.name}' is missing kernel 'CopyUint4' — assign GsplatCopyBuffer.compute.");
                else
                {
                    m_kernelCopyUint4 = copyShader.FindKernel("CopyUint4");
                    m_kernelCopyUint2 = copyShader.FindKernel("CopyUint2");
                }
            }

            m_globalBuffersDirty = true;
        }

        public void Update(List<IGsplat> activeGsplats)
        {
            m_globalLayer = activeGsplats.Count > 0
                ? (activeGsplats[0] as Component)?.gameObject.layer ?? 0
                : 0;
            EnsureGlobalBuffers(activeGsplats);
            if (m_totalSplatCount == 0) return;

            // The camera's sort pass executes before transparent drawing and fills the new
            // order buffer. Seed the draw count from current renderer state so the first
            // frame after a rebuild is rendered instead of waiting for a previous merge.
            m_totalRemainingCount = 0;
            foreach (var gsplat in activeGsplats)
                m_totalRemainingCount = checked(m_totalRemainingCount + gsplat.RemainingCount);

            UpdateRendererTransforms(activeGsplats);
            UpdateRendererParams(activeGsplats);
            Render();
        }

        public void DispatchMerge(CommandBuffer cmd, List<IGsplat> activeGsplats)
        {
            if (m_totalSplatCount == 0) return;
            cmd.BeginSample(k_mergePassName);
            DispatchMergeInternal(cmd, activeGsplats);
            cmd.EndSample(k_mergePassName);
        }

        /// <summary>
        /// Call when a renderer's asset changes so the global packed buffer is rebuilt next frame.
        /// </summary>
        public void MarkGlobalBuffersDirty() => m_globalBuffersDirty = true;

        // -----------------------------------------------------------------------
        // Global buffer management
        // -----------------------------------------------------------------------
        void EnsureGlobalBuffers(List<IGsplat> activeGsplats)
        {
            // A renderer streaming in its splats via async upload (AsyncUpload +
            // RenderBeforeUploadComplete) grows its SplatCount frame to frame. Rebuild when an
            // active renderer's count no longer matches what we last built with, otherwise the
            // global sub-ranges keep only the splats present at the first build and the rest are
            // never copied in (leaving the merge to read stale/under-sized global buffers).
            if (!m_globalBuffersDirty && ActiveSplatCountsChanged(activeGsplats))
                m_globalBuffersDirty = true;

            if (!m_globalBuffersDirty) return;
            m_globalBuffersDirty = false;

            DisposeGlobalBuffers();

            m_totalSplatCount = 0;
            m_rendererOffsets = new uint[activeGsplats.Count];
            m_builtRenderers = new IGsplat[activeGsplats.Count];
            m_builtSplatCounts = new uint[activeGsplats.Count];
            // Size SH buffers to the highest band count in the set; lower-band renderers
            // occupy the extra slots but never read them, since each renderer's SHDegree is
            // clamped to its own asset bands in UpdateRendererParams.
            m_globalSHBands = 0;

            foreach (var gs in activeGsplats.Where(gs => gs.SHBands > m_globalSHBands))
                m_globalSHBands = gs.SHBands;

            for (int k = 0; k < activeGsplats.Count; k++)
            {
                var count = activeGsplats[k].SplatCount;
                m_rendererOffsets[k] = m_totalSplatCount;
                m_builtRenderers[k] = activeGsplats[k];
                m_builtSplatCounts[k] = count;
                m_totalSplatCount += count;
            }

            if (m_totalSplatCount == 0) return;

            // CanRenderGlobally() in Gather already guarded count<=255 and Spark-only;
            // GlobalRenderEnabled would be false and we wouldn't be here otherwise.

            var st = GraphicsBuffer.Target.Structured;
            m_globalPackedBuffer = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(uint) * 4)
                { name = "Gsplat.GlobalPacked" };
            m_globalOrderBuffer = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(uint))
                { name = "Gsplat.GlobalOrder" };
            m_rendererOffsetsBuffer = new GraphicsBuffer(st, activeGsplats.Count, sizeof(uint))
                { name = "Gsplat.RendererOffsets" };
            m_rendererTransformsBuffer = new GraphicsBuffer(st, activeGsplats.Count, sizeof(float) * 16)
                { name = "Gsplat.RendererTransforms" };
            Debug.Assert(Marshal.SizeOf<RendererParams>() == k_rendererParamsStride,
                $"RendererParams C# stride must match HLSL ({k_rendererParamsStride} bytes).");
            m_rendererParamsBuffer = new GraphicsBuffer(st, activeGsplats.Count, k_rendererParamsStride)
                { name = "Gsplat.RendererParams" };

            if (m_globalSHBands >= 1)
                m_globalSH1Buffer = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(uint) * 2)
                    { name = "Gsplat.GlobalSH1" };
            if (m_globalSHBands >= 2)
                m_globalSH2Buffer = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(uint) * 4)
                    { name = "Gsplat.GlobalSH2" };
            if (m_globalSHBands >= 3)
                m_globalSH3Buffer = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(uint) * 4)
                    { name = "Gsplat.GlobalSH3" };
            if (m_globalSHBands >= 4)
                m_globalSH4Buffer = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(uint) * 4)
                    { name = "Gsplat.GlobalSH4" };

            m_rendererOffsetsBuffer.SetData(m_rendererOffsets);

            // Copy per-renderer packed splat data into global buffer sub-ranges.
            for (int k = 0; k < activeGsplats.Count; k++)
            {
                var gs = activeGsplats[k];
                var count = gs.SplatCount;
                var off = m_rendererOffsets[k];
                if (count == 0) continue;

                var res = (GsplatResourceSpark)gs.GsplatResource;

                CopyUint4(res.PackedSplatsBuffer, m_globalPackedBuffer, count, off);
                if (m_globalSHBands >= 1 && res.PackedSH1Buffer != null)
                    CopyUint2(res.PackedSH1Buffer, m_globalSH1Buffer, count, off);
                if (m_globalSHBands >= 2 && res.PackedSH2Buffer != null)
                    CopyUint4(res.PackedSH2Buffer, m_globalSH2Buffer, count, off);
                if (m_globalSHBands >= 3 && res.PackedSH3Buffer != null)
                    CopyUint4(res.PackedSH3Buffer, m_globalSH3Buffer, count, off);
                if (m_globalSHBands >= 4 && res.PackedSH4Buffer != null)
                    CopyUint4(res.PackedSH4Buffer, m_globalSH4Buffer, count, off);
            }

            // Allocate merge scratch buffers (always fresh after a dirty rebuild).
            m_packScratchOrders = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(uint))
                { name = "Gsplat.PackScratchOrders" };
            m_packScratchDepths = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(float))
                { name = "Gsplat.PackScratchDepths" };
            if (activeGsplats.Count > 2)
            {
                m_mergeScratchAOrders = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(uint))
                    { name = "Gsplat.MergeScratchAOrders" };
                m_mergeScratchADepths = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(float))
                    { name = "Gsplat.MergeScratchADepths" };
            }
            if (activeGsplats.Count > 3)
            {
                m_mergeScratchBOrders = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(uint))
                    { name = "Gsplat.MergeScratchBOrders" };
                m_mergeScratchBDepths = new GraphicsBuffer(st, (int)m_totalSplatCount, sizeof(float))
                    { name = "Gsplat.MergeScratchBDepths" };
            }
        }

        // True when active renderer identity/order or SplatCount no longer matches the global build.
        // Identity matters because a resident renderer can enter or leave the active set without a
        // register/unregister event when its explicit range crosses zero.
        bool ActiveSplatCountsChanged(List<IGsplat> activeGsplats)
        {
            if (m_builtRenderers == null || m_builtSplatCounts == null ||
                m_builtRenderers.Length != activeGsplats.Count || m_builtSplatCounts.Length != activeGsplats.Count)
                return true;
            return activeGsplats.Where((t, k) =>
                !ReferenceEquals(m_builtRenderers[k], t) || m_builtSplatCounts[k] != t.SplatCount).Any();
        }

        void UpdateRendererTransforms(List<IGsplat> activeGsplats)
        {
            if (m_rendererTransformsBuffer == null) return;
            var count = activeGsplats.Count;
            if (m_rendererTransformsCache == null || m_rendererTransformsCache.Length != count)
                m_rendererTransformsCache = new Matrix4x4[count];
            for (int k = 0; k < count; k++)
                m_rendererTransformsCache[k] = activeGsplats[k].transform.localToWorldMatrix;
            m_rendererTransformsBuffer.SetData(m_rendererTransformsCache);
        }

        void UpdateRendererParams(List<IGsplat> activeGsplats)
        {
            if (m_rendererParamsBuffer == null) return;
            var count = activeGsplats.Count;
            if (m_rendererParamsCache == null || m_rendererParamsCache.Length != count)
                m_rendererParamsCache = new RendererParams[count];
            m_relightingSource = null;
            for (int k = 0; k < count; k++)
            {
                var gs = activeGsplats[k] as GsplatRenderer;
                if (gs != null)
                {
                    bool relightingEnabled = gs.TryGetActiveRelighting(
                        out var relighting,
                        out float relightBlend,
                        out float relightBrightness,
                        out float relightBackground);
                    if (relightingEnabled && !m_relightingSource)
                        m_relightingSource = relighting;
                    m_rendererParamsCache[k] = new RendererParams
                    {
                        Brightness = gs.Brightness,
                        ScaleFactor = 1.0f - gs.SplatDownscaleFactor,
                        GammaToLinear = gs.GammaToLinear ? 1u : 0u,
                        // Clamp to the asset's own bands so EvalSH never reads slots that
                        // weren't copied for this renderer (the global buffers are sized to
                        // the max bands across the set).
                        SHDegree = (uint)Mathf.Min(gs.SHDegree, activeGsplats[k].SHBands),
                        Antialiased = gs.GsplatAsset && gs.GsplatAsset.Antialiased ? 1u : 0u,
                        RelightEnabled = relightingEnabled ? 1u : 0u,
                        RelightBlend = relightingEnabled ? relightBlend : 0f,
                        RelightBrightness = relightingEnabled ? relightBrightness : 0f,
                        RelightBackground = relightingEnabled ? relightBackground : 1f,
                    };
                }
                else
                {
                    m_rendererParamsCache[k] = new RendererParams
                    {
                        Brightness = 1.0f,
                        ScaleFactor = 1.0f,
                        GammaToLinear = 0u,
                        SHDegree = activeGsplats[k].SHBands,
                        Antialiased = 0u,
                        RelightBackground = 1f,
                    };
                }
            }

            m_rendererParamsBuffer.SetData(m_rendererParamsCache);
        }

        // -----------------------------------------------------------------------
        // Copy helpers
        // -----------------------------------------------------------------------
        void CopyUint4(GraphicsBuffer src, GraphicsBuffer dst, uint count, uint dstOff)
        {
            var cs = m_globalMaterial.CopyBufferShader;
            cs.SetInt(k_srcElementCount, (int)count);
            cs.SetInt(k_dstElementOffset, (int)dstOff);
            cs.SetBuffer(m_kernelCopyUint4, k_srcUint4, src);
            cs.SetBuffer(m_kernelCopyUint4, k_dstUint4, dst);
            cs.Dispatch(m_kernelCopyUint4, (int)GsplatUtils.DivRoundUp(count, 256), 1, 1);
        }

        void CopyUint2(GraphicsBuffer src, GraphicsBuffer dst, uint count, uint dstOff)
        {
            var cs = m_globalMaterial.CopyBufferShader;
            cs.SetInt(k_srcElementCount, (int)count);
            cs.SetInt(k_dstElementOffset, (int)dstOff);
            cs.SetBuffer(m_kernelCopyUint2, k_srcUint2, src);
            cs.SetBuffer(m_kernelCopyUint2, k_dstUint2, dst);
            cs.Dispatch(m_kernelCopyUint2, (int)GsplatUtils.DivRoundUp(count, 256), 1, 1);
        }

        // -----------------------------------------------------------------------
        // Merge dispatch
        // -----------------------------------------------------------------------
        void DispatchMergeInternal(CommandBuffer cmd, List<IGsplat> activeGsplats)
        {
            int K = activeGsplats.Count;
            if (K < 2 || m_globalOrderBuffer == null) return;

            // Step 1: pack each renderer's sorted orders + depths into m_packScratch.
            for (int k = 0; k < K; k++)
            {
                var gs = activeGsplats[k];
                if (gs.SorterResource is not { } res) continue;
                uint cnt = gs.RemainingCount;
                uint off = m_rendererOffsets[k];
                if (cnt == 0) continue;

                cmd.SetComputeIntParam(m_globalMaterial.MergeShader, k_rendererIdx, k);
                cmd.SetComputeIntParam(m_globalMaterial.MergeShader, k_srcCount, (int)cnt);
                cmd.SetComputeIntParam(m_globalMaterial.MergeShader, k_dstOffset, (int)off);
                cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, m_kernelPackOrders, k_rawOrders,
                    res.OrderBuffer);
                cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, m_kernelPackOrders, k_rawDepths, res.InputKeys);
                cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, m_kernelPackOrders, k_packedOrders,
                    m_packScratchOrders);
                cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, m_kernelPackOrders, k_packedDepths,
                    m_packScratchDepths);
                cmd.DispatchCompute(m_globalMaterial.MergeShader, m_kernelPackOrders,
                    (int)GsplatUtils.DivRoundUp(cnt, 256), 1, 1);
            }

            // Step 2: cascaded 2-way merges. The packed buffer remains immutable: each
            // renderer's source range always comes from it, while intermediate merged
            // results ping-pong between two distinct scratch pairs.

            uint mergedCount = activeGsplats[0].RemainingCount; // size of merged result so far
            uint mergedOffset = 0; // always at start of its scratch buffer

            GraphicsBuffer currentOrders = m_packScratchOrders;
            GraphicsBuffer currentDepths = m_packScratchDepths;
            bool writeScratchA = true;

            for (int k = 1; k < K; k++)
            {
                var gs = activeGsplats[k];
                uint cntK = gs.RemainingCount;
                uint offK = m_rendererOffsets[k];
                bool isFinal = (k == K - 1);
                uint total = mergedCount + cntK;
                if (total == 0)
                    continue;

                // Source B: renderer k's range in packScratch.
                var srcBOrders = m_packScratchOrders;
                var srcBDepths = m_packScratchDepths;

                // Destination: final pass → globalOrderBuffer; otherwise the other scratch.
                GraphicsBuffer dstOrders, dstDepths;
                if (isFinal)
                {
                    dstOrders = m_globalOrderBuffer;
                    dstDepths = null;
                }
                else
                {
                    dstOrders = writeScratchA ? m_mergeScratchAOrders : m_mergeScratchBOrders;
                    dstDepths = writeScratchA ? m_mergeScratchADepths : m_mergeScratchBDepths;
                }

                int kernel = isFinal ? m_kernelMergeTwo : m_kernelMergeTwoWithDepth;

                cmd.SetComputeIntParam(m_globalMaterial.MergeShader, k_offsetA, (int)mergedOffset);
                cmd.SetComputeIntParam(m_globalMaterial.MergeShader, k_offsetB, (int)offK);
                cmd.SetComputeIntParam(m_globalMaterial.MergeShader, k_countA, (int)mergedCount);
                cmd.SetComputeIntParam(m_globalMaterial.MergeShader, k_countB, (int)cntK);
                cmd.SetComputeIntParam(m_globalMaterial.MergeShader, k_outputOffset, 0);
                cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, kernel, k_depthsA, currentDepths);
                cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, kernel, k_ordersA, currentOrders);
                cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, kernel, k_depthsB, srcBDepths);
                cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, kernel, k_ordersB, srcBOrders);
                cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, kernel, k_outputOrders, dstOrders);
                if (!isFinal)
                    cmd.SetComputeBufferParam(m_globalMaterial.MergeShader, kernel, k_outputDepths, dstDepths);

                cmd.DispatchCompute(m_globalMaterial.MergeShader, kernel, (int)GsplatUtils.DivRoundUp(total, 256), 1,
                    1);

                mergedCount = total;
                mergedOffset = 0; // output always starts at 0 in the destination buffer
                if (!isFinal)
                {
                    currentOrders = dstOrders;
                    currentDepths = dstDepths;
                    writeScratchA = !writeScratchA;
                }
            }

            m_totalRemainingCount = mergedCount;
        }

        // -----------------------------------------------------------------------
        // Global draw call
        // -----------------------------------------------------------------------
        void Render()
        {
            // m_globalBuffersDirty: the active renderer set changed and the next DispatchSort
            // hasn't validated/rebuilt the buffers yet. Drawing now would bind stale buffers
            // (under URP, DrawAllIfEnabled fires before DispatchSort each frame).
            if (m_globalBuffersDirty || m_globalOrderBuffer == null || m_totalRemainingCount == 0) return;

            // Bind buffers via a MaterialPropertyBlock rather than on the material itself, so
            // each queued draw captures its own bindings and multiple cameras (Game + SceneView,
            // or any multi-camera setup) don't overwrite one another's state before the GPU
            // executes them.
            m_globalPropertyBlock ??= new MaterialPropertyBlock();
            m_globalPropertyBlock.Clear();
            m_globalPropertyBlock.SetBuffer(k_globalOrderBuffer, m_globalOrderBuffer);
            m_globalPropertyBlock.SetBuffer(k_globalPackedBuffer, m_globalPackedBuffer);
            m_globalPropertyBlock.SetBuffer(k_rendererOffsetsProp, m_rendererOffsetsBuffer);
            m_globalPropertyBlock.SetBuffer(k_rendererTransformsProp, m_rendererTransformsBuffer);
            m_globalPropertyBlock.SetBuffer(k_rendererParamsProp, m_rendererParamsBuffer);
            m_globalPropertyBlock.SetInteger(k_totalSplatCount, (int)m_totalRemainingCount);
            m_globalPropertyBlock.SetInteger(k_splatInstanceSize, (int)GsplatSettings.Instance.SplatInstanceSize);
            m_globalPropertyBlock.SetTexture(k_relightMap,
                m_relightingSource && m_relightingSource.IsReady
                    ? m_relightingSource.RelightingTexture
                    : Texture2D.blackTexture);
            m_globalPropertyBlock.SetVector(k_relightScreen,
                m_relightingSource && m_relightingSource.IsReady
                    ? m_relightingSource.SourceScreen
                    : Vector4.zero);

            if (m_globalSHBands >= 1)
                m_globalPropertyBlock.SetBuffer(k_globalSH1Buffer, m_globalSH1Buffer);
            if (m_globalSHBands >= 2)
                m_globalPropertyBlock.SetBuffer(k_globalSH2Buffer, m_globalSH2Buffer);
            if (m_globalSHBands >= 3)
                m_globalPropertyBlock.SetBuffer(k_globalSH3Buffer, m_globalSH3Buffer);
            if (m_globalSHBands >= 4)
                m_globalPropertyBlock.SetBuffer(k_globalSH4Buffer, m_globalSH4Buffer);

            var rp = new RenderParams(m_globalMaterial.Materials[m_globalSHBands])
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 1e6f),
                matProps = m_globalPropertyBlock,
                layer = m_globalLayer
            };

            int instances = Mathf.CeilToInt(m_totalRemainingCount / (float)GsplatSettings.Instance.SplatInstanceSize);
            Graphics.RenderMeshPrimitives(rp, GsplatSettings.Instance.Mesh, 0, instances);
        }


        // -----------------------------------------------------------------------
        // Cleanup
        // -----------------------------------------------------------------------
        public void DisposeGlobalBuffers()
        {
            // A rebuilt order buffer has no valid merged entries until DispatchMerge writes it.
            // Clear both counts before disposing so Update cannot draw a fresh/uninitialized
            // buffer using the previous build's m_totalRemainingCount.
            m_totalSplatCount = 0;
            m_totalRemainingCount = 0;

            m_globalPackedBuffer?.Dispose();
            m_globalPackedBuffer = null;
            m_globalSH1Buffer?.Dispose();
            m_globalSH1Buffer = null;
            m_globalSH2Buffer?.Dispose();
            m_globalSH2Buffer = null;
            m_globalSH3Buffer?.Dispose();
            m_globalSH3Buffer = null;
            m_globalSH4Buffer?.Dispose();
            m_globalSH4Buffer = null;
            m_globalOrderBuffer?.Dispose();
            m_globalOrderBuffer = null;
            m_rendererOffsetsBuffer?.Dispose();
            m_rendererOffsetsBuffer = null;
            m_rendererTransformsBuffer?.Dispose();
            m_rendererTransformsBuffer = null;
            m_rendererParamsBuffer?.Dispose();
            m_rendererParamsBuffer = null;
            m_relightingSource = null;
            // Scratch buffers are rebuilt every time global buffers are rebuilt,
            // so dispose them here too to avoid leaking when renderer count shrinks.
            m_packScratchOrders?.Dispose();
            m_packScratchOrders = null;
            m_packScratchDepths?.Dispose();
            m_packScratchDepths = null;
            m_mergeScratchAOrders?.Dispose();
            m_mergeScratchAOrders = null;
            m_mergeScratchADepths?.Dispose();
            m_mergeScratchADepths = null;
            m_mergeScratchBOrders?.Dispose();
            m_mergeScratchBOrders = null;
            m_mergeScratchBDepths?.Dispose();
            m_mergeScratchBDepths = null;
            // Drop the build snapshot so the next EnsureGlobalBuffers re-captures the active set.
            m_builtRenderers = null;
            m_builtSplatCounts = null;
        }
    }
}
