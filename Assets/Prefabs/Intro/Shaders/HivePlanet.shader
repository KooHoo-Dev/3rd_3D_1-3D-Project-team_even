// 하이브 근원 행성 — "행성 자체가 하나의 생명체" (네러티브 설정). 텍스처 없이 절차적으로 그린다.
//  - 표면: 도메인 워핑 fbm 으로 살갗 같은 어두운 자주·갈색 얼룩
//  - 혈관: 능선(ridged) 노이즈로 뻗는 붉은 발광 줄기. 천천히 맥동하고, 빛이 안 드는 밤 쪽에서 더 또렷하다 (HDR → Bloom)
//  - 빛: 메인 라이트(Directional) 램버트 + 부드러운 명암 경계
//  - 대기: 1패스 안쪽 가장자리 프레넬 + 2패스(SRPDefaultUnlit) 바깥 껍질 — 법선 방향으로 부풀린 구의 뒷면만 가산으로 그려 행성 둘레에 빛 테를 두른다
//  - 큐: Transparent-100 (아래 Tags 주석 참고)
//  - 엔딩 붕괴(EndingPlanetShatter): _CrackAmount 로 금빛 균열이 퍼지고 _VeinFade 로 핏줄 빛이 꺼진다.
//    새 속성은 기본값이면 꺼져 있어 인트로 행성은 그대로
// 메시는 반지름 0.5 구 (IntroSceneBuilder 가 고해상도 구를 만들어 넣는다)
Shader "HybridShooter/Intro/Hive Planet"
{
    Properties
    {
        [Header(Surface)]
        _BaseColor ("Base Color", Color) = (0.07, 0.035, 0.05, 1)
        _FleshColor ("Flesh Color", Color) = (0.32, 0.11, 0.13, 1)
        _SurfaceScale ("Surface Scale", Range(0.5, 12)) = 4

        [Header(Veins)]
        [HDR] _VeinColor ("Vein Color", Color) = (3.2, 0.45, 0.18, 1)
        _VeinScale ("Vein Scale", Range(0.5, 20)) = 6
        _VeinWidth ("Vein Width", Range(0.01, 0.3)) = 0.08
        _PulseSpeed ("Pulse Speed", Range(0, 4)) = 0.9

        [Header(Atmosphere)]
        [HDR] _AtmosphereColor ("Atmosphere Color", Color) = (1.4, 0.32, 0.45, 1)
        _RimPower ("Inner Rim Power", Range(0.5, 8)) = 3
        _AtmosphereThickness ("Shell Thickness (radius ratio)", Range(0.005, 0.2)) = 0.045
        _ShellIntensity ("Shell Intensity", Range(0, 4)) = 1.4

        // 엔딩 붕괴 연출 (EndingPlanetShatter). 기본값이면 위 결과와 같다 → 인트로 행성은 그대로
        [Header(Crack Ending)]
        _CrackAmount ("Crack Amount", Range(0, 1)) = 0
        [HDR] _CrackColor ("Crack Color", Color) = (6, 4.2, 2.2, 1)
        _CrackScale ("Crack Scale", Range(1, 20)) = 5
        _VeinFade ("Vein Fade", Range(0, 1)) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        half4 _FleshColor;
        float _SurfaceScale;
        half4 _VeinColor;
        float _VeinScale;
        half _VeinWidth;
        half _PulseSpeed;
        half4 _AtmosphereColor;
        half _RimPower;
        float _AtmosphereThickness;
        half _ShellIntensity;
        half _CrackAmount;
        half4 _CrackColor;
        float _CrackScale;
        half _VeinFade;
    CBUFFER_END

    uint3 Pcg3d(uint3 v)
    {
        v = v * 1664525u + 1013904223u;
        v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
        v ^= v >> 16u;
        v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
        return v;
    }

    float Hash13(float3 p)
    {
        return float(Pcg3d(asuint(int3(floor(p)))).x) * (1.0 / 4294967295.0);
    }

    float ValueNoise(float3 x)
    {
        float3 i = floor(x);
        float3 f = frac(x);
        f = f * f * (3.0 - 2.0 * f);
        return lerp(
            lerp(lerp(Hash13(i), Hash13(i + float3(1, 0, 0)), f.x),
                 lerp(Hash13(i + float3(0, 1, 0)), Hash13(i + float3(1, 1, 0)), f.x), f.y),
            lerp(lerp(Hash13(i + float3(0, 0, 1)), Hash13(i + float3(1, 0, 1)), f.x),
                 lerp(Hash13(i + float3(0, 1, 1)), Hash13(i + float3(1, 1, 1)), f.x), f.y), f.z);
    }

    float Fbm(float3 p)
    {
        float sum = 0.0;
        float amplitude = 0.5;
        for (int i = 0; i < 5; i++)
        {
            sum += ValueNoise(p) * amplitude;
            p = p * 2.03 + 17.1;
            amplitude *= 0.5;
        }
        return sum / 0.96875;
    }

    float3 Hash33(float3 p)
    {
        return float3(Pcg3d(asuint(int3(floor(p))))) * (1.0 / 4294967295.0);
    }

    // 3D 보로노이 경계까지 거리 (F2 - F1, 0 = 칸 경계) — 엔딩 균열
    float CellEdge(float3 p)
    {
        float3 cell = floor(p);
        float f1 = 8.0, f2 = 8.0;
        for (int z = -1; z <= 1; z++)
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            float3 c = cell + float3(x, y, z);
            float d = length(c + Hash33(c) - p);
            if (d < f1) { f2 = f1; f1 = d; }
            else if (d < f2) f2 = d;
        }
        return f2 - f1;
    }
    ENDHLSL

    SubShader
    {
        // 불투명 큐에 두면 깊이를 안 쓰는 대기 껍질을 나중에 그려지는 스카이박스가 덮는다 → 스카이박스 다음(투명 큐 앞쪽)에 그린다
        Tags { "Queue" = "Transparent-100" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        // 1. 행성 표면
        Pass
        {
            Name "Surface"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 surfaceOS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.surfaceOS = normalize(input.positionOS.xyz); // 행성과 함께 도는 노이즈 좌표
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                Light light = GetMainLight();
                float ndl = dot(n, light.direction);
                half lit = smoothstep(-0.12, 0.65, ndl);

                float3 q = input.surfaceOS * _SurfaceScale;
                float3 warp = float3(Fbm(q + 3.1), Fbm(q + 7.7), Fbm(q + 1.3));
                float h = Fbm(q + warp * 1.8);
                half3 albedo = lerp(_BaseColor.rgb, _FleshColor.rgb, smoothstep(0.3, 0.75, h));
                albedo *= 0.75 + 0.5 * Fbm(q * 4.0 + warp);

                // 혈관: 능선 노이즈가 1 에 가까운 가는 줄기
                float ridge = 1.0 - abs(Fbm(input.surfaceOS * _VeinScale + warp * 2.2) * 2.0 - 1.0);
                float fine = 1.0 - abs(Fbm(input.surfaceOS * _VeinScale * 2.7 + warp * 3.0 + 5.0) * 2.0 - 1.0);
                float vein = smoothstep(1.0 - _VeinWidth, 1.0, ridge) + 0.45 * smoothstep(1.0 - _VeinWidth * 0.6, 1.0, fine);
                float pulse = 0.55 + 0.45 * sin(_Time.y * _PulseSpeed * 6.2832 * 0.5 + h * 14.0);
                half3 emission = _VeinColor.rgb * vein * pulse * lerp(1.0, 0.35, lit) * (1.0 - _VeinFade);

                // 엔딩 균열: 보로노이 경계를 따라 금이 가고, 퍼지는 순서는 큰 얼룩 노이즈가 정한다 (_CrackAmount 0 이면 없음)
                if (_CrackAmount > 0.0)
                {
                    float spread = saturate((_CrackAmount * 1.25 - Fbm(input.surfaceOS * 1.7 + 11.0)) * 4.0);
                    float width = 0.015 + 0.09 * _CrackAmount * _CrackAmount;
                    float edge = CellEdge(input.surfaceOS * _CrackScale + warp * 0.35);
                    float crack = (1.0 - smoothstep(width * 0.4, width, edge)) * spread;
                    float halo = exp(-edge / (width * 3.5)) * spread * 0.35;
                    float flicker = 0.8 + 0.2 * sin(_Time.y * 9.0 + h * 20.0);
                    emission += _CrackColor.rgb * (crack + halo) * flicker * (0.4 + _CrackAmount);
                }

                half3 color = albedo * light.color * lit + albedo * 0.025 + emission;

                float fresnel = pow(1.0 - saturate(dot(n, v)), _RimPower);
                color += _AtmosphereColor.rgb * fresnel * (0.15 + 0.85 * saturate(ndl + 0.35));
                return half4(color, 1);
            }
            ENDHLSL
        }

        // 2. 대기 껍질 (행성 바깥 빛 테)
        Pass
        {
            Name "Atmosphere"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionOS = input.positionOS.xyz + input.normalOS * (0.5 * _AtmosphereThickness);
                output.positionWS = TransformObjectToWorld(positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                // 껍질 뒷면에서 |n·v| 는 행성 가장자리에서 limb, 껍질 바깥 끝에서 0 → limb 로 나눠 0~1
                float limb = sqrt(1.0 - 1.0 / ((1.0 + _AtmosphereThickness) * (1.0 + _AtmosphereThickness)));
                float a = saturate(abs(dot(n, v)) / limb);
                Light light = GetMainLight();
                half sun = saturate(dot(n, light.direction) + 0.35);
                return half4(_AtmosphereColor.rgb * pow(a, 2.5) * (0.12 + sun) * _ShellIntensity, 0);
            }
            ENDHLSL
        }
    }
}
