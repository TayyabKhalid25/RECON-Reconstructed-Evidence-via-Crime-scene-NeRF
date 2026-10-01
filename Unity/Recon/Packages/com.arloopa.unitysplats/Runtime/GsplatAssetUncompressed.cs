// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using Gsplat.Formats;

namespace Gsplat
{
    public class GsplatAssetUncompressed : GsplatAsset
    {
        public override CompressionMode Compression => CompressionMode.Uncompressed;

        [HideInInspector] public Vector3[] Positions;
        [HideInInspector] public Vector4[] Colors; // RGB, Opacity
        [HideInInspector] public Vector3[] SHs;
        [HideInInspector] public Vector3[] Scales;
        [HideInInspector] public Vector4[] Rotations; // Quaternion, wxyz

        static readonly int k_positionBuffer = Shader.PropertyToID("_PositionBuffer");
        static readonly int k_scaleBuffer = Shader.PropertyToID("_ScaleBuffer");
        static readonly int k_rotationBuffer = Shader.PropertyToID("_RotationBuffer");
        static readonly int k_colorBuffer = Shader.PropertyToID("_ColorBuffer");
        static readonly int k_shBuffer = Shader.PropertyToID("_SHBuffer");
        static readonly int k_positionTexture = Shader.PropertyToID("_PositionTexture");
        static readonly int k_scaleTexture = Shader.PropertyToID("_ScaleTexture");
        static readonly int k_rotationTexture = Shader.PropertyToID("_RotationTexture");
        static readonly int k_colorTexture = Shader.PropertyToID("_ColorTexture");
        static readonly int k_shTexture = Shader.PropertyToID("_SHTexture");
        static readonly int k_gsplatTextureWidth = Shader.PropertyToID("_GsplatTextureWidth");
        static readonly int k_shTextureWidth = Shader.PropertyToID("_SHTextureWidth");
        static readonly int k_splatCount = Shader.PropertyToID("_SplatCount");
        static readonly int k_matrixMv = Shader.PropertyToID("_MatrixMV");
        static readonly int k_depthBuffer = Shader.PropertyToID("_DepthBuffer");
        static readonly int k_orderBuffer = Shader.PropertyToID("_OrderBuffer");

        // Reused while expanding compact CPU Vector3 arrays to cross-API-safe GPU float4
        // elements. This caps transient memory at about 1.6 MB per uploading asset.
        const int k_maxVector4StagingElements = 100_000;

        public override void Allocate()
        {
            Positions = new Vector3[SplatCount];
            Colors = new Vector4[SplatCount];
            Scales = new Vector3[SplatCount];
            Rotations = new Vector4[SplatCount];
            if (SHBands > 0)
                SHs = new Vector3[SplatCount * GsplatUtils.SHBandsToCoefficientCount(SHBands)];
        }

        public override void LoadFromDecoded(GsplatDecodedData data, ProgressCallback progressCallback = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            data.Validate();
            SplatCount = checked((uint)data.Count);
            SHBands = data.SHBands;
            Antialiased = data.Antialiased;
            Bounds = data.Bounds;
            Positions = data.Positions;
            Colors = data.Colors;
            Scales = data.Scales;
            Rotations = data.Rotations;
            SHs = data.SHs;
            progressCallback?.Invoke("Preparing uncompressed splats", 1f);
        }

        public override GsplatResource CreateResource()
        {
            return new GsplatResourceUncompressed(SplatCount, SHBands);
        }

        protected override void _UploadData(GsplatResource resource)
        {
            var res = (GsplatResourceUncompressed)resource;
            if (res.UsesVertexTextures)
            {
                UploadVertexTextures(res);
                return;
            }
            var staging = new Vector4[GetVector4StagingCapacity()];
            UploadVector3Buffer(res.PositionBuffer, Positions, staging);
            UploadVector3Buffer(res.ScaleBuffer, Scales, staging);
            res.RotationBuffer.SetData(Rotations);
            res.ColorBuffer.SetData(Colors);
            if (SHBands > 0)
                UploadVector3Buffer(res.SHBuffer, SHs, staging);
        }

        protected override async Task _UploadDataAsync(GsplatResource resource,
            CancellationToken cancellationToken)
        {
            var res = (GsplatResourceUncompressed)resource;
            if (res.UsesVertexTextures)
            {
                cancellationToken.ThrowIfCancellationRequested();
                UploadVertexTextures(res);
                res.UploadedCount = SplatCount;
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }
            var staging = new Vector4[GetVector4StagingCapacity()];
            while (res.UploadedCount < SplatCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // The staging capacity is at least one, even if UploadBatchSize was set to zero.
                var batchSize = (int)Math.Min((uint)staging.Length, SplatCount - res.UploadedCount);
                var splatStart = (int)res.UploadedCount;
                UploadVector3Range(res.PositionBuffer, Positions, splatStart, splatStart, batchSize, staging,
                    cancellationToken);
                UploadVector3Range(res.ScaleBuffer, Scales, splatStart, splatStart, batchSize, staging,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                res.RotationBuffer.SetData(Rotations, splatStart, splatStart, batchSize);
                cancellationToken.ThrowIfCancellationRequested();
                res.ColorBuffer.SetData(Colors, splatStart, splatStart, batchSize);

                if (SHBands > 0)
                {
                    var coefficientCount = GsplatUtils.SHBandsToCoefficientCount(SHBands);
                    var shStart = coefficientCount * splatStart;
                    var shRemaining = coefficientCount * batchSize;
                    while (shRemaining > 0)
                    {
                        var count = Math.Min(staging.Length, shRemaining);
                        UploadVector3Range(res.SHBuffer, SHs, shStart, shStart, count, staging,
                            cancellationToken);
                        shStart += count;
                        shRemaining -= count;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                res.UploadedCount += (uint)batchSize;
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        int GetVector4StagingCapacity()
        {
            var sourceElementCount = Math.Max(Positions?.LongLength ?? 0L,
                Math.Max(Scales?.LongLength ?? 0L, SHs?.LongLength ?? 0L));
            var configuredBatchSize = GsplatSettings.Instance.UploadBatchSize == 0
                ? k_maxVector4StagingElements
                : (long)GsplatSettings.Instance.UploadBatchSize;
            var bounded = Math.Min(sourceElementCount,
                Math.Min(configuredBatchSize, k_maxVector4StagingElements));
            return (int)Math.Max(1L, bounded);
        }

        static void UploadVector3Buffer(GraphicsBuffer destination, Vector3[] source, Vector4[] staging)
        {
            for (var start = 0; start < source.Length; start += staging.Length)
            {
                var count = Math.Min(staging.Length, source.Length - start);
                UploadVector3Range(destination, source, start, start, count, staging);
            }
        }

        static void UploadVector3Range(GraphicsBuffer destination, Vector3[] source, int sourceStart,
            int destinationStart, int count, Vector4[] staging,
            CancellationToken cancellationToken = default)
        {
            Debug.Assert(count >= 0 && count <= staging.Length);
            for (var i = 0; i < count; ++i)
            {
                var value = source[sourceStart + i];
                staging[i] = new Vector4(value.x, value.y, value.z, 0f);
            }
            cancellationToken.ThrowIfCancellationRequested();
            destination.SetData(staging, 0, destinationStart, count);
        }

        public override void SetupMaterialPropertyBlock(MaterialPropertyBlock propertyBlock,
            GsplatResource resource)
        {
            var res = (GsplatResourceUncompressed)resource;
            if (res.UsesVertexTextures)
            {
                propertyBlock.SetInteger(k_gsplatTextureWidth, res.PositionTexture.width);
                propertyBlock.SetTexture(k_positionTexture, res.PositionTexture);
                propertyBlock.SetTexture(k_scaleTexture, res.ScaleTexture);
                propertyBlock.SetTexture(k_rotationTexture, res.RotationTexture);
                propertyBlock.SetTexture(k_colorTexture, res.ColorTexture);
                if (SHBands > 0)
                {
                    propertyBlock.SetInteger(k_shTextureWidth, res.SHTexture.width);
                    propertyBlock.SetTexture(k_shTexture, res.SHTexture);
                }
                return;
            }

            var cs = GsplatMaterial.InitOrderShader;
            m_kernelInitOrder = cs.FindKernel("InitOrder");
            propertyBlock.SetBuffer(k_positionBuffer, res.PositionBuffer);
            propertyBlock.SetBuffer(k_scaleBuffer, res.ScaleBuffer);
            propertyBlock.SetBuffer(k_rotationBuffer, res.RotationBuffer);
            propertyBlock.SetBuffer(k_colorBuffer, res.ColorBuffer);
            if (SHBands > 0)
                propertyBlock.SetBuffer(k_shBuffer, res.SHBuffer);
        }

        void UploadVertexTextures(GsplatResourceUncompressed res)
        {
            Vector4[] positions = ExpandVector3(Positions);
            Vector4[] scales = ExpandVector3(Scales);
            GsplatUtils.UploadVertexDataTexture(res.PositionTexture, positions, positions.Length);
            GsplatUtils.UploadVertexDataTexture(res.ScaleTexture, scales, scales.Length);
            GsplatUtils.UploadVertexDataTexture(res.RotationTexture, Rotations, Rotations.Length);
            GsplatUtils.UploadVertexDataTexture(res.ColorTexture, Colors, Colors.Length);
            if (SHBands > 0)
            {
                Vector4[] sh = ExpandVector3(SHs);
                GsplatUtils.UploadVertexDataTexture(res.SHTexture, sh, sh.Length);
            }
        }

        static Vector4[] ExpandVector3(Vector3[] source)
        {
            var result = new Vector4[source.Length];
            for (int i = 0; i < source.Length; ++i)
                result[i] = new Vector4(source[i].x, source[i].y, source[i].z, 0f);
            return result;
        }

        public override void ComputeDepth(CommandBuffer cmd, Matrix4x4 matrixMv,
            ISorterResource sorterResource, GsplatResource resource) =>
            ComputeDepth(cmd, matrixMv, resource.UploadedCount, sorterResource, resource);

        public override void ComputeDepth(CommandBuffer cmd, Matrix4x4 matrixMv, uint activeCount,
            ISorterResource sorterResource, GsplatResource resource)
        {
            var res = (GsplatResourceUncompressed)resource;
            var cs = GsplatMaterial.CalcDepthShader;
            var kernelCalcDepth = 0;
            cmd.SetComputeIntParam(cs, k_splatCount, (int)activeCount);
            cmd.SetComputeMatrixParam(cs, k_matrixMv, matrixMv);
            cmd.SetComputeBufferParam(cs, kernelCalcDepth, k_positionBuffer, res.PositionBuffer);
            cmd.SetComputeBufferParam(cs, kernelCalcDepth, k_depthBuffer, sorterResource.InputKeys);
            cmd.SetComputeBufferParam(cs, kernelCalcDepth, k_orderBuffer, sorterResource.OrderBuffer);
            cmd.DispatchCompute(cs, kernelCalcDepth, (int)GsplatUtils.DivRoundUp(activeCount, 256), 1, 1);
        }

        public override void InitOrder(ISorterResource sorterResource, GsplatResource resource, bool updateBounds)
        {
            var cs = GsplatMaterial.InitOrderShader;
            var res = (GsplatResourceUncompressed)resource;
            sorterResource.OrderBuffer.SetCounterValue(0);
            cs.SetInt(k_splatCount, (int)res.UploadedCount);
            cs.SetBuffer(m_kernelInitOrder, k_orderBuffer, sorterResource.OrderBuffer);
            cs.SetBuffer(m_kernelInitOrder, k_positionBuffer, res.PositionBuffer);
            if (updateBounds)
                cs.EnableKeyword("UPDATE_BOUNDS");
            else
                cs.DisableKeyword("UPDATE_BOUNDS");
            cs.Dispatch(m_kernelInitOrder, (int)GsplatUtils.DivRoundUp(res.UploadedCount, 256), 1, 1);
        }

        public override void LoadFromPly(string plyPath, ProgressCallback progressCallback = null,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUF)
        {
            using var fs = new FileStream(plyPath, FileMode.Open, FileAccess.Read);
            // C# arrays and NativeArrays make it hard to have a "byte" array larger than 2GB :/
            if (fs.Length >= 2 * 1024 * 1024 * 1024L)
                throw new NotSupportedException("currently files larger than 2GB are not supported");

            LoadFromPlyStream(fs, progressCallback, sourceCoordinates);
        }

        public override void LoadFromPlyBytes(byte[] plyBytes, ProgressCallback progressCallback = null,
            SourceCoordinates sourceCoordinates = SourceCoordinates.RUF)
        {
            if (plyBytes == null || plyBytes.Length == 0)
                throw new ArgumentException("PLY byte array is null or empty.", nameof(plyBytes));
            using var ms = new MemoryStream(plyBytes, writable: false);
            LoadFromPlyStream(ms, progressCallback, sourceCoordinates);
        }

        void LoadFromPlyStream(Stream fs, ProgressCallback progressCallback, SourceCoordinates sourceCoordinates)
        {
            var plyInfo = new PlyHeaderInfo(fs);
            var shCoeffs = plyInfo.SHPropertyCount / 3;
            SplatCount = plyInfo.VertexCount;
            SHBands = GsplatUtils.CalcSHBandsFromSHPropertyCount(plyInfo.SHPropertyCount);

            if (SHBands > 4 || GsplatUtils.SHBandsToCoefficientCount(SHBands) * 3 != plyInfo.SHPropertyCount)
                throw new NotSupportedException($"unexpected SH property count {plyInfo.SHPropertyCount}");

            if (plyInfo.PositionOffset == -1 || plyInfo.ColorOffset == -1 || plyInfo.OpacityOffset == -1 ||
                plyInfo.ScaleOffset == -1 || plyInfo.RotationOffset == -1)
                throw new NotSupportedException("missing required properties in PLY header");

            var (posXSign, posYSign, posZSign) = GsplatUtils.AxisSigns(sourceCoordinates);
            float rotXSign = posYSign * posZSign;
            float rotYSign = posXSign * posZSign;
            float rotZSign = posXSign * posYSign;

            Allocate();
            var buffer = new byte[plyInfo.PropertyCount * sizeof(float)];
            for (uint i = 0; i < plyInfo.VertexCount; i++)
            {
                var readBytes = fs.Read(buffer);
                if (readBytes != buffer.Length)
                    throw new EndOfStreamException($"unexpected end of file, got {readBytes} bytes at vertex {i}");

                var properties = MemoryMarshal.Cast<byte, float>(buffer);
                Positions[i] = new Vector3(
                    posXSign * properties[plyInfo.PositionOffset],
                    posYSign * properties[plyInfo.PositionOffset + 1],
                    posZSign * properties[plyInfo.PositionOffset + 2]);
                Colors[i] = new Vector4(
                    properties[plyInfo.ColorOffset],
                    properties[plyInfo.ColorOffset + 1],
                    properties[plyInfo.ColorOffset + 2],
                    GsplatUtils.Sigmoid(properties[plyInfo.OpacityOffset]));

                for (int j = 0, bandOffset = 0; j < SHBands; j++)
                {
                    int bandSize = (j + 1) * 2 + 1; // band l = j+1 has 2l+1 coefficients
                    for (int k = 0; k < bandSize; k++)
                    {
                        float sign = GsplatUtils.ShSign(sourceCoordinates, j + 1, k);
                        int idx = (int)i * shCoeffs + bandOffset + k;
                        SHs[idx] = sign * new Vector3(
                            properties[bandOffset + k + plyInfo.SHOffset],
                            properties[bandOffset + k + plyInfo.SHOffset + shCoeffs],
                            properties[bandOffset + k + plyInfo.SHOffset + shCoeffs * 2]);
                    }
                    bandOffset += bandSize;
                }

                Scales[i] = new Vector3(
                    Mathf.Exp(properties[plyInfo.ScaleOffset]),
                    Mathf.Exp(properties[plyInfo.ScaleOffset + 1]),
                    Mathf.Exp(properties[plyInfo.ScaleOffset + 2]));
                Rotations[i] = new Vector4(
                    properties[plyInfo.RotationOffset],
                    rotXSign * properties[plyInfo.RotationOffset + 1],
                    rotYSign * properties[plyInfo.RotationOffset + 2],
                    rotZSign * properties[plyInfo.RotationOffset + 3]).normalized;

                if (i == 0) Bounds = new Bounds(Positions[i], Vector3.zero);
                else Bounds.Encapsulate(Positions[i]);

                progressCallback?.Invoke("Reading vertices", i / (float)plyInfo.VertexCount);
            }
        }
    }
}
