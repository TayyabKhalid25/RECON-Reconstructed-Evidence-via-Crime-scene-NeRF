// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Vector3 = UnityEngine.Vector3;

namespace Gsplat
{
    public class GsplatRendererImpl
    {
        public uint SplatCount { get; private set; }

        MaterialPropertyBlock m_propertyBlock;
        GsplatAsset m_gsplatAsset;
        public uint m_remainingCount = 0;
        public Bounds m_bounds;

        public GsplatResource GsplatResource;
        public GraphicsBuffer OrderBuffer { get; private set; }
        public Texture2D OrderTexture { get; private set; }
        public GraphicsBuffer CutoutsBuffer { get; private set; }
        public GraphicsBuffer OrderSizeBuffer { get; private set; }
        public GraphicsBuffer BoundsBuffer { get; private set; }
        public ISorterResource SorterResource { get; private set; }

        static readonly int k_orderBuffer = Shader.PropertyToID("_OrderBuffer");
        static readonly int k_orderTexture = Shader.PropertyToID("_OrderTexture");
        static readonly int k_gsplatTextureWidth = Shader.PropertyToID("_GsplatTextureWidth");
        static readonly int k_matrixM = Shader.PropertyToID("_MATRIX_M");
        static readonly int k_splatInstanceSize = Shader.PropertyToID("_SplatInstanceSize");
        static readonly int k_splatCount = Shader.PropertyToID("_SplatCount");
        static readonly int k_gammaToLinear = Shader.PropertyToID("_GammaToLinear");
        static readonly int k_shDegree = Shader.PropertyToID("_SHDegree");
        static readonly int k_brightness = Shader.PropertyToID("_Brightness");
        static readonly int k_scaleFactor = Shader.PropertyToID("_ScaleFactor");
        static readonly int k_antialiased = Shader.PropertyToID("_Antialiased");
        static readonly int k_relightMap = Shader.PropertyToID("_GsplatRelightMap");
        static readonly int k_relightScreen = Shader.PropertyToID("_GsplatRelightScreen");
        static readonly int k_relightParams = Shader.PropertyToID("_GsplatRelightParams");

        uint m_framesBeforeRecomputeSort = 0;
        uint m_sortsBeforeRecomputeCutouts = 0;
        public bool ComputeSortRequired = true;
        public bool ComputeCutoutsRequired = true;
        Dictionary<Camera, (Vector3, Vector3)> m_prevCamTransforms;

        GsplatCutout.ShaderData[] m_cutoutsData;
        uint m_prevSplatCount;
        uint[] m_activeSourceIds;

        public GsplatRendererImpl(uint splatCount)
        {
            SplatCount = splatCount;
            m_prevCamTransforms = new Dictionary<Camera, (Vector3, Vector3)>();
            CreateResources(splatCount);
            CreatePropertyBlock();
        }

        public void RecreateResources(uint splatCount)
        {
            if (SplatCount == splatCount)
                return;
            Dispose();
            SplatCount = splatCount;
            CreateResources(splatCount);
            CreatePropertyBlock();
        }

        public void ComputeDepth(CommandBuffer cmd, Matrix4x4 matrixMv) =>
            m_gsplatAsset.ComputeDepth(cmd, matrixMv, m_remainingCount, SorterResource, GsplatResource);

        public void SetActiveSourceIds(uint[] sourceIds)
        {
            m_activeSourceIds = sourceIds ?? throw new ArgumentNullException(nameof(sourceIds));
            GsplatSorter.Instance.SetActiveSourceIds(SorterResource, sourceIds);
            m_cutoutsData = Array.Empty<GsplatCutout.ShaderData>();
            UpdateActiveRangeCount();
            ComputeSortRequired = true;
            ComputeCutoutsRequired = false;
            ForceRefresh();
        }

        public void ClearActiveSourceIds()
        {
            m_activeSourceIds = null;
            GsplatSorter.Instance.ClearActiveSourceIds(SorterResource);
            m_cutoutsData = Array.Empty<GsplatCutout.ShaderData>();
            m_remainingCount = GsplatResource?.UploadedCount ?? 0;
            ComputeSortRequired = true;
            ComputeCutoutsRequired = true;
            ForceRefresh();
        }

        public void PrepareActiveRanges()
        {
            if (m_activeSourceIds == null)
                return;
            UpdateActiveRangeCount();
            ComputeCutoutsRequired = false;
            m_bounds = m_gsplatAsset ? m_gsplatAsset.Bounds : default;
        }

        void UpdateActiveRangeCount()
        {
            var uploadedCount = GsplatResource?.UploadedCount ?? 0;
            var residentCount = CountResidentSourceIds(m_activeSourceIds, uploadedCount);
            if (residentCount == m_remainingCount)
                return;
            m_remainingCount = residentCount;
            ComputeSortRequired = true;
            m_framesBeforeRecomputeSort = 0;
        }

        static uint CountResidentSourceIds(uint[] sourceIds, uint uploadedCount)
        {
            var lo = 0;
            var hi = sourceIds.Length;
            while (lo < hi)
            {
                var mid = lo + ((hi - lo) >> 1);
                if (sourceIds[mid] < uploadedCount)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return (uint)lo;
        }

        Bounds ExtractBounds()
        {
            uint[] boundsData = new uint[6];
            BoundsBuffer.GetData(boundsData);

            Bounds bounds = default;
            Vector3 bmin = new(GsplatUtils.SortableUintToFloat(boundsData[0]),
                GsplatUtils.SortableUintToFloat(boundsData[1]), GsplatUtils.SortableUintToFloat(boundsData[2]));
            Vector3 bmax = new(GsplatUtils.SortableUintToFloat(boundsData[3]),
                GsplatUtils.SortableUintToFloat(boundsData[4]), GsplatUtils.SortableUintToFloat(boundsData[5]));
            bounds.SetMinMax(bmin, bmax);

            if (bounds.extents.sqrMagnitude < 0.01)
                bounds.extents = new Vector3(0.1f, 0.1f, 0.1f);
            return bounds;
        }

        uint ExtractOrderSize(GraphicsBuffer orderBuffer)
        {
            GraphicsBuffer.CopyCount(orderBuffer, OrderSizeBuffer, 0);
            uint[] count = new uint[1];
            OrderSizeBuffer.GetData(count);
            return count[0];
        }

        public void DispatchInitOrder(GsplatCutout[] cutouts, Matrix4x4 matrixWorld, bool cutoutsUpdateBounds)
        {
            if (cutouts.Length == 0)
            {
                if (m_cutoutsData.Length > 0)
                    SorterResource.Initialized = false;
                m_cutoutsData = Array.Empty<GsplatCutout.ShaderData>();
                m_remainingCount = GsplatResource.UploadedCount;
                m_bounds = m_gsplatAsset.Bounds;
                return;
            }

            if (!ComputeCutoutsRequired)
                return;

            SorterResource.Initialized = true;

            var cutoutsUnchanged = m_cutoutsData.Length == cutouts.Length;
            var updatedCutoutsData = new GsplatCutout.ShaderData[cutouts.Length];
            for (int i = 0; i != cutouts.Length; i++)
            {
                updatedCutoutsData[i] = cutouts[i].GetShaderData(matrixWorld);
                if (cutoutsUnchanged)
                    if (updatedCutoutsData[i].matrix != m_cutoutsData[i].matrix ||
                        updatedCutoutsData[i].typeAndFlags != m_cutoutsData[i].typeAndFlags)
                        cutoutsUnchanged = false;
            }

            if (cutoutsUnchanged && m_prevSplatCount == GsplatResource.UploadedCount)
                return;

            m_prevSplatCount = GsplatResource.UploadedCount;
            m_cutoutsData = updatedCutoutsData;
            CutoutsBuffer = m_gsplatAsset.UpdateCutoutsBuffer(CutoutsBuffer, m_cutoutsData);
            if (cutoutsUpdateBounds)
                m_gsplatAsset.UpdateBoundsBuffer(BoundsBuffer);
            m_gsplatAsset.InitOrder(SorterResource, GsplatResource, cutoutsUpdateBounds);
            m_remainingCount = ExtractOrderSize(SorterResource.OrderBuffer);
            m_bounds = cutoutsUpdateBounds ? ExtractBounds() : m_gsplatAsset.Bounds;
        }

        public void BindGsplatAsset(GsplatAsset gsplatAsset, bool asyncUpload = false)
        {
            Debug.Assert(!m_gsplatAsset);
            // Capacity can be reused for a same-count asset, but its prior permutation and
            // cutout membership are asset-specific and must never survive the rebind.
            SorterResource.Initialized = false;
            m_cutoutsData = Array.Empty<GsplatCutout.ShaderData>();
            m_prevSplatCount = uint.MaxValue;
            m_remainingCount = 0;
            ComputeSortRequired = true;
            ComputeCutoutsRequired = true;
            ForceRefresh();

            m_gsplatAsset = gsplatAsset;
            GsplatResource = GsplatResourceManager.Get(gsplatAsset);
            gsplatAsset.SetupMaterialPropertyBlock(m_propertyBlock, GsplatResource);
            if (asyncUpload)
            {
                var uploadTask = gsplatAsset.UploadDataAsync(GsplatResource);
                if (GsplatResource.TryOwnUploadObservation())
                    _ = ObserveAsyncUpload(uploadTask);
            }
            else
                gsplatAsset.UploadData(GsplatResource);
            GsplatSorter.Instance.BindSorterAsset(SorterResource, gsplatAsset);
        }

        static async Task ObserveAsyncUpload(Task uploadTask)
        {
            try
            {
                // BindGsplatAsset is called from MonoBehaviour.Update. Deliberately retain
                // Unity's synchronization context so any failure is logged on the main thread.
                await uploadTask;
            }
            catch (OperationCanceledException)
            {
                // Releasing the last shared-resource reference intentionally cancels upload.
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public void ReleaseGsplatAsset()
        {
            if (m_activeSourceIds != null)
            {
                GsplatSorter.Instance.ClearActiveSourceIds(SorterResource);
                m_activeSourceIds = null;
            }
            GsplatSorter.Instance.UnbindSorterAsset(SorterResource);
            GsplatResourceManager.Release(m_gsplatAsset);
            GsplatResource = null;
            m_gsplatAsset = null;
        }

        /// <summary>
        /// Updates the draw count for the portable CPU-sort path without dispatching the
        /// compute-only cutout/order initialization pass.
        /// </summary>
        public void PrepareCpuFallback()
        {
            m_remainingCount = GsplatResource?.UploadedCount ?? 0;
            m_bounds = m_gsplatAsset ? m_gsplatAsset.Bounds : default;
        }

        void CreateResources(uint splatCount)
        {
            try
            {
                // Ensure the settings asset has initialized the sorter before choosing a backend.
                // GsplatRendererImpl can otherwise be created before GsplatSettings.OnEnable runs.
                _ = GsplatSettings.Instance;
                bool useVertexDataTextures = GsplatUtils.UseVertexDataTextures;
                if (useVertexDataTextures)
                {
                    OrderTexture = GsplatUtils.CreateVertexDataTexture((int)splatCount,
                        GraphicsFormat.R8G8B8A8_UNorm, "Gsplat.Order.Texture");
                }
                else
                {
                    // The CPU path only needs a shader-readable structured buffer. Avoid requiring
                    // append/UAV behavior when the compute-only cutout path is disabled.
                    var orderTarget = GsplatSorter.Instance.CpuFallbackEnabled
                        ? GraphicsBuffer.Target.Structured
                        : GraphicsBuffer.Target.Append;
                    OrderBuffer = new GraphicsBuffer(orderTarget, (int)splatCount, sizeof(uint));
                }

                SorterResource = GsplatSorter.Instance.CreateSorterResource(splatCount, OrderBuffer, OrderTexture);
                m_cutoutsData = Array.Empty<GsplatCutout.ShaderData>();
                CutoutsBuffer = null;
                if (!GsplatSorter.Instance.CpuFallbackEnabled)
                {
                    OrderSizeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, sizeof(uint));
                    BoundsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 6, sizeof(uint));
                }
            }
            catch
            {
                SorterResource?.Dispose();
                SorterResource = null;
                OrderBuffer?.Dispose();
                OrderBuffer = null;
                GsplatUtils.DestroyRuntimeTexture(OrderTexture);
                OrderTexture = null;
                OrderSizeBuffer?.Dispose();
                OrderSizeBuffer = null;
                BoundsBuffer?.Dispose();
                BoundsBuffer = null;
                throw;
            }
        }

        void CreatePropertyBlock()
        {
            m_propertyBlock ??= new MaterialPropertyBlock();
            if (OrderTexture)
            {
                m_propertyBlock.SetTexture(k_orderTexture, OrderTexture);
                m_propertyBlock.SetInteger(k_gsplatTextureWidth, OrderTexture.width);
            }
            else
            {
                m_propertyBlock.SetBuffer(k_orderBuffer, OrderBuffer);
            }
        }

        public void Dispose()
        {
            ReleaseGsplatAsset();
            m_activeSourceIds = null;
            OrderBuffer?.Dispose();
            OrderBuffer = null;
            GsplatUtils.DestroyRuntimeTexture(OrderTexture);
            OrderTexture = null;
            SorterResource?.Dispose();
            SorterResource = null;
            CutoutsBuffer?.Dispose();
            CutoutsBuffer = null;
            OrderSizeBuffer?.Dispose();
            OrderSizeBuffer = null;
            BoundsBuffer?.Dispose();
            BoundsBuffer = null;
        }

        public void ForceRefresh()
        {
            m_framesBeforeRecomputeSort = 0;
            m_sortsBeforeRecomputeCutouts = 0;
        }

        public void RefreshOnCameraMove()
        {
            foreach (var cam in Camera.allCameras)
            {
                if (m_prevCamTransforms.TryGetValue(cam, out (Vector3, Vector3) prevCamTransform))
                {
                    (Vector3 prevCamPos, Vector3 prevCamRot) = prevCamTransform;

                    if ((cam.transform.position - prevCamPos).magnitude >
                        GsplatSettings.Instance.CameraTranslationRefreshTreshold
                        || (cam.transform.eulerAngles - prevCamRot).magnitude >
                        GsplatSettings.Instance.CameraRotationRefreshTreshold)
                    {
                        m_prevCamTransforms[cam] = (cam.transform.position, cam.transform.eulerAngles);
                        ForceRefresh();
                    }
                }
                else
                {
                    m_prevCamTransforms.Add(cam, (cam.transform.position, cam.transform.eulerAngles));
                    ForceRefresh();
                }
            }
        }

        public void EvaluateRefreshRequired(GsplatRenderer.GsplatSortMode mode, uint sortRefreshRate,
            uint cutoutsRefreshRate)
        {
            if (mode == GsplatRenderer.GsplatSortMode.Always)
            {
                sortRefreshRate = 0;
                cutoutsRefreshRate = 0;
            }

            if (mode == GsplatRenderer.GsplatSortMode.SortEveryNFrames)
            {
                cutoutsRefreshRate = 0;
            }

            RefreshOnCameraMove();

            ComputeSortRequired = false;
            ComputeCutoutsRequired = false;

            if (m_framesBeforeRecomputeSort == 0)
            {
                m_framesBeforeRecomputeSort = sortRefreshRate;
                ComputeSortRequired = true;
                if (m_sortsBeforeRecomputeCutouts == 0)
                {
                    m_sortsBeforeRecomputeCutouts = cutoutsRefreshRate;
                    ComputeCutoutsRequired = true;
                }
                else
                    m_sortsBeforeRecomputeCutouts -= 1;
            }
            else
                m_framesBeforeRecomputeSort -= 1;
        }

        /// <summary>
        /// Render the splats.
        /// </summary>
        /// <param name="transform">Object transform.</param>
        /// <param name="layer">Layer used for rendering.</param>
        /// <param name="gammaToLinear">Covert color space from Gamma to Linear.</param>
        /// <param name="shDegree">Order of SH coefficients used for rendering. The final value is capped by the SHBands property.</param>
        /// <param name="brightness">Brightness color scaling.</param>
        /// <param name="scaleFactor">Splats uv scaling factor, reduce splat size while trying to keep visual fidelity.</param>
        /// <param name="renderOrder">Manual render order placement of the gsplat. The final value is capped by the maximum render order setting.</param>
        public void Render(Transform transform, int layer, bool gammaToLinear = false, int shDegree = 3,
            float brightness = 1.0f, float scaleFactor = 1.0f, uint renderOrder = 0,
            GsplatRelighting relighting = null, float relightingBlend = 1f,
            float relightingBrightness = 2f, float relightingBackground = 1f)
        {
            if (m_remainingCount <= 0)
                return;

            m_propertyBlock.SetInteger(k_splatCount, (int)m_remainingCount);
            m_propertyBlock.SetInteger(k_gammaToLinear, gammaToLinear ? 1 : 0);
            m_propertyBlock.SetInteger(k_splatInstanceSize, (int)GsplatSettings.Instance.SplatInstanceSize);
            m_propertyBlock.SetInteger(k_shDegree, Math.Min(m_gsplatAsset.SHBands, shDegree));
            m_propertyBlock.SetFloat(k_brightness, brightness);
            m_propertyBlock.SetFloat(k_scaleFactor, scaleFactor);
            m_propertyBlock.SetInteger(k_antialiased, m_gsplatAsset.Antialiased ? 1 : 0);
            m_propertyBlock.SetMatrix(k_matrixM, transform.localToWorldMatrix);
            if (relighting && relighting.IsReady)
            {
                m_propertyBlock.SetTexture(k_relightMap, relighting.RelightingTexture);
                m_propertyBlock.SetVector(k_relightScreen, relighting.SourceScreen);
                m_propertyBlock.SetVector(k_relightParams, new Vector4(
                    relightingBlend,
                    relightingBrightness,
                    relightingBackground,
                    1f));
            }
            else
            {
                // Always bind a valid texture because some mobile drivers validate sampler
                // bindings even when the dynamic relighting branch is disabled.
                m_propertyBlock.SetTexture(k_relightMap, Texture2D.blackTexture);
                m_propertyBlock.SetVector(k_relightScreen, Vector4.zero);
                m_propertyBlock.SetVector(k_relightParams, Vector4.zero);
            }

            uint order = Math.Clamp(renderOrder, 0, GsplatSettings.Instance.MaxRenderOrder - 1);
            var rp = new RenderParams(m_gsplatAsset.Materials[order])
            {
                worldBounds = GsplatUtils.CalcWorldBounds(m_bounds, transform),
                matProps = m_propertyBlock,
                layer = layer
            };

            Graphics.RenderMeshPrimitives(rp, GsplatSettings.Instance.Mesh, 0,
                Mathf.CeilToInt(m_remainingCount / (float)GsplatSettings.Instance.SplatInstanceSize));
        }
    }
}
