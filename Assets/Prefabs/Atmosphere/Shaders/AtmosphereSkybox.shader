// 외계 행성 밤하늘 스카이박스 (절차적 생성, 텍스처 없음).
//  - 별: 두 겹(밝고 드문 별 + 어둡고 촘촘한 별), 색온도·반짝임. 화면 픽셀보다 작아지지 않게 fwidth 로 크기 보정 (깜빡임 방지)
//  - 성운: 도메인 워핑 fbm 으로 보라·청록 가스 + 어두운 먼지 띠. 하늘을 가로지르는 띠(은하면) 주변에 몰린다
//  - 거대한 달: 메인 라이트(달빛) 방향에 뜬다 → 그림자 방향과 일치. 바다·크레이터·가장자리 어두워짐·후광(HDR → Bloom)
//    가운데 어두운 큰 구덩이와 희미한 붉은 테두리가 '눈동자'처럼 보여 플레이어를 내려다보는 불길한 느낌 (_EyeIntensity 로 조절)
//  - 지평선: 안개 색(_HorizonColor)으로 덮는다. URP 스카이박스는 안개를 받지 않으므로 이 색을 안개 색과 맞춰야 경계선이 안 보인다
Shader "HybridShooter/Atmosphere/Alien Night Sky"
{
    Properties
    {
        [Header(Sky)]
        _ZenithColor ("Zenith Color", Color) = (0.16, 0.17, 0.30, 1)
        _HorizonColor ("Horizon Color (= Fog Color)", Color) = (0.31, 0.36, 0.43, 1)
        _HorizonBlend ("Horizon Haze Height", Range(0.01, 1)) = 0.3
        _HorizonExponent ("Horizon Haze Exponent", Range(0.2, 4)) = 0.7

        [Header(Stars)]
        _StarBrightness ("Star Brightness", Range(0, 10)) = 3
        _StarTwinkle ("Star Twinkle", Range(0, 1)) = 0.6
        _StarTwinkleSpeed ("Star Twinkle Speed", Range(0, 10)) = 2

        [Header(Nebula)]
        [HDR] _NebulaColorA ("Nebula Color A", Color) = (0.55, 0.22, 0.8, 1)
        [HDR] _NebulaColorB ("Nebula Color B", Color) = (0.15, 0.6, 0.7, 1)
        _NebulaIntensity ("Nebula Intensity", Range(0, 4)) = 1
        _NebulaScale ("Nebula Scale", Range(0.5, 6)) = 2.2
        _NebulaBandNormal ("Nebula Band Normal", Vector) = (0.35, 0.45, -0.82, 0)
        _NebulaBandWidth ("Nebula Band Width", Range(0.05, 1.5)) = 0.4

        [Header(Moon)]
        _MoonRadius ("Moon Radius (deg)", Range(1, 30)) = 14
        _MoonColor ("Moon Color", Color) = (0.88, 0.9, 0.95, 1)
        _MoonBrightness ("Moon Brightness", Range(0, 4)) = 1.4
        [HDR] _MoonGlowColor ("Moon Glow Color", Color) = (0.55, 0.68, 1, 1)
        _MoonGlowIntensity ("Moon Glow Intensity", Range(0, 4)) = 0.8
        [HDR] _EyeColor ("Eye Rim Color", Color) = (2, 0.25, 0.1, 1)
        _EyeIntensity ("Eye Intensity", Range(0, 1)) = 0.6
        _EyeSize ("Eye Size", Range(0.05, 0.8)) = 0.3
        _EyeOffset ("Eye Offset (moon local xy)", Vector) = (0.06, 0.05, 0, 0)
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _HorizonColor;
                half _HorizonBlend;
                half _HorizonExponent;
                half _StarBrightness;
                half _StarTwinkle;
                half _StarTwinkleSpeed;
                half4 _NebulaColorA;
                half4 _NebulaColorB;
                half _NebulaIntensity;
                float _NebulaScale;
                float4 _NebulaBandNormal;
                half _NebulaBandWidth;
                float _MoonRadius;
                half4 _MoonColor;
                half _MoonBrightness;
                half4 _MoonGlowColor;
                half _MoonGlowIntensity;
                half4 _EyeColor;
                half _EyeIntensity;
                float _EyeSize;
                float4 _EyeOffset;
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

            // ───────── 노이즈 ─────────

            // 정수 격자점 해시 (PCG3D). 입력을 정수로 바꿔 정수 연산만 한다.
            // 실수 해시(frac 기반)는 컴파일러가 (i + 1) * k 를 i * k + k 로 바꾸는 등 연산 순서만 달라져도 결과가 크게 달라져,
            // 이웃 칸에서 계산한 같은 격자점 값이 어긋나 노이즈에 실선 경계가 생길 수 있다.
            uint3 Pcg3d(uint3 v)
            {
                v = v * 1664525u + 1013904223u;
                v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
                v ^= v >> 16u;
                v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
                return v;
            }

            // p 는 정수 격자점이어야 한다 (내림해서 쓴다)
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
                for (int i = 0; i < 4; i++)
                {
                    sum += ValueNoise(p) * amplitude;
                    p = p * 2.02 + 17.1;
                    amplitude *= 0.5;
                }
                return sum / 0.9375;
            }

            // ───────── 별 ─────────

            // 방향을 density 크기의 격자로 나눠 칸마다 별 하나(확률 1 - threshold)
            half3 StarLayer(float3 dir, float density, float size, float threshold, float seed)
            {
                float3 p = dir * density;
                // 분기 전에 계산 (분기 안의 fwidth 는 정의되지 않음)
                float3 fw = fwidth(p);
                float3 cell = floor(p);
                float3 h = Hash33(cell + seed);
                float present = step(threshold, h.x);

                float3 starDir = normalize(cell + 0.2 + 0.6 * Hash33(cell + seed + 1000.0));
                float dist = length(dir - starDir) * density;

                // 구면으로 투영한 별 위치가 이 칸 안쪽(경계에서 0.25 이상)에 있을 때만 그린다.
                // 칸 밖이나 경계 가까이에 떨어진 별은 이웃 칸에서 계산되지 않아 반쪽만 그려진다
                float3 local = starDir * density - cell;
                float3 margin = min(local, 1.0 - local);
                present *= step(0.25, min(margin.x, min(margin.y, margin.z)));

                // 픽셀 크기보다 작아지면 크기를 키우고 밝기를 낮춘다 (에너지 보존 → 깜빡임 방지)
                float pixel = (fw.x + fw.y + fw.z) * 0.6;
                float radius = max(size, pixel);
                float energy = (size * size) / (radius * radius);
                // 별 중심은 칸 가장자리에서 0.2 이상 안쪽 → 번짐 반경을 0.12 로 묶어 칸 경계에서 잘리지 않게
                float drawRadius = min(radius, 0.12);
                float core = exp(-dist * dist / (drawRadius * drawRadius) * 2.0) * energy;

                float brightness = pow(h.y, 5.0) * 4.0 + 0.15;
                float twinkle = 1.0 - _StarTwinkle * 0.5 * (1.0 + sin(_Time.y * _StarTwinkleSpeed * (0.5 + h.z) + h.x * 60.0));
                half3 tint = lerp(half3(0.65, 0.75, 1.0), half3(1.0, 0.82, 0.62), h.z);
                return tint * (core * brightness * twinkle * present);
            }

            // ───────── 성운 ─────────

            // rgb = 성운 색, a = 먼지(별을 가리는 정도)
            half4 Nebula(float3 dir)
            {
                float3 q = dir * _NebulaScale;
                float3 warp = float3(Fbm(q + 1.7), Fbm(q + 9.2), Fbm(q + 4.1));
                float n = Fbm(q + warp * 1.5);
                float n2 = Fbm(q * 2.0 + warp * 2.0 + 3.3);

                float bandDist = dot(dir, normalize(_NebulaBandNormal.xyz)) / _NebulaBandWidth;
                float band = exp(-bandDist * bandDist);

                float density = saturate(pow(n, 2.2) * 2.2) * (0.3 + 0.7 * band);
                half3 color = lerp(_NebulaColorA.rgb, _NebulaColorB.rgb, smoothstep(0.35, 0.75, n2));
                // 짙은 곳은 밝은 심지처럼
                color *= density * (1.0 + 2.0 * pow(density, 3.0));

                float dust = smoothstep(0.5, 0.75, Fbm(q * 3.0 + warp)) * band;
                color *= 1.0 - dust * 0.6;
                return half4(color * _NebulaIntensity, dust);
            }

            // ───────── 달 ─────────

            // 크레이터: 3D 보로노이 F1 거리 (x) 와 칸 해시 (y)
            float2 Crater(float3 p)
            {
                float3 cell = floor(p);
                float best = 8.0;
                float id = 0.0;
                for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float3 c = cell + float3(x, y, z);
                    float3 o = Hash33(c);
                    float d = length(c + o - p);
                    if (d < best) { best = d; id = o.z; }
                }
                return float2(best, id);
            }

            // p: 달 원판 위 좌표 (-1~1), n: 구 표면 법선 (달 로컬)
            half3 MoonSurface(float2 p, float3 n)
            {
                // 바다(어두운 평원) + 잔무늬
                float maria = Fbm(n * 2.2 + 5.0);
                float albedo = lerp(1.0, 0.55, smoothstep(0.45, 0.7, maria));
                albedo *= 0.85 + 0.3 * Fbm(n * 9.0);

                // 크레이터: 바닥은 어둡고 테두리는 밝게
                float2 crater = Crater(n * 5.0);
                float craterRadius = 0.25 + 0.2 * crater.y;
                albedo -= 0.14 * smoothstep(craterRadius, craterRadius * 0.6, crater.x);
                float rimDist = (crater.x - craterRadius) * 14.0; // 음수 가능 → pow 대신 제곱
                albedo += 0.12 * exp(-rimDist * rimDist);

                // 눈동자: 가운데 큰 어두운 구덩이 + 방사형 줄무늬(홍채) + 희미하게 맥동하는 붉은 테두리
                float2 eyeP = (p - _EyeOffset.xy) / _EyeSize;
                float eyeDist = length(eyeP);
                float pupil = smoothstep(1.0, 0.55, eyeDist);
                // 각도(atan2) 대신 방향 벡터로 노이즈를 뽑는다 → ±180° 에서 무늬가 끊기지 않는다
                float2 eyeDir = eyeP / max(eyeDist, 1e-4);
                float iris = Fbm(float3(eyeDir * 6.0, eyeDist * 1.5)) * smoothstep(1.8, 1.0, eyeDist) * smoothstep(0.6, 1.0, eyeDist);
                albedo *= 1.0 - _EyeIntensity * (pupil * 0.75 + iris * 0.35);

                // 정면에서 약간 비껴 비춰 입체감, 가장자리 어두워짐
                float3 lightDir = normalize(float3(-0.35, 0.25, 0.9));
                float shade = lerp(0.2, 1.0, saturate(dot(n, lightDir)));
                float limb = pow(saturate(n.z), 0.35);

                half3 color = _MoonColor.rgb * saturate(albedo) * shade * limb * _MoonBrightness;

                float pulse = 0.8 + 0.2 * sin(_Time.y * 0.7);
                float eyeRimDist = (eyeDist - 1.0) * 5.0;
                float rim = exp(-eyeRimDist * eyeRimDist);
                color += _EyeColor.rgb * _EyeIntensity * (rim * pulse + iris * 0.25) * limb;
                return color;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.viewDirOS);

                // 기본 하늘 + 성운 + 별
                half3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, saturate(dir.y * 1.5));
                half4 nebula = Nebula(dir);
                sky += nebula.rgb;

                // 칸 경계 필터로 상당수가 걸러지므로 threshold 를 낮게 둔다
                half3 stars = StarLayer(dir, 90.0, 0.06, 0.1, 0.0) + StarLayer(dir, 230.0, 0.08, 0.0, 41.0) * 0.5;
                stars *= 1.0 - nebula.a;

                // 달 원판 좌표계 (달빛 방향 = 메인 라이트 방향)
                float3 moonDir = normalize(_MainLightPosition.xyz);
                float3 right = normalize(cross(float3(0, 1, 0), moonDir) + float3(1e-5, 0, 0));
                float3 up = cross(moonDir, right);
                float facing = dot(dir, moonDir);
                float2 p = float2(dot(dir, right), dot(dir, up)) / sin(radians(_MoonRadius));
                float r = length(p);
                float front = step(0.0, facing);

                // 후광: 가까운 밝은 후광 + 넓고 희미한 빛
                // 달과의 실제 각도(달 반지름 단위)로 계산해야 하늘 전체에서 끊김이 없다.
                // (원판 좌표 r 에 앞쪽 반구 마스크를 곱하면 달에서 90° 떨어진 큰 원에서 후광·별 밝기가 칼같이 끊긴다)
                float outside = max(acos(clamp(facing, -1.0, 1.0)) / radians(_MoonRadius) - 1.0, 0.0);
                half glow = exp(-outside * 5.0) + 0.3 * exp(-outside * 0.7);
                stars *= 1.0 - saturate(glow * 1.5);
                sky += stars * _StarBrightness + _MoonGlowColor.rgb * glow * _MoonGlowIntensity;

                // 지평선 아지랑이: 안개 색으로 덮는다
                half haze = 1.0 - pow(saturate(dir.y / _HorizonBlend), _HorizonExponent);
                half3 color = lerp(sky, _HorizonColor.rgb, haze);

                float edge = fwidth(r) * 1.5;
                float disc = (1.0 - smoothstep(1.0 - edge, 1.0 + edge, r)) * front;
                [branch]
                if (disc > 0.0)
                {
                    float3 n = float3(p, sqrt(saturate(1.0 - r * r)));
                    half3 moon = MoonSurface(p, n);
                    // 아래쪽은 아지랑이에 살짝 잠긴다
                    moon = lerp(moon, _HorizonColor.rgb, haze * 0.6);
                    color = lerp(color, moon, disc);
                }

                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
