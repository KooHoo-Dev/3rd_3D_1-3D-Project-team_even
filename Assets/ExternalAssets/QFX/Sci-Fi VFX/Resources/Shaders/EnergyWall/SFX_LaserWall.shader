// URP Unlit port of the original Amplify Shader Editor surface shader.
// Requires "Depth Texture" and "Opaque Texture" on the active URP asset.
Shader "QFX/SFX/LaserWall"
{
	Properties
	{
		_GridTexture("Grid Texture", 2D) = "white" {}
		_GridOffsetX("Grid Offset X", Float) = 0
		_GridOffsetY("Grid Offset Y", Float) = 0
		_GridTilling("Grid Tilling", Float) = 0.2
		_GridTexturePow("Grid Texture Pow", Float) = 0
		_NoiseMap("Noise Map", 2D) = "white" {}
		_DistortionMap("Distortion Map", 2D) = "white" {}
		_NoiseDistortionTilling("Noise Distortion Tilling", Range( 0 , 2)) = 0.1793017
		_NoiseDistortionSpeed("Noise Distortion Speed", Vector) = (0,0.2,0,0)
		_NoiseTilling("Noise Tilling", Range( 0 , 2)) = 0.5832521
		_NoisePower("Noise Power", Range( 0 , 2)) = 0.9750929
		_SmokeSpeed2("Smoke Speed 2", Vector) = (-0.2,0.2,0,0)
		_SmokeSpeed1("Smoke Speed 1", Vector) = (0.2,0.2,0,0)
		_SmokeTexture("Smoke Texture", 2D) = "white" {}
		_SmokeTexturePower("Smoke Texture Power", Range( 0 , 10)) = 0
		_OpacityPower("Opacity Power", Range( 0 , 1)) = 0
		[HDR]_DepthColor("Depth Color", Color) = (1,1,1,1)
		_DepthDistance("Depth Distance", Float) = 0
		_DepthFadeExp("Depth Fade Exp", Range( 0.2 , 10)) = 5.316663
		[HDR]_TintColor("Tint Color", Color) = (1,1,1,1)
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

			TEXTURE2D(_GridTexture); SAMPLER(sampler_GridTexture);
			TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);
			TEXTURE2D(_DistortionMap); SAMPLER(sampler_DistortionMap);
			TEXTURE2D(_SmokeTexture); SAMPLER(sampler_SmokeTexture);

			CBUFFER_START(UnityPerMaterial)
				float _GridOffsetX;
				float _GridOffsetY;
				float _GridTilling;
				float _GridTexturePow;
				float _NoiseDistortionTilling;
				float2 _NoiseDistortionSpeed;
				float _NoiseTilling;
				float _NoisePower;
				float2 _SmokeSpeed2;
				float2 _SmokeSpeed1;
				float _SmokeTexturePower;
				float _OpacityPower;
				float4 _DepthColor;
				float _DepthDistance;
				float _DepthFadeExp;
				float4 _TintColor;
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
				float3 normalWS = normalize(i.normalWS);
				float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);

				float4 depthColor = SFXDepthFade(i.positionCS, _DepthDistance, _DepthFadeExp) * _DepthColor;
				float4 grid = SFXGridTriplanar(TEXTURE2D_ARGS(_GridTexture, sampler_GridTexture), i.positionWS, normalWS,
					_GridTilling, float2(_GridOffsetX, _GridOffsetY), _GridTexturePow);

				float smoke =
					pow(SAMPLE_TEXTURE2D(_SmokeTexture, sampler_SmokeTexture, _Time.y * _SmokeSpeed1 + uv).r, _SmokeTexturePower) +
					pow(SAMPLE_TEXTURE2D(_SmokeTexture, sampler_SmokeTexture, _Time.y * _SmokeSpeed2 + uv).r, _SmokeTexturePower);
				float4 wallColor = grid * _TintColor * smoke;

				float2 distortion = SAMPLE_TEXTURE2D(_DistortionMap, sampler_DistortionMap, (uv + _Time.y * _NoiseDistortionSpeed) * _NoiseDistortionTilling).rg;
				float2 noise = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, distortion + uv * _NoiseTilling).rg * _NoisePower;
				float4 screenColor = SFXSampleSceneColor(screenUV + noise);

				float3 emission = (depthColor + wallColor + screenColor).rgb;
				float alpha = saturate(_OpacityPower * i.color.a * wallColor.a);

				emission = MixFog(emission, i.fogFactor);
				return half4(emission, alpha);
			}
			ENDHLSL
		}
	}
}
