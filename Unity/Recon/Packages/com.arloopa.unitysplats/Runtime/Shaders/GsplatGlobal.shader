// Copyright (c) 2026 Keir Rice
// SPDX-License-Identifier: MIT
//
// Unified draw shader for global K-way merged Gaussian splatting.
// Reads from concatenated global buffers (GlobalPackedBuffer, GlobalSH*Buffers)
// via indices from GlobalOrderBuffer. Per-renderer transforms applied via
// RendererTransforms structured buffer.

Shader "Gsplat/Global"
{
    Properties {}
    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Pass
        {
            ZWrite Off
            Blend One OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require compute
            #pragma multi_compile SH_BANDS_0 SH_BANDS_1 SH_BANDS_2 SH_BANDS_3 SH_BANDS_4

            #include "UnityCG.cginc"

            int _SplatInstanceSize;
            sampler2D _GsplatRelightMap;
            float4 _GsplatRelightScreen;
            sampler2D _GsplatCameraRelightMap;
            float _GsplatCameraRelightOverride;
            float4 _GsplatCameraRelightScreen;
            float4x4 _GsplatCameraRelightView;
            float4 _GsplatCameraRelightPosition;
            float4 _GsplatCameraRelightProjection;

            #include "GsplatSparkGlobal.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                #if !defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(UNITY_STEREO_INSTANCING_ENABLED)
                uint instanceID : SV_InstanceID;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                nointerpolation uint rendererId : TEXCOORD1;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            bool GsplatCameraOverrideMatchesCurrentCamera()
            {
                float3 positionDelta =
                    _GsplatCameraRelightPosition.xyz - _WorldSpaceCameraPos.xyz;
                float positionError = dot(positionDelta, positionDelta);

                const float3 weights = float3(1.0, 1.0, 1.0);
                float rotationError =
                    dot(abs(_GsplatCameraRelightView[0].xyz - UNITY_MATRIX_V[0].xyz), weights) +
                    dot(abs(_GsplatCameraRelightView[1].xyz - UNITY_MATRIX_V[1].xyz), weights) +
                    dot(abs(_GsplatCameraRelightView[2].xyz - UNITY_MATRIX_V[2].xyz), weights);

                float2 currentProjection = float2(
                    abs(UNITY_MATRIX_P[0][0]),
                    abs(UNITY_MATRIX_P[1][1]));
                float2 projectionError =
                    abs(_GsplatCameraRelightProjection.xy - currentProjection);

                return positionError < 1e-4 &&
                       rotationError < 1e-3 &&
                       max(projectionError.x, projectionError.y) < 1e-3;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = discardVec;

                #if !defined(UNITY_INSTANCING_ENABLED) && !defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) && !defined(UNITY_STEREO_INSTANCING_ENABLED)
                uint instanceId = v.instanceID;
                #else
                uint instanceId = unity_InstanceID;
                #endif

                GlobalSplatSource source;
                if (!InitGlobalSource(instanceId, v.vertex.xyz, source))
                    return o;

                SplatCenter center;
                SplatCorner corner;
                float4 color;
                if (!InitGlobalSplatData(source, center, corner, color))
                    return o;

                // CDRIN optimization: aggressive alpha clipping in vertex stage.
                if (color.a <= 10.0 / 255.0)
                    return o;

                #ifndef SH_BANDS_0
                // center.modelView is already computed by InitGlobalSplatData → InitCenter.
                float3 dir = normalize(mul(center.view, (float3x3)center.modelView));
                float3 sh[SH_COEFFS];
                InitGlobalSH(source, sh);
                color.rgb += EvalSH(sh, dir, (int)_RendererParams[source.rendererId].shDegree);
                #endif

                ClipCorner(corner, color.a);

                o.vertex = center.proj + float4(corner.offset.x, _ProjectionParams.x * corner.offset.y, 0, 0);
                o.color  = color;
                o.uv     = corner.uv;
                o.rendererId = source.rendererId;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                RendererParams p = _RendererParams[i.rendererId];

                float A = dot(i.uv, i.uv);
                if (A > 1.0) discard;

                float2 absUV = abs(i.uv);
                float maxUV = max(absUV.x, absUV.y);

                float falloff = -exp((maxUV - p.scaleFactor * 1.16) * 25 * p.scaleFactor);
                float alpha = (exp(-A * 4.0) + falloff) * i.color.a;

                // CDRIN optimization: aggressive alpha clipping in fragment stage.
                if (alpha < 10.0 / 255.0) discard;
                float3 color = p.gammaToLinear ? GammaToLinearSpace(i.color.rgb) : i.color.rgb;
                if (p.relightEnabled != 0u)
                {
                    bool useCameraOverride =
                        _GsplatCameraRelightOverride > 0.5 &&
                        GsplatCameraOverrideMatchesCurrentCamera();
                    float2 inverseScreen = useCameraOverride
                        ? _GsplatCameraRelightScreen.zw
                        : _GsplatRelightScreen.zw;
                    if (inverseScreen.x <= 0.0 || inverseScreen.y <= 0.0)
                        inverseScreen = 1.0 / _ScreenParams.xy;
                    float2 screenUV = saturate(i.vertex.xy * inverseScreen);
                    float4 lit;
                    if (useCameraOverride)
                        lit = tex2D(_GsplatCameraRelightMap, screenUV);
                    else
                        lit = tex2D(_GsplatRelightMap, screenUV);
                    float3 factor = lerp(
                        p.relightBackground.xxx,
                        lit.rgb * p.relightBrightness,
                        lit.a);
                    color = lerp(color, color * factor, p.relightBlend);
                }
                return float4(color * alpha * p.brightness, alpha);
            }
            ENDHLSL
        }
    }
}
