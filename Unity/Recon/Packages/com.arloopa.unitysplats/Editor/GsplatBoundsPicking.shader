// Copyright (c) 2026 ARLOOPA
// SPDX-License-Identifier: MIT

Shader "Hidden/Gsplat/EditorBoundsPicking"
{
    SubShader
    {
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _SelectionId;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                return _SelectionId;
            }
            ENDHLSL
        }
    }
}
