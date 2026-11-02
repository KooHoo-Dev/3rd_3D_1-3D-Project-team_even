// 보스 등장 공격(낙하) 예고 그림자. 큐브 메시를 상자형 데칼로 써서 바닥(지형 경사 포함)에 투사한다.
//  - 카메라 깊이 텍스처로 화면 픽셀의 월드 위치를 복원 → 큐브(오브젝트 공간 -0.5~0.5) 안쪽만 그린다
//    (Decal 렌더러 기능 없이 동작. URP Asset 의 Depth Texture 가 켜져 있어야 한다)
//  - 위를 보는 면에만 칠한다 (플레이어·몬스터 옆면, 벽에는 묻지 않게)
//  - 가운데가 진한 그림자 + 피해 범위 테두리의 붉은 경고선 + 안쪽으로 조여드는 경고 물결
//  - _Darkness, _Urgency, _BlinkPhase 는 EntranceShadowTelegraph 가 MaterialPropertyBlock 으로 넣는다
Shader "HybridShooter/Effects/Entrance Shadow Telegraph"
{
    Properties
    {
        _ShadowColor ("Shadow Color", Color) = (0.02, 0.012, 0.01, 1)
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.85
        [HDR] _WarnColor ("Warning Color", Color) = (3, 0.35, 0.04, 1)
        _RingWidth ("Edge Ring Width", Range(0.002, 0.1)) = 0.02
        _FillTint ("Danger Fill Tint", Range(0, 1)) = 0.02
        _WaveCount ("Inward Wave Count", Range(0, 8)) = 3
        _WaveStrength ("Inward Wave Strength", Range(0, 1)) = 0.25

        [HideInInspector] _Darkness ("Darkness", Range(0, 1)) = 1
        [HideInInspector] _Urgency ("Urgency", Range(0, 1)) = 0
        [HideInInspector] _BlinkPhase ("Blink Phase", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-100" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Front

        Pass
        {
            Name "GroundProjector"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                half _ShadowStrength;
                half4 _WarnColor;
                half _RingWidth;
                half _FillTint;
                half _WaveCount;
                half _WaveStrength;
                half _Darkness;
                half _Urgency;
                float _BlinkPhase;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
            #if UNITY_REVERSED_Z
                float depth = SampleSceneDepth(uv);
            #else
                float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
            #endif
                float3 positionWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 positionOS = TransformWorldToObject(positionWS);
                float3 boxDistance = abs(positionOS);
                clip(0.5 - max(boxDistance.x, max(boxDistance.y, boxDistance.z)));

                // 위를 보는 면만, 상자 위아래 끝은 부드럽게
                float3 surfaceNormal = normalize(cross(ddy(positionWS), ddx(positionWS)));
                half fade = smoothstep(0.35, 0.75, abs(surfaceNormal.y)) * (1.0 - smoothstep(0.35, 0.5, boxDistance.y));

                float r = length(positionOS.xz) * 2.0; // 0 = 중심, 1 = 피해 범위 끝
                half inside = 1.0 - smoothstep(0.985, 1.0, r);

                half shadowAlpha = _ShadowStrength * _Darkness * lerp(0.45, 1.0, 1.0 - smoothstep(0.1, 0.95, r)) * inside;

                half blink = 0.55 + 0.45 * sin(_BlinkPhase);
                half ring = smoothstep(1.0 - _RingWidth - 0.012, 1.0 - _RingWidth, r) * inside * blink;
                half wave = smoothstep(0.88, 1.0, frac(r * _WaveCount + _BlinkPhase * 0.159155)) * _WaveStrength * _Urgency * inside;
                half fill = _FillTint * inside * (0.5 + 0.5 * _Urgency);

                half3 color = _ShadowColor.rgb * shadowAlpha + _WarnColor.rgb * (ring + wave + fill);
                half alpha = saturate(shadowAlpha + (ring + wave) * 0.6);
                return half4(color, alpha) * fade;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
