// URP Unlit port of the original Amplify Shader Editor surface shader.
// (Built-in surface shaders are not supported in URP and render magenta)
Shader "QFX/SFX/Distortion/NoiseDistortionAdjust"
{
	Properties
	{
		[HDR]_TintColor("Tint Color", Color) = (1,0,0,1)
		_MainTex("Main Tex", 2D) = "white" {}
		_NoiseMap("Noise Map", 2D) = "white" {}
		_DistortionMap("Distortion Map", 2D) = "white" {}
		_NoiseDistortionPower("Noise Distortion Power", Range( 0 , 2)) = 0.2179676
		_NoiseDistortionTilling("Noise Distortion Tilling", Range( 0 , 10)) = 1.142912
		_NoiseDistortionSpeed("Noise Distortion Speed", Vector) = (0,0.2,0,0)
		_NoisePower("Noise Power", Range( 0 , 2)) = 2
		_NoiseOffset("Noise Offset", Range( 0 , 1)) = 0
		_NoiseTilling("Noise Tilling", Range( 0 , 10)) = 0.894883
		_NoiseSpeed("Noise Speed", Vector) = (0.2,0,0,0)
		_AdjustPower("Adjust Power", Float) = 0
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
			TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);
			TEXTURE2D(_DistortionMap); SAMPLER(sampler_DistortionMap);

			CBUFFER_START(UnityPerMaterial)
				float4 _TintColor;
				float4 _MainTex_ST;
				float2 _NoiseDistortionSpeed;
				float _NoiseDistortionTilling;
				float _NoiseDistortionPower;
				float2 _NoiseSpeed;
				float _NoiseTilling;
				float _NoisePower;
				float _NoiseOffset;
				float _AdjustPower;
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
				float2 uvMainTex = uv * _MainTex_ST.xy + _MainTex_ST.zw;

				// Scrolling noise, itself distorted by a second scrolling map
				float2 distortionCoords = (_Time.y * _NoiseDistortionSpeed + uv) * _NoiseDistortionTilling;
				float distortion = SAMPLE_TEXTURE2D(_DistortionMap, sampler_DistortionMap, distortionCoords).r * _NoiseDistortionPower;
				float2 noiseCoords = distortion + (_Time.y * _NoiseSpeed + uv) * _NoiseTilling;
				float noise = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, noiseCoords).r * _NoisePower - _NoiseOffset;

				// uv0.z = particle custom data (adjust power over lifetime)
				float mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvMainTex + noise).r;
				float4 color = _TintColor * saturate(pow(max(mainTex, 0.0), _AdjustPower + i.uv0.z));

				float3 emission = color.rgb * i.color.a;
				float alpha = saturate(color.a * i.color.a);

				emission = MixFog(emission, i.fogFactor);
				return half4(emission, alpha);
			}
			ENDHLSL
		}
	}
}
