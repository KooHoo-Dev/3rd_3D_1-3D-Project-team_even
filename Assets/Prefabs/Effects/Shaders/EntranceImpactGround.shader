// 보스 등장 착지 충격파의 바닥 연출. EntranceShadowTelegraph 와 같은 상자형 데칼 투사 방식.
//  - 착지 순간 중심에서 피해 범위 끝(r = 1)까지 빠르게 퍼지는 뜨거운 링
//  - 링이 지나간 자리는 그을음으로 어두워졌다가 사라진다
//  - 착지 중심에는 금 간 자국이 생기고 용암빛이 돌다가 식는다
//  - _Elapsed (생성 후 경과 초)는 EntranceShockwave 가 MaterialPropertyBlock 으로 넣는다
Shader "HybridShooter/Effects/Entrance Impact Ground"
{
    Properties
    {
        [Header(Wave)]
        [HDR] _WaveColor ("Wave Color", Color) = (2.5, 0.8, 0.15, 1)
        _WaveDuration ("Wave Duration (sec)", Float) = 0.45
        _WaveWidth ("Wave Width", Range(0.005, 0.2)) = 0.02

        [Header(Scorch)]
        _ScorchColor ("Scorch Color", Color) = (0.03, 0.022, 0.018, 1)
        _ScorchStrength ("Scorch Strength", Range(0, 1)) = 0.55
        _ScorchRadius ("Scorch Radius", Range(0.05, 1)) = 0.45

        [Header(Cracks)]
        [HDR] _LavaColor ("Crack Lava Color", Color) = (3, 0.5, 0.05, 1)
        _CrackRadius ("Crack Radius", Range(0.02, 0.6)) = 0.35
        _CrackScale ("Crack Scale", Float) = 9
        _CrackWidth ("Crack Width", Range(0.005, 0.2)) = 0.06
        _CoolTime ("Lava Cool Time (sec)", Float) = 1.2

        [Header(Lifetime)]
        _FadeStart ("Fade Start (sec)", Float) = 2.1
        _FadeEnd ("Fade End (sec)", Float) = 2.9

        [HideInInspector] _Elapsed ("Elapsed", Float) = 0
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
                half4 _WaveColor;
                float _WaveDuration;
                half _WaveWidth;
                half4 _ScorchColor;
                half _ScorchStrength;
                half _ScorchRadius;
                half4 _LavaColor;
                half _CrackRadius;
                float _CrackScale;
                half _CrackWidth;
                float _CoolTime;
                float _FadeStart;
                float _FadeEnd;
                float _Elapsed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            float2 ImpactHash22(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            float ImpactNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = ImpactHash22(i).x;
                float b = ImpactHash22(i + float2(1, 0)).x;
                float c = ImpactHash22(i + float2(0, 1)).x;
                float d = ImpactHash22(i + float2(1, 1)).x;
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 보로노이 경계까지의 거리 (F2 - F1)
            float ImpactCellEdge(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float f1 = 8.0;
                float f2 = 8.0;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y);
                    float2 r = g + ImpactHash22(i + g) - f;
                    float d = dot(r, r);
                    if (d < f1) { f2 = f1; f1 = d; }
                    else if (d < f2) { f2 = d; }
                }
                return sqrt(f2) - sqrt(f1);
            }

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

                float3 surfaceNormal = normalize(cross(ddy(positionWS), ddx(positionWS)));
                half fade = smoothstep(0.35, 0.75, abs(surfaceNormal.y)) * (1.0 - smoothstep(0.35, 0.5, boxDistance.y));

                float t = _Elapsed;
                float2 p = positionOS.xz * 2.0;   // -1 ~ 1, 길이 1 = 피해 범위 끝
                float r = length(p);
                half inside = 1.0 - smoothstep(0.98, 1.0, r);
                half lifeFade = 1.0 - smoothstep(_FadeStart, _FadeEnd, t);

                // 퍼지는 링 (끝으로 갈수록 느려진다)
                float waveT = saturate(t / max(_WaveDuration, 1e-3));
                float waveRadius = 1.0 - pow(1.0 - waveT, 3.0);
                float width = _WaveWidth * (1.0 + waveT * 1.5);
                float jitter = (ImpactNoise(p * 6.0) - 0.5) * 0.03;
                half wave = exp(-pow((r + jitter - waveRadius) / width, 2.0)) * pow(1.0 - waveT, 0.6) * inside;

                // 링이 지나간 자리의 그을음
                half passed = 1.0 - smoothstep(waveRadius - 0.05, waveRadius, r);
                half scorch = (1.0 - smoothstep(_ScorchRadius * 0.4, _ScorchRadius, r * (0.85 + ImpactNoise(p * 3.0) * 0.3)))
                            * passed * _ScorchStrength;

                // 중심 금 간 자국 + 식어 가는 용암
                float2 warped = p + (float2(ImpactNoise(p * 4.0), ImpactNoise(p * 4.0 + 7.3)) - 0.5) * 0.08;
                half crackLine = 1.0 - smoothstep(_CrackWidth * 0.4, _CrackWidth, ImpactCellEdge(warped * _CrackScale));
                half crackMask = (1.0 - smoothstep(_CrackRadius * 0.5, _CrackRadius, r * (0.8 + ImpactNoise(p * 5.0) * 0.4)))
                               * smoothstep(0.0, 0.05, t);
                half crack = crackLine * crackMask;
                half heat = exp(-t / max(_CoolTime, 1e-3));

                half shadeAlpha = saturate(scorch + crack * 0.7) * inside;
                half3 color = _ScorchColor.rgb * shadeAlpha
                            + _WaveColor.rgb * wave
                            + _LavaColor.rgb * crack * heat;
                half alpha = saturate(shadeAlpha + wave * 0.5);
                return half4(color, alpha) * (fade * lifeFade);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
