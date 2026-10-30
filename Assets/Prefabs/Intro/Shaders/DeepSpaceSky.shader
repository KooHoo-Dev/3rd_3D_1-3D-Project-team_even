// 인트로 우주 스카이박스 (절차적 생성, 텍스처 없음). 별·성운 계산은 외계 밤하늘(AtmosphereSkybox)과 같은 방식에서 지평선·달을 빼고 우주용으로 바꿨다.
//  - 별: 세 겹(밝고 드문 별 / 중간 / 어둡고 촘촘한 별), 색온도·반짝임. 화면 픽셀보다 작아지지 않게 fwidth 로 크기 보정 (깜빡임 방지)
//  - 은하수: 하늘을 비스듬히 가로지르는 띠에 어두운 별 먼지와 희미한 빛
//  - 성운: 도메인 워핑 fbm. 차가운 청보라 가스 + 아래쪽(행성 방향)에 하이브를 떠올리게 하는 붉은 기운 (_HiveTint)
Shader "HybridShooter/Intro/Deep Space Sky"
{
    Properties
    {
        _SpaceColor ("Space Color", Color) = (0.004, 0.006, 0.014, 1)

        [Header(Stars)]
        _StarBrightness ("Star Brightness", Range(0, 10)) = 3.2
        _StarTwinkle ("Star Twinkle", Range(0, 1)) = 0.35
        _StarTwinkleSpeed ("Star Twinkle Speed", Range(0, 10)) = 1.6

        [Header(Galaxy Band)]
        _BandNormal ("Band Normal", Vector) = (0.42, 0.8, 0.35, 0)
        _BandWidth ("Band Width", Range(0.05, 1)) = 0.28
        [HDR] _BandColor ("Band Glow Color", Color) = (0.32, 0.36, 0.55, 1)

        [Header(Nebula)]
        [HDR] _NebulaColorA ("Nebula Color A", Color) = (0.22, 0.12, 0.55, 1)
        [HDR] _NebulaColorB ("Nebula Color B", Color) = (0.05, 0.35, 0.55, 1)
        [HDR] _HiveTint ("Hive Tint (below)", Color) = (0.7, 0.08, 0.1, 1)
        _NebulaIntensity ("Nebula Intensity", Range(0, 4)) = 0.9
        _NebulaScale ("Nebula Scale", Range(0.5, 6)) = 1.8
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SpaceColor;
                half _StarBrightness;
                half _StarTwinkle;
                half _StarTwinkleSpeed;
                float4 _BandNormal;
                half _BandWidth;
                half4 _BandColor;
                half4 _NebulaColorA;
                half4 _NebulaColorB;
                half4 _HiveTint;
                half _NebulaIntensity;
                float _NebulaScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 viewDirOS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.viewDirOS = input.positionOS.xyz;
                return output;
            }

            // ───────── 노이즈 (AtmosphereSkybox 와 같은 정수 해시 — 격자 경계에 실선이 생기지 않는다) ─────────

            uint3 Pcg3d(uint3 v)
            {
                v = v * 1664525u + 1013904223u;
                v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
                v ^= v >> 16u;
                v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
                return v;
            }

            float3 Hash33(float3 p)
            {
                return float3(Pcg3d(asuint(int3(floor(p))))) * (1.0 / 4294967295.0);
            }

            float Hash13(float3 p)
            {
                return Hash33(p).x;
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

            // ───────── 별 ─────────

            half3 StarLayer(float3 dir, float density, float size, float threshold, float seed)
            {
                float3 p = dir * density;
                float3 fw = fwidth(p);
                float3 cell = floor(p);
                float3 h = Hash33(cell + seed);
                float present = step(threshold, h.x);

                float3 starDir = normalize(cell + 0.2 + 0.6 * Hash33(cell + seed + 1000.0));
                float dist = length(dir - starDir) * density;

                float3 local = starDir * density - cell;
                float3 margin = min(local, 1.0 - local);
                present *= step(0.25, min(margin.x, min(margin.y, margin.z)));

                float pixel = (fw.x + fw.y + fw.z) * 0.6;
                float radius = max(size, pixel);
                float energy = (size * size) / (radius * radius);
                float drawRadius = min(radius, 0.12);
                float core = exp(-dist * dist / (drawRadius * drawRadius) * 2.0) * energy;

                float brightness = pow(h.y, 5.0) * 4.0 + 0.15;
                float twinkle = 1.0 - _StarTwinkle * 0.5 * (1.0 + sin(_Time.y * _StarTwinkleSpeed * (0.5 + h.z) + h.x * 60.0));
                half3 tint = lerp(half3(0.62, 0.74, 1.0), half3(1.0, 0.84, 0.66), h.z);
                return tint * (core * brightness * twinkle * present);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.viewDirOS);

                // 은하수 띠: 가운데로 갈수록 별이 많고 희미한 빛
                float bandDist = dot(dir, normalize(_BandNormal.xyz)) / _BandWidth;
                float band = exp(-bandDist * bandDist);

                // 성운
                float3 q = dir * _NebulaScale;
                float3 warp = float3(Fbm(q + 1.7), Fbm(q + 9.2), Fbm(q + 4.1));
                float n = Fbm(q + warp * 1.6);
                float n2 = Fbm(q * 2.0 + warp * 2.0 + 3.3);
                float density = saturate(pow(n, 2.4) * 2.4) * (0.35 + 0.65 * band);
                half3 nebula = lerp(_NebulaColorA.rgb, _NebulaColorB.rgb, smoothstep(0.35, 0.75, n2));
                // 아래(행성 쪽)로 갈수록 붉은 기운
                float below = smoothstep(0.1, -0.7, dir.y);
                nebula = lerp(nebula, _HiveTint.rgb, below * smoothstep(0.4, 0.7, n2));
                nebula *= density * (1.0 + 2.0 * pow(density, 3.0)) * _NebulaIntensity;

                float dust = smoothstep(0.5, 0.78, Fbm(q * 3.2 + warp)) * band;

                half3 stars = StarLayer(dir, 90.0, 0.07, 0.86, 0.0) * 1.4
                            + StarLayer(dir, 220.0, 0.06, 0.72, 31.0) * (0.6 + 0.8 * band)
                            + StarLayer(dir, 520.0, 0.05, 0.6, 77.0) * (0.15 + 1.2 * band);
                stars *= _StarBrightness * (1.0 - dust * 0.8);

                half3 glow = _BandColor.rgb * band * (0.25 + 0.75 * Fbm(dir * 6.0 + warp)) * (1.0 - dust * 0.7);

                half3 color = _SpaceColor.rgb + nebula * (1.0 - dust * 0.5) + glow * 0.35 + stars;
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
