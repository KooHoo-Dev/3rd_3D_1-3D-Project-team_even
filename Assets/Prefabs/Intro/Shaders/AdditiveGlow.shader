// 인트로 컷의 빛 효과 공용 (가산 합성, HDR → Bloom).
//  - _Radial 1: 판(Quad) 가운데가 밝은 둥근 빛 — 폭발 섬광
//  - _Radial 0: LineRenderer 폭 방향(uv.y)으로 가운데가 밝은 띠 — 엔진 불꽃·불꽃 꼬리·대공 포화
// 색 = _Color × 정점 색(LineRenderer 그라데이션) × 정점 알파
Shader "HybridShooter/Intro/Additive Glow"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (2, 2, 2, 1)
        [Toggle] _Radial ("Radial (Quad)", Float) = 0
        _Softness ("Softness", Range(0.5, 12)) = 4
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Radial;
                half _Softness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float d = _Radial > 0.5 ? length(input.uv - 0.5) * 2.0 : abs(input.uv.y - 0.5) * 2.0;
                half falloff = exp(-d * d * _Softness) * saturate(1.0 - d);
                half3 color = _Color.rgb * input.color.rgb * input.color.a * falloff;
                return half4(color, 0);
            }
            ENDHLSL
        }
    }
}
