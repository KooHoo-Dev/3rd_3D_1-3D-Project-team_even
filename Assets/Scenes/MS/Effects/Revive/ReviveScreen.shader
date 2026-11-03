// 부활 연출: 화면 전체에 덮는 글리치 오버레이 (PlayerReviveEffect 의 RawImage).
// 가장자리 청록 번짐 + 주사선 + 디지털 격자 + 가로 글리치 띠 + 픽셀 반짝임 + 위→아래로 훑는 스캔 선.
// _Intensity 0 이면 완전히 투명하다. _CenterClear 만큼 화면 중앙(조준점 근처)을 비운다.
Shader "HybridShooter/UI/ReviveScreen"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        [HDR] _Color ("Color", Color) = (0.3, 1.8, 2.4, 1)
        [HDR] _GlitchColor ("Glitch Color", Color) = (1.8, 0.3, 1.6, 1)
        _Intensity ("Intensity", Range(0, 1)) = 0
        _Glitch ("Glitch", Range(0, 1)) = 0
        _Scan ("Scan Line Position (0 top ~ 1 bottom)", Range(0, 1)) = 0
        _CenterClear ("Center Clear", Range(0, 1)) = 0.7
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Color;
            fixed4 _GlitchColor;
            float _Intensity;
            float _Glitch;
            float _Scan;
            float _CenterClear;

            float Hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            float Hash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
                float tick = floor(_Time.y * 15.0);

                // 가로 글리치 띠: 줄마다 확률적으로 켜지고, 켜진 줄은 uv 를 옆으로 민다
                float row = floor(uv.y * 28.0);
                float band = step(1.0 - _Glitch * 0.45, Hash11(row * 1.37 + tick * 0.71));
                float shift = (Hash11(row * 4.1 + tick) - 0.5) * 0.08 * band;
                float2 suv = float2(uv.x + shift, uv.y);

                // 가장자리 번짐
                float2 c = (uv - 0.5) * float2(aspect, 1.0);
                float dist = length(c) / max(aspect * 0.5, 0.5);
                float edge = smoothstep(0.35, 1.05, dist);

                // 중앙 비우기: 조준점 근처는 덜 덮는다. 글리치 띠·스캔 선은 반만 줄여 다시 쓰기 순간 가림은 유지
                float clear = _CenterClear * (1.0 - smoothstep(0.15, 0.6, dist));

                // 주사선 · 격자
                float scan = 0.5 + 0.5 * sin((suv.y * _ScreenParams.y * 0.5) + _Time.y * 8.0);
                float2 gcell = abs(frac(suv * float2(48.0, 48.0 / aspect)) - 0.5);
                float grid = saturate((max(gcell.x, gcell.y) - 0.47) * 40.0);

                // 픽셀 반짝임
                float sparkle = step(0.992 - _Glitch * 0.01, Hash21(floor(suv * float2(160.0, 90.0)) + tick));

                // 훑는 스캔 선 (_Scan: 0 위 ~ 1 아래)
                float sweep = 1.0 - saturate(abs((1.0 - uv.y) - _Scan) * 40.0);

                float a = (0.10 + scan * 0.05 + sparkle * 0.6) * (1.0 - clear)
                        + edge * 0.65
                        + grid * 0.10 * (0.4 + edge) * (1.0 - clear)
                        + (band * 0.22 + sweep * 0.35) * (1.0 - clear * 0.5);
                a = saturate(a) * _Intensity * i.color.a;

                fixed3 col = _Color.rgb * (0.5 + edge + sweep) + _GlitchColor.rgb * band * 0.6;
                return fixed4(col * i.color.rgb, a);
            }
            ENDCG
        }
    }
    FallBack Off
}
