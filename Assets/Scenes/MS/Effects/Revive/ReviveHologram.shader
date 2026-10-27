// 부활 연출: 몸 렌더러에 잠깐 덧씌우는 홀로그램 재질 (PlayerReviveEffect 가 붙이고 뗀다).
// _BuildHeight(월드 Y) 위쪽만 홀로그램으로 덮는다. 경계는 복셀 단위로 들쭉날쭉하고 밝은 띠가 생긴다.
// _Glitch 가 높을수록 가로 조각이 옆으로 튀고 깜빡인다.
Shader "HybridShooter/Player/ReviveHologram"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.25, 1.6, 2.6, 1)
        [HDR] _EdgeColor ("Edge Color", Color) = (2.0, 4.5, 6.0, 1)
        _Intensity ("Intensity", Range(0, 1)) = 0
        _BuildHeight ("Build Height (World Y)", Float) = 0
        _EdgeWidth ("Edge Width (m)", Float) = 0.08
        _Glitch ("Glitch", Range(0, 1)) = 0
        _ScanDensity ("Scan Lines per m", Float) = 70
        _VoxelSize ("Voxel Size (m)", Float) = 0.045
        _SliceCount ("Glitch Slices per m", Float) = 14
        _SliceOffset ("Glitch Slice Offset (m)", Float) = 0.12
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ReviveHologram"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _EdgeColor;
                half _Intensity;
                float _BuildHeight;
                float _EdgeWidth;
                half _Glitch;
                float _ScanDensity;
                float _VoxelSize;
                float _SliceCount;
                float _SliceOffset;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // 글리치: 가로 조각마다 일정 확률로 옆으로 밀린다 (초당 20번 다시 뽑음)
                float tick = floor(_Time.y * 20.0);
                float slice = floor(positionWS.y * _SliceCount);
                float pick = Hash11(slice * 7.13 + tick * 1.71);
                float hit = step(1.0 - _Glitch * 0.35, pick);
                float dirX = Hash11(slice * 3.7 + tick * 0.37) - 0.5;
                float dirZ = Hash11(slice * 9.1 + tick * 2.03) - 0.5;
                positionWS.xz += float2(dirX, dirZ) * (_SliceOffset * 2.0 * hit * _Glitch);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 p = input.positionWS;
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(p);

                // 경계: 복셀마다 높이를 조금씩 흔들어 블록 단위로 지워지고 쓰이는 느낌
                float3 voxel = floor(p / max(_VoxelSize, 0.001));
                float jitter = (Hash31(voxel) - 0.5) * _EdgeWidth * 1.5;
                float d = p.y - (_BuildHeight + jitter);
                float above = saturate(d / max(_EdgeWidth, 0.001));
                float edge = 1.0 - saturate(abs(d) / max(_EdgeWidth, 0.001));
                edge *= edge;

                float fresnel = pow(1.0 - saturate(dot(n, v)), 2.5);
                float scan = 0.5 + 0.5 * sin((p.y - _Time.y * 0.8) * _ScanDensity);
                scan = smoothstep(0.35, 1.0, scan);

                // 표면에 깔리는 디지털 격자 (복셀 경계)
                float3 cell = abs(frac(p / max(_VoxelSize * 2.0, 0.001)) - 0.5);
                float grid = saturate((max(cell.x, max(cell.y, cell.z)) - 0.44) * 25.0);

                float flicker = lerp(1.0, 0.55 + 0.45 * Hash11(floor(_Time.y * 24.0)), _Glitch);

                half alpha = (0.5 + 0.25 * scan + 0.35 * fresnel + 0.3 * grid) * above * flicker;
                alpha = max(alpha, edge);
                alpha = saturate(alpha) * _Intensity;

                half3 color = _Color.rgb * (0.6 + fresnel + 0.6 * grid + 0.3 * scan) + _EdgeColor.rgb * edge * 2.0;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
