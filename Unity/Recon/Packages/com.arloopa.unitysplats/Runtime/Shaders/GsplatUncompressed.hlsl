// Copyright (c) 2026 Yize Wu
// SPDX-License-Identifier: MIT

#ifndef GSPLAT_UNCOMPRESSED_INCLUDED
#define GSPLAT_UNCOMPRESSED_INCLUDED

#include "Gsplat.hlsl"
#ifdef GSPLAT_VERTEX_TEXTURES
Texture2D<float4> _PositionTexture;
Texture2D<float4> _ScaleTexture;
Texture2D<float4> _RotationTexture;
Texture2D<float4> _ColorTexture;
#else
StructuredBuffer<float4> _PositionBuffer;
StructuredBuffer<float4> _ScaleBuffer;
StructuredBuffer<float4> _RotationBuffer;
StructuredBuffer<float4> _ColorBuffer;
#endif

bool InitSplatData(SplatSource source, float4x4 modelView, bool antialiased,
                   out SplatCenter center, out SplatCorner corner, out float4 color)
{
    #ifdef GSPLAT_VERTEX_TEXTURES
    int3 textureCoord = int3(GsplatLinearTextureCoord(source.id, _GsplatTextureWidth), 0);
    float3 modelCenter = _PositionTexture.Load(textureCoord).xyz;
    #else
    float3 modelCenter = _PositionBuffer[source.id].xyz;
    #endif
    if (!InitCenter(modelView, modelCenter, center))
        return false;
    #ifdef GSPLAT_VERTEX_TEXTURES
    float4 quat = _RotationTexture.Load(textureCoord);
    float3 scale = _ScaleTexture.Load(textureCoord).xyz;
    #else
    float4 quat = _RotationBuffer[source.id];
    float3 scale = _ScaleBuffer[source.id].xyz;
    #endif
    SplatCovariance cov = CalcCovariance(quat, scale);
    if (!InitCorner(source, cov, center, antialiased, corner))
        return false;
    #ifdef GSPLAT_VERTEX_TEXTURES
    color = _ColorTexture.Load(textureCoord);
    #else
    color = _ColorBuffer[source.id];
    #endif
    color.rgb = color.rgb * SH_C0 + 0.5;
    if (antialiased)
        color.a *= corner.aaFactor;
    return true;
}

#ifndef SH_BANDS_0
#ifdef GSPLAT_VERTEX_TEXTURES
Texture2D<float4> _SHTexture;
int _SHTextureWidth;
#else
StructuredBuffer<float4> _SHBuffer;
#endif

void InitSH(uint id, out float3 sh[SH_COEFFS])
{
    for (int i = 0; i < SH_COEFFS; i++)
    {
        #ifdef GSPLAT_VERTEX_TEXTURES
        uint index = id * (uint)SH_COEFFS + (uint)i;
        sh[i] = _SHTexture.Load(int3(GsplatLinearTextureCoord(index, _SHTextureWidth), 0)).xyz;
        #else
        sh[i] = _SHBuffer[id * SH_COEFFS + i].xyz;
        #endif
    }
}
#endif

#endif
