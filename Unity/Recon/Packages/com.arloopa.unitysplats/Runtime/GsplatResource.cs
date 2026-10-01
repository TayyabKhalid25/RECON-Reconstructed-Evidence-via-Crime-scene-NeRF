using System.Runtime.InteropServices;
using UnityEngine;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Experimental.Rendering;

namespace Gsplat
{
    public abstract class GsplatResource
    {
        public bool Uploaded;
        public uint UploadedCount;
        public Task UploadTask { get; private set; } = Task.CompletedTask;
        public bool Disposed { get; private set; }

        CancellationTokenSource m_uploadCancellation;
        bool m_uploadTaskObserved;

        internal CancellationToken BeginAsyncUpload()
        {
            if (Disposed)
                throw new ObjectDisposedException(GetType().Name);
            m_uploadCancellation?.Dispose();
            m_uploadCancellation = new CancellationTokenSource();
            m_uploadTaskObserved = false;
            return m_uploadCancellation.Token;
        }

        internal void OwnUploadTask(Task task)
        {
            UploadTask = task ?? Task.CompletedTask;
        }

        internal bool TryOwnUploadObservation()
        {
            if (m_uploadTaskObserved)
                return false;
            m_uploadTaskObserved = true;
            return true;
        }

        /// <summary>
        /// Marks this resource disposed and cancels its owned upload before derived classes
        /// release any GraphicsBuffers. Returns false for an already-disposed resource.
        /// </summary>
        protected bool BeginDispose()
        {
            if (Disposed)
                return false;
            Disposed = true;
            m_uploadCancellation?.Cancel();
            m_uploadCancellation?.Dispose();
            m_uploadCancellation = null;
            return true;
        }

        public abstract void Dispose();
    }

    public class GsplatResourceUncompressed : GsplatResource
    {
        const int k_float4Stride = sizeof(float) * 4;

        public GraphicsBuffer PositionBuffer { get; private set; }
        public GraphicsBuffer ScaleBuffer { get; private set; }
        public GraphicsBuffer RotationBuffer { get; private set; }
        public GraphicsBuffer ColorBuffer { get; private set; }
        public GraphicsBuffer SHBuffer { get; private set; }
        public Texture2D PositionTexture { get; private set; }
        public Texture2D ScaleTexture { get; private set; }
        public Texture2D RotationTexture { get; private set; }
        public Texture2D ColorTexture { get; private set; }
        public Texture2D SHTexture { get; private set; }
        public bool UsesVertexTextures { get; }

        public GsplatResourceUncompressed(uint splatCount, byte shBands)
        {
            if (splatCount == 0)
                return;
            UsesVertexTextures = GsplatUtils.UseVertexDataTextures;
            if (UsesVertexTextures)
            {
                int count = checked((int)splatCount);
                PositionTexture = GsplatUtils.CreateVertexDataTexture(count,
                    GraphicsFormat.R32G32B32A32_SFloat, "Gsplat.Positions.Texture");
                ScaleTexture = GsplatUtils.CreateVertexDataTexture(count,
                    GraphicsFormat.R32G32B32A32_SFloat, "Gsplat.Scales.Texture");
                RotationTexture = GsplatUtils.CreateVertexDataTexture(count,
                    GraphicsFormat.R32G32B32A32_SFloat, "Gsplat.Rotations.Texture");
                ColorTexture = GsplatUtils.CreateVertexDataTexture(count,
                    GraphicsFormat.R32G32B32A32_SFloat, "Gsplat.Colors.Texture");
                if (shBands > 0)
                    SHTexture = GsplatUtils.CreateVertexDataTexture(
                        checked(GsplatUtils.SHBandsToCoefficientCount(shBands) * count),
                        GraphicsFormat.R32G32B32A32_SFloat, "Gsplat.SH.Texture");
                return;
            }
            // Vector3 stays compact in serialized CPU assets, but GPU elements use a
            // cross-API-safe float4 stride. Upload expands through bounded staging.
            PositionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                k_float4Stride);
            ScaleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                k_float4Stride);
            RotationBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                k_float4Stride);
            ColorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                k_float4Stride);
            if (shBands > 0)
                SHBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured,
                    GsplatUtils.SHBandsToCoefficientCount(shBands) * (int)splatCount, k_float4Stride);
        }

        public override void Dispose()
        {
            if (!BeginDispose())
                return;
            PositionBuffer?.Dispose();
            PositionBuffer = null;
            ScaleBuffer?.Dispose();
            ScaleBuffer = null;
            RotationBuffer?.Dispose();
            RotationBuffer = null;
            ColorBuffer?.Dispose();
            ColorBuffer = null;
            SHBuffer?.Dispose();
            SHBuffer = null;
            GsplatUtils.DestroyRuntimeTexture(PositionTexture);
            PositionTexture = null;
            GsplatUtils.DestroyRuntimeTexture(ScaleTexture);
            ScaleTexture = null;
            GsplatUtils.DestroyRuntimeTexture(RotationTexture);
            RotationTexture = null;
            GsplatUtils.DestroyRuntimeTexture(ColorTexture);
            ColorTexture = null;
            GsplatUtils.DestroyRuntimeTexture(SHTexture);
            SHTexture = null;
        }
    }

    public class GsplatResourceSpark : GsplatResource
    {
        public GraphicsBuffer PackedSplatsBuffer { get; private set; }
        public GraphicsBuffer PackedSH1Buffer { get; private set; }
        public GraphicsBuffer PackedSH2Buffer { get; private set; }
        public GraphicsBuffer PackedSH3Buffer { get; private set; }
        public GraphicsBuffer PackedSH4Buffer { get; private set; }
        public Texture2D PackedSplatsTexture { get; private set; }
        public Texture2D PackedSH1Texture { get; private set; }
        public Texture2D PackedSH2Texture { get; private set; }
        public Texture2D PackedSH3Texture { get; private set; }
        public Texture2D PackedSH4Texture { get; private set; }
        public bool UsesVertexTextures { get; }

        public GsplatResourceSpark(uint splatCount, byte shBands) : base()
        {
            if (splatCount == 0)
                return;
            UsesVertexTextures = GsplatUtils.UseVertexDataTextures;
            if (UsesVertexTextures)
            {
                int count = checked((int)splatCount);
                PackedSplatsTexture = GsplatUtils.CreateVertexDataTexture(checked(count * 4),
                    GraphicsFormat.R8G8B8A8_UNorm, "Gsplat.Packed.Texture");
                if (shBands >= 1)
                    PackedSH1Texture = GsplatUtils.CreateVertexDataTexture(checked(count * 2),
                        GraphicsFormat.R8G8B8A8_UNorm, "Gsplat.SH1.Texture");
                if (shBands >= 2)
                    PackedSH2Texture = GsplatUtils.CreateVertexDataTexture(checked(count * 4),
                        GraphicsFormat.R8G8B8A8_UNorm, "Gsplat.SH2.Texture");
                if (shBands >= 3)
                    PackedSH3Texture = GsplatUtils.CreateVertexDataTexture(checked(count * 4),
                        GraphicsFormat.R8G8B8A8_UNorm, "Gsplat.SH3.Texture");
                if (shBands >= 4)
                    PackedSH4Texture = GsplatUtils.CreateVertexDataTexture(checked(count * 4),
                        GraphicsFormat.R8G8B8A8_UNorm, "Gsplat.SH4.Texture");
                return;
            }
            PackedSplatsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                sizeof(uint) * 4);
            if (shBands >= 1)
                PackedSH1Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                    sizeof(uint) * 2);
            if (shBands >= 2)
                PackedSH2Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                    sizeof(uint) * 4);
            if (shBands >= 3)
                PackedSH3Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                    sizeof(uint) * 4);
            if (shBands >= 4)
                PackedSH4Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, (int)splatCount,
                    sizeof(uint) * 4);
        }

        public override void Dispose()
        {
            if (!BeginDispose())
                return;
            PackedSplatsBuffer?.Dispose();
            PackedSplatsBuffer = null;
            PackedSH1Buffer?.Dispose();
            PackedSH1Buffer = null;
            PackedSH2Buffer?.Dispose();
            PackedSH2Buffer = null;
            PackedSH3Buffer?.Dispose();
            PackedSH3Buffer = null;
            PackedSH4Buffer?.Dispose();
            PackedSH4Buffer = null;
            GsplatUtils.DestroyRuntimeTexture(PackedSplatsTexture);
            PackedSplatsTexture = null;
            GsplatUtils.DestroyRuntimeTexture(PackedSH1Texture);
            PackedSH1Texture = null;
            GsplatUtils.DestroyRuntimeTexture(PackedSH2Texture);
            PackedSH2Texture = null;
            GsplatUtils.DestroyRuntimeTexture(PackedSH3Texture);
            PackedSH3Texture = null;
            GsplatUtils.DestroyRuntimeTexture(PackedSH4Texture);
            PackedSH4Texture = null;
        }
    }
}
