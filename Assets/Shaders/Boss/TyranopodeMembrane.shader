// Tyranopode 보호막: 보스 발광 주머니와 같은 결의 생체 막.
//  - 세포 경계(3D 보로노이) 모양 혈관이 맥박치며 주황으로 빛나고, 가장자리(프레넬)가 밝다
//  - 표면이 천천히 꿈틀거린다 (꼭짓점 흔들림)
//  - _Hit0~3 (xyz = 맞은 월드 위치, w = 맞은 시각 _Time.y): 맞은 자리에서 퍼지는 물결
//  - _Opacity 0~1: 전체 투명도 (나타나고 사라질 때)
//  - _Damage 0~1: 겹이 깨질수록 혈관이 붉어지고, _Tear 0~1 만큼 막이 찢겨 구멍이 난다 (노이즈 디졸브)
// 값은 TyranopodeShield 가 MaterialPropertyBlock 으로 바꾼다
Shader "HybridShooter/TyranopodeMembrane"
{
    Properties
    {
        _BaseColor ("Membrane", Color) = (0.55, 0.22, 0.12, 0.18)
        [HDR] _VeinColor ("Vein", Color) = (3.0, 1.4, 0.3, 1)
        [HDR] _DamagedVeinColor ("Damaged Vein", Color) = (3.0, 0.35, 0.15, 1)
        [HDR] _RimColor ("Rim", Color) = (2.0, 0.9, 0.35, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _CellScale ("Cell Scale", Float) = 2.2
        _VeinWidth ("Vein Width", Range(0.01, 0.5)) = 0.12
        _PulseSpeed ("Pulse Speed", Float) = 2.2
        _Wobble ("Wobble", Range(0, 0.1)) = 0.02
        _Damage ("Damage", Range(0, 1)) = 0
        _Tear ("Tear", Range(0, 1)) = 0
        _Boost ("Boost", Range(0, 5)) = 0
        _Opacity ("Opacity", Range(0, 1)) = 1
        [HDR] _HitColor ("Hit", Color) = (4.0, 2.2, 0.8, 1)
        _HitSpeed ("Hit Ripple Speed", Float) = 6
        _Hit0 ("Hit0", Vector) = (0, 0, 0, -100)
        _Hit1 ("Hit1", Vector) = (0, 0, 0, -100)
        _Hit2 ("Hit2", Vector) = (0, 0, 0, -100)
        _Hit3 ("Hit3", Vector) = (0, 0, 0, -100)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Membrane"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _VeinColor;
                half4 _DamagedVeinColor;
                half4 _RimColor;
                half _RimPower;
                float _CellScale;
                half _VeinWidth;
                half _PulseSpeed;
                half _Wobble;
                half _Damage;
                half _Tear;
                half _Boost;
                half _Opacity;
                half4 _HitColor;
                half _HitSpeed;
                float4 _Hit0;
                float4 _Hit1;
                float4 _Hit2;
                float4 _Hit3;
            CBUFFER_END

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
                float3 positionOS : TEXCOORD2;
            };

            float3 Hash3(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)), dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453);
            }

            // 3D 보로노이: x = 가장 가까운 점까지, y = 두 번째 점까지 (y - x 가 작으면 세포 경계 = 혈관)
            float2 Voronoi(float3 p)
            {
                float3 cell = floor(p);
                float3 local = frac(p);
                float first = 8.0;
                float second = 8.0;
                [unroll] for (int z = -1; z <= 1; z++)
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float3 offset = float3(x, y, z);
                    float3 seed = Hash3(cell + offset);
                    // 세포 중심이 천천히 움직여 살아 있는 느낌
                    seed = 0.5 + 0.4 * sin(_Time.y * 0.6 + 6.2831 * seed);
                    float d = length(offset + seed - local);
                    if (d < first) { second = first; first = d; }
                    else if (d < second) { second = d; }
                }
                return float2(first, second);
            }

            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash3(i).x;
                float n100 = Hash3(i + float3(1, 0, 0)).x;
                float n010 = Hash3(i + float3(0, 1, 0)).x;
                float n110 = Hash3(i + float3(1, 1, 0)).x;
                float n001 = Hash3(i + float3(0, 0, 1)).x;
                float n101 = Hash3(i + float3(1, 0, 1)).x;
                float n011 = Hash3(i + float3(0, 1, 1)).x;
                float n111 = Hash3(i + float3(1, 1, 1)).x;
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            half HitRipple(float4 hit, float3 positionWS)
            {
                float age = _Time.y - hit.w;
                if (age < 0.0 || age > 0.8) return 0;
                float d = distance(positionWS, hit.xyz);
                float front = age * _HitSpeed;
                float gap = (d - front) * 2.5;
                float ring = exp(-gap * gap);
                float spot = exp(-d * 1.5) * saturate(1.0 - age * 4.0);
                return (ring + spot) * (1.0 - age / 0.8);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 p = input.positionOS.xyz;
                float wobble = sin(_Time.y * 1.7 + p.x * 6.0 + p.y * 4.0) * cos(_Time.y * 1.3 + p.z * 5.0);
                p += input.normalOS * wobble * _Wobble;
                VertexPositionInputs positions = GetVertexPositionInputs(p);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = p;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = pow(1.0 - saturate(dot(normal, viewDir)), _RimPower);

                // 찢김: 노이즈가 _Tear 아래인 곳은 구멍, 구멍 가장자리는 빛나는 살점
                float tearNoise = ValueNoise(input.positionOS * 7.0) * 0.7 + ValueNoise(input.positionOS * 19.0) * 0.3;
                clip(tearNoise - _Tear * 1.05);
                half tearEdge = _Tear > 0.001 ? saturate(1.0 - (tearNoise - _Tear * 1.05) * 14.0) : 0;

                float2 cells = Voronoi(input.positionOS * _CellScale);
                half vein = 1.0 - smoothstep(0.0, _VeinWidth, cells.y - cells.x);
                half pulse = 0.65 + 0.35 * sin(_Time.y * _PulseSpeed - cells.x * 3.0);
                half3 veinColor = lerp(_VeinColor.rgb, _DamagedVeinColor.rgb, _Damage);

                half hits = HitRipple(_Hit0, input.positionWS) + HitRipple(_Hit1, input.positionWS)
                          + HitRipple(_Hit2, input.positionWS) + HitRipple(_Hit3, input.positionWS);

                half3 color = _BaseColor.rgb
                            + veinColor * vein * pulse * (1.0 + _Boost)
                            + _RimColor.rgb * rim * (1.0 + _Boost * 0.5)
                            + _HitColor.rgb * hits
                            + veinColor * tearEdge * 1.5;
                half alpha = saturate(_BaseColor.a + vein * 0.45 * pulse + rim * 0.6 + hits * 0.5 + tearEdge + _Boost * 0.1);
                return half4(color, alpha * _Opacity);
            }
            ENDHLSL
        }
    }
}
