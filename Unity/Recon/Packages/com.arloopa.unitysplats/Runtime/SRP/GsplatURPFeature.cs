// Originated from the GaussianSplatHDRPPass in aras-p/UnityGaussianSplatting by Aras Pranckevičius
// https://github.com/aras-p/UnityGaussianSplatting/blob/main/package/Runtime/GaussianSplatHDRPPass.cs
// Copyright (c) 2023 Aras Pranckevičius
// Modified by Yize Wu
// Copyright (c) 2025 Yize Wu
// SPDX-License-Identifier: MIT

#if GSPLAT_ENABLE_URP

using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace Gsplat
{
    class GsplatURPFeature : ScriptableRendererFeature
    {
        class GsplatRenderPass : ScriptableRenderPass
        {
            // Unity 6000.0-6000.3 still expose Execute for URP compatibility mode.
            // Unity 6000.4 / URP 17.4 removed that override point and use only the
            // RecordRenderGraph path below.
            public CommandBuffer CommandBuffer;

#if UNITY_6000_0_OR_NEWER
            class PassData
            {
                public UniversalCameraData CameraData;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using var builder = renderGraph.AddUnsafePass(GsplatSorter.k_passName, out PassData passData);
                passData.CameraData = frameData.Get<UniversalCameraData>();
                builder.AllowPassCulling(false);
                // DispatchSort records camera-local relighting globals. RenderGraph must know
                // that this pass mutates global shader state, otherwise it may reorder the pass
                // or let another camera's Scene/Game binding remain active for transparent draws.
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    var commandBuffer = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    GsplatSorter.Instance.DispatchSort(commandBuffer, data.CameraData.camera);
                });
            }
#endif

#if !UNITY_6000_4_OR_NEWER
#pragma warning disable CS0672 // Compatibility-mode override is obsolete in RenderGraph mode.
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (CommandBuffer == null)
                    return;
                GsplatSorter.Instance.DispatchSort(CommandBuffer, renderingData.cameraData.camera);
                context.ExecuteCommandBuffer(CommandBuffer);
            }
#pragma warning restore CS0672
#endif
        }

        GsplatRenderPass m_pass;
        bool m_hasGsplats;

        public override void Create()
        {
            m_pass = new GsplatRenderPass { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        }

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            m_hasGsplats = GsplatSorter.Instance.GatherGsplatsForCamera(cameraData.camera);
            m_pass.CommandBuffer ??= new CommandBuffer { name = "SortGsplats" };
            m_pass.CommandBuffer.Clear();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // CPU fallback sorting is consumed by GsplatPlayerLoopHook, but it still needs this
            // lightweight pass so each camera receives its own relighting texture binding.
            if (GsplatSettings.Instance.Valid && m_hasGsplats)
                renderer.EnqueuePass(m_pass);
        }

        protected override void Dispose(bool disposing)
        {
            m_pass.CommandBuffer?.Dispose();
            m_pass.CommandBuffer = null;
            m_pass = null;
        }
    }
}

#endif
