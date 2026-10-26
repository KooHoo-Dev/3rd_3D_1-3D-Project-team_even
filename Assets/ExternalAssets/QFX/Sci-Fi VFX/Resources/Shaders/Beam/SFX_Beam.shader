// URP Unlit port of the original Amplify Shader Editor surface shader.
// Requires "Opaque Texture" on the active URP asset.
Shader "QFX/SFX/Beam/Energy Beam"
{
	Properties
	{
		[HDR]_TintColor("Tint Color", Color) = (0,0,0,0)
		_MainTex("Main Tex", 2D) = "white" {}
		_MainSpeed("Main Speed", Vector) = (0,0,0,0)
		_MaskTex("Mask Tex", 2D) = "white" {}
		_NoiseMap("Noise Map", 2D) = "white" {}
		_DistortionMap("Distortion Map", 2D) = "white" {}
		_NoiseDistortionPower("Noise Distortion Power", Range( 0 , 2)) = 0.2179676
		_NoiseDistortionTilling("Noise Distortion Tilling", Range( 0 , 10)) = 1.142912
		_NoiseDistortionSpeed("Noise Distortion Speed", Vector) = (0,0.2,0,0)
		_NoisePower("Noise Power", Range( 0 , 2)) = 1.098096
		_NoiseOffset("Noise Offset", Range( 0 , 1)) = 0
		_NoiseTilling("Noise Tilling", Range( 0 , 10)) = 0.894883
		_NoiseSpeed("Noise Speed", Vector) = (0.2,0,0,0)
		_Float0("Float 0", Float) = 0
	}

	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+0" "IgnoreProjector" = "True" }

		Pass
		{
			Name "Unlit"
			Tags { "LightMode" = "UniversalForward" }

			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite Off
			Cull Off

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			#pragma multi_compile_instancing

			#include "../SFX_URPCommon.hlsl"

			TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
			TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
			TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);
			TEXTURE2D(_DistortionMap); SAMPLER(sampler_DistortionMap);

			CBUFFER_START(UnityPerMaterial)
				float4 _TintColor;
				float4 _MainTex_ST;
				float2 _MainSpeed;
				float4 _MaskTex_ST;
				float _NoiseDistortionPower;
				float _NoiseDistortionTilling;
				float2 _NoiseDistortionSpeed;
				float _NoisePower;
				float _NoiseOffset;
				float _NoiseTilling;
				float2 _NoiseSpeed;
				float _Float0;
			CBUFFER_END

			SFXVaryings vert(SFXAttributes v)
			{
				return SFXVertex(v, float3(0, 0, 0));
			}

			half4 frag(SFXVaryings i) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(i);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

				float2 uv = i.uv0.xy;
				float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);

				float distortion = SAMPLE_TEXTURE2D(_DistortionMap, sampler_DistortionMap, (uv + _Time.y * _NoiseDistortionSpeed) * _NoiseDistortionTilling).r * _NoiseDistortionPower;
				float noise = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, distortion + (uv + _Time.y * _NoiseSpeed) * _NoiseTilling).r * _NoisePower - _NoiseOffset;

				float2 mainUV = uv * _MainTex_ST.xy + _MainTex_ST.zw + _Time.y * _MainSpeed;
				float4 mainColor = pow(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, mainUV + noise), _Float0.xxxx);
				float4 screenColor = SFXSampleSceneColor(screenUV + distortion);

				float3 emission = (_TintColor * mainColor + screenColor).rgb;
				float alpha = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv * _MaskTex_ST.xy + _MaskTex_ST.zw).r;

				emission = MixFog(emission, i.fogFactor);
				return half4(emission, alpha);
			}
			ENDHLSL
		}
	}
}
