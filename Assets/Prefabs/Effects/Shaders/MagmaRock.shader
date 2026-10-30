// 보스 마그마 투사체(운석) 전용 셰이더. 검게 식은 현무암 덩어리 틈새로 용암이 비치는 사실적인 느낌.
//  - 버텍스: 구 표면을 큰 덩어리(보로노이)로 나누고, 덩어리는 둥글게 솟고 경계는 깊게 파이게 변형
//            + 저주파 노이즈로 전체 실루엣을 찌그러뜨리고 중간 노이즈로 바위 요철을 더한다
//            법선은 높이 함수를 유한 차분해 다시 계산한다
//  - 픽셀: URP Lit 과 같은 PBR 라이팅(UniversalFragmentPBR) → 다른 에셋과 같은 조명·그림자·안개를 받는다
//          미세 요철은 높이 미분 범프(탄젠트 불필요)로 더한다
//  - 틈새 용암은 HDR Emission 이라 Bloom 으로 번진다. 일부 틈만 활성화(_LavaCoverage)되고 나머지는 어둡게 식은 틈
//  - 틈 주변 바위에 열기(잔불) 번짐, 용암은 천천히 흐르고 살짝 일렁인다
//  - 고해상도 아이코스피어(반지름 0.5)를 전제로 한다. 메시는 Tools/HybridShooter/Rebuild Magma Rock Mesh 로 생성
Shader "HybridShooter/Boss/Magma Rock"
{
    Properties
    {
        [Header(Rock Surface)]
        _RockColor ("Rock Color Dark", Color) = (0.022, 0.02, 0.02, 1)
        _RockColor2 ("Rock Color Light", Color) = (0.07, 0.062, 0.058, 1)
        _SmoothnessMin ("Smoothness Min", Range(0, 1)) = 0.25
        _SmoothnessMax ("Smoothness Max (glassy spots)", Range(0, 1)) = 0.55
        _BumpStrength ("Fine Bump Strength", Range(0, 0.02)) = 0.004

        [Header(Shape)]
        _ChunkScale ("Chunk Scale", Float) = 1.35
        _Warp ("Crack Wobble", Range(0, 1)) = 0.4
        _ChunkHeight ("Chunk Height Variation", Range(0, 0.3)) = 0.07
        _Bevel ("Chunk Roundness", Range(0.01, 0.6)) = 0.25
        _CrackDepth ("Crack Depth", Range(0, 0.2)) = 0.06
        _LumpHeight ("Overall Lumpiness", Range(0, 0.5)) = 0.22
        _DetailHeight ("Rock Detail Height", Range(0, 0.15)) = 0.05

        [Header(Lava)]
        [HDR] _LavaColor ("Lava Color", Color) = (2, 0.28, 0.02, 1)
        [HDR] _LavaHotColor ("Lava Hot Color", Color) = (4, 1.5, 0.2, 1)
        _LavaCoverage ("Lava Coverage", Range(0, 1)) = 0.6
        _CrackWidth ("Crack Width", Range(0.005, 0.2)) = 0.03
        _CrackSoftness ("Crack Softness", Range(0.001, 0.2)) = 0.03
        [HDR] _EmberColor ("Ember Glow Color", Color) = (1.2, 0.18, 0.02, 1)
        _HeatSpread ("Heat Spread", Range(0.01, 0.4)) = 0.12
        _FlowSpeed ("Flow Speed", Float) = 0.4
        _FlickerSpeed ("Flicker Speed", Float) = 2
        _FlickerAmount ("Flicker Amount", Range(0, 1)) = 0.12

        [Header(Motion)]
        _SpinSpeed ("Spin Speed (rad per sec)", Float) = 1.2
        _SpinAxis ("Spin Axis", Vector) = (0.4, 1, 0.25, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _RockColor;
            half4 _RockColor2;
            half _SmoothnessMin;
            half _SmoothnessMax;
            float _BumpStrength;
            float _ChunkScale;
            float _Warp;
            float _ChunkHeight;
            float _Bevel;
            float _CrackDepth;
            float _LumpHeight;
            float _DetailHeight;
            half4 _LavaColor;
            half4 _LavaHotColor;
            half _LavaCoverage;
            half _CrackWidth;
            half _CrackSoftness;
            half4 _EmberColor;
            half _HeatSpread;
            float _FlowSpeed;
            float _FlickerSpeed;
            half _FlickerAmount;
            float _SpinSpeed;
            float4 _SpinAxis;
        CBUFFER_END

        float3 MagmaHash33(float3 p)
        {
            p = float3(dot(p, float3(127.1, 311.7, 74.7)),
                       dot(p, float3(269.5, 183.3, 246.1)),
                       dot(p, float3(113.5, 271.9, 124.6)));
            return frac(sin(p) * 43758.5453123);
        }

        float MagmaHash13(float3 p)
        {
            return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453);
        }

        // x = 가장 가까운 점까지 거리(F1), y = 두 번째(F2), z = 셀 고유 난수
        float3 MagmaVoronoi(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            float f1 = 8.0;
            float f2 = 8.0;
            float id = 0.0;

            [unroll] for (int z = -1; z <= 1; z++)
            [unroll] for (int y = -1; y <= 1; y++)
            [unroll] for (int x = -1; x <= 1; x++)
            {
                float3 g = float3(x, y, z);
                float3 r = g + MagmaHash33(i + g) - f;
                float d = dot(r, r);
                if (d < f1)
                {
                    f2 = f1;
                    f1 = d;
                    id = MagmaHash13(i + g);
                }
                else if (d < f2)
                {
                    f2 = d;
                }
            }
            return float3(sqrt(f1), sqrt(f2), id);
        }

        float MagmaValueNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            float3 u = f * f * (3.0 - 2.0 * f);

            float n000 = MagmaHash13(i);
            float n100 = MagmaHash13(i + float3(1, 0, 0));
            float n010 = MagmaHash13(i + float3(0, 1, 0));
            float n110 = MagmaHash13(i + float3(1, 1, 0));
            float n001 = MagmaHash13(i + float3(0, 0, 1));
            float n101 = MagmaHash13(i + float3(1, 0, 1));
            float n011 = MagmaHash13(i + float3(0, 1, 1));
            float n111 = MagmaHash13(i + float3(1, 1, 1));

            return lerp(lerp(lerp(n000, n100, u.x), lerp(n010, n110, u.x), u.y),
                        lerp(lerp(n001, n101, u.x), lerp(n011, n111, u.x), u.y), u.z);
        }

        float MagmaFbm(float3 p)
        {
            float sum = 0.0;
            float amp = 0.5;
            [unroll] for (int i = 0; i < 4; i++)
            {
                sum += amp * MagmaValueNoise(p);
                p = p * 2.03 + 11.7;
                amp *= 0.5;
            }
            return sum / 0.9375;
        }

        // 보로노이 경계가 직선·원으로 보이지 않도록 방향을 노이즈로 비튼다
        float3 MagmaWarp(float3 dir)
        {
            float3 n = float3(MagmaValueNoise(dir * 3.0 + 1.0),
                              MagmaValueNoise(dir * 3.0 + 7.0),
                              MagmaValueNoise(dir * 3.0 + 13.0));
            return dir + (n - 0.5) * _Warp;
        }

        float3 MagmaChunks(float3 dir)
        {
            return MagmaVoronoi(MagmaWarp(dir) * _ChunkScale + 17.3);
        }

        // 단위 방향 → 반지름 오프셋
        float MagmaHeight(float3 dir)
        {
            float3 v = MagmaChunks(dir);
            float plate = smoothstep(0.0, _Bevel, v.y - v.x);
            float h = (v.z - 0.5) * _ChunkHeight * plate - (1.0 - plate) * _CrackDepth;
            h += (MagmaFbm(dir * 1.3 + 2.0) - 0.5) * _LumpHeight;
            h += (MagmaFbm(dir * 5.0 + 9.0) - 0.5) * _DetailHeight;
            return h;
        }

        float3 MagmaSurfacePoint(float3 dir, float baseRadius)
        {
            return dir * (baseRadius + MagmaHeight(dir));
        }

        float3 MagmaRotate(float3 v, float3 axis, float angle)
        {
            float s, c;
            sincos(angle, s, c);
            return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
        }

        // dirOS: 변형 전 방향(무늬 계산용), positionOS: 변형·회전된 위치, normalOS: 회전된 법선
        void MagmaDeform(float3 inputPositionOS, bool computeNormal, out float3 dirOS, out float3 positionOS, out float3 normalOS)
        {
            float baseRadius = max(length(inputPositionOS), 1e-5);
            dirOS = inputPositionOS / baseRadius;

            positionOS = MagmaSurfacePoint(dirOS, baseRadius);
            normalOS = dirOS;

            if (computeNormal)
            {
                float3 up = abs(dirOS.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 t1 = normalize(cross(dirOS, up));
                float3 t2 = cross(dirOS, t1);
                const float eps = 0.012;
                float3 p1 = MagmaSurfacePoint(normalize(dirOS + t1 * eps), baseRadius);
                float3 p2 = MagmaSurfacePoint(normalize(dirOS + t2 * eps), baseRadius);
                float3 n = normalize(cross(p1 - positionOS, p2 - positionOS));
                normalOS = dot(n, dirOS) < 0.0 ? -n : n;
            }

            float3 axis = normalize(_SpinAxis.xyz + float3(0, 1e-4, 0));
            float angle = _Time.y * _SpinSpeed;
            positionOS = MagmaRotate(positionOS, axis, angle);
            normalOS = MagmaRotate(normalOS, axis, angle);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 dirOS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 dirOS, positionOS, normalOS;
                MagmaDeform(input.positionOS.xyz, true, dirOS, positionOS, normalOS);

                output.positionWS = TransformObjectToWorld(positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.dirOS = dirOS;
                output.normalWS = TransformObjectToWorldNormal(normalOS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.dirOS);
                float3 normalWS = normalize(input.normalWS);
                float t = _Time.y;

                float3 chunk = MagmaChunks(dir);
                float edge = chunk.y - chunk.x;

                // 활성 틈만 넓고 밝게, 나머지는 가늘고 식은 틈
                half active = smoothstep(0.95 - _LavaCoverage, 1.3 - _LavaCoverage, MagmaValueNoise(dir * 2.2 + 31.0));
                half crackWidth = _CrackWidth * (0.35 + active * 1.1);
                half crack = 1.0 - smoothstep(crackWidth, crackWidth + _CrackSoftness, edge);
                half heat = 1.0 - smoothstep(0.0, _HeatSpread, edge);
                half detail = MagmaFbm(dir * 5.0 + 9.0);

                // 미세 요철 범프 (Mikkelsen 높이 미분 방식, 월드 단위)
                float objectScale = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                float fineHeight = (MagmaFbm(dir * 22.0 + 3.0) * 0.7 + MagmaValueNoise(dir * 60.0) * 0.3) * _BumpStrength * objectScale;
                float3 dpdx = ddx(input.positionWS);
                float3 dpdy = ddy(input.positionWS);
                float3 r1 = cross(dpdy, normalWS);
                float3 r2 = cross(normalWS, dpdx);
                float det = dot(dpdx, r1);
                float3 surfaceGrad = sign(det) * (ddx(fineHeight) * r1 + ddy(fineHeight) * r2);
                normalWS = normalize(abs(det) * normalWS - surfaceGrad);

                // 용암
                half flow = MagmaValueNoise(dir * 5.0 + float3(0.0, -t * _FlowSpeed, t * _FlowSpeed * 0.5));
                half flicker = 1.0 + _FlickerAmount * sin(t * _FlickerSpeed + chunk.z * 6.2831853);
                half glow = lerp(0.08, 1.0, active);
                half lavaMask = crack * glow;
                half3 lava = lerp(_LavaColor.rgb, _LavaHotColor.rgb, smoothstep(0.3, 0.9, flow) * crack * active);

                // 잔불: 틈 주변 열기 번짐 + 움푹한 곳의 희미한 불씨
                half ember = heat * (1.0 - crack) * active * smoothstep(0.35, 0.75, MagmaValueNoise(dir * 12.0 + t * 0.1));
                half cavity = smoothstep(0.42, 0.3, detail) * smoothstep(0.55, 0.8, MagmaValueNoise(dir * 14.0));

                SurfaceData surfaceData = (SurfaceData)0;
                half tone = MagmaValueNoise(dir * 9.0 + 4.0) * 0.7 + detail * 0.3;
                surfaceData.albedo = lerp(_RockColor.rgb, _RockColor2.rgb, tone) * (1.0 - lavaMask);
                surfaceData.metallic = 0.0;
                surfaceData.specular = half3(0.0, 0.0, 0.0);
                surfaceData.smoothness = lerp(_SmoothnessMin, _SmoothnessMax, smoothstep(0.5, 0.8, MagmaValueNoise(dir * 7.0 + 2.0))) * (1.0 - lavaMask);
                surfaceData.occlusion = lerp(1.0, 0.25, heat);
                surfaceData.emission = (lava * lavaMask + _EmberColor.rgb * ember * 0.8 + _EmberColor.rgb * float3(0.6, 0.45, 0.5) * cavity * 0.3) * flicker;
                surfaceData.alpha = 1.0;
                surfaceData.normalTS = half3(0.0, 0.0, 1.0);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1.0, 1.0, 1.0, 1.0);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                float3 dirOS, positionOS, normalOS;
                MagmaDeform(input.positionOS.xyz, false, dirOS, positionOS, normalOS);
                float3 positionWS = TransformObjectToWorld(positionOS);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            float4 DepthVert(Attributes input) : SV_POSITION
            {
                float3 dirOS, positionOS, normalOS;
                MagmaDeform(input.positionOS.xyz, false, dirOS, positionOS, normalOS);
                return TransformObjectToHClip(positionOS);
            }

            half DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                float3 dirOS, positionOS, normalOS;
                MagmaDeform(input.positionOS.xyz, true, dirOS, positionOS, normalOS);
                output.positionCS = TransformObjectToHClip(positionOS);
                output.normalWS = TransformObjectToWorldNormal(normalOS);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
