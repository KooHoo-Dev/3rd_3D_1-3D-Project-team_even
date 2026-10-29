// 보급 빛기둥 전용 셰이더. 안개를 적용하지 않아 짙은 안개 너머에서도 보인다.
//  - 가산(Additive) HDR 색 → Bloom 으로 번진다
//  - 실린더 가운데가 밝고 가장자리는 흐린 빛줄기 느낌 (시선과 법선의 각도)
//  - 아래(상자 쪽)가 밝고 위로 갈수록 사라진다 (오브젝트 공간 y: 유니티 기본 실린더 -1 ~ 1)
//  - 멀어질수록 화면에서 너무 가늘어지지 않게 거리에 비례해 폭을 넓힌다 (_MinAngularWidth)
Shader "HybridShooter/Atmosphere/Supply Beacon (No Fog)"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (4, 1.8, 0.4, 1)
        _CoreSharpness ("Core Sharpness", Range(0.5, 8)) = 2
        _TopFade ("Top Fade", Range(0.2, 4)) = 1.2
        _MinAngularWidth ("Min Angular Width (radius / distance)", Range(0, 0.02)) = 0.004
        _PulseSpeed ("Pulse Speed", Float) = 2.5
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags { "Queue" = "Transparent+50" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Back

        Pass
        {
            Name "Beacon"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _CoreSharpness;
                half _TopFade;
                float _MinAngularWidth;
                half _PulseSpeed;
                half _PulseAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float height01 : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                // 같은 높이의 축 위 점을 기준으로 가로 방향만 넓힌다
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 axisWS = TransformObjectToWorld(float3(0, input.positionOS.y, 0));
                float3 radial = positionWS - axisWS;
                float radius = length(radial);
                float minRadius = distance(axisWS, _WorldSpaceCameraPos) * _MinAngularWidth;
                positionWS = axisWS + radial * max(1.0, minRadius / max(radius, 1e-4));

                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(positionWS);
                output.height01 = input.positionOS.y * 0.5 + 0.5;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half facing = abs(dot(normalize(input.normalWS), normalize(input.viewDirWS)));
                half core = pow(saturate(facing), _CoreSharpness);
                half fade = pow(saturate(1.0 - input.height01), _TopFade);
                half pulse = 1.0 + _PulseAmount * sin(_Time.y * _PulseSpeed);
                return half4(_Color.rgb * core * fade * pulse, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
