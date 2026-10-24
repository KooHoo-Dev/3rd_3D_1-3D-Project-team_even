// URP Unlit port of the original Amplify Shader Editor surface shader.
// Requires "Depth Texture" and "Opaque Texture" on the active URP asset.
Shader "QFX/SFX/EnergyWallDouble"
{
	Properties
	{
		_SphereCenter("Sphere Center", Vector) = (0.5,0.5,0,0)
		_SphereRadius("Sphere Radius", Range( 0 , 1)) = 0.3105874
		_SphereHardness("Sphere Hardness", Range( 0 , 1)) = 0.3796069
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
		[Toggle]_BlendGrid("Blend Grid", Float) = 0
		[Toggle]_NoiseDistortion("NoiseDistortion", Float) = 1
		_OpacityPower("Opacity Power", Range( 0 , 1)) = 0
		_MainTexSpeed("Main Tex Speed", Vector) = (0.5,0,0,0)
		_MainTextureTilling("Main Texture Tilling", Float) = 0.1
		[KeywordEnum(UV,U,V)] _MainTexNoise("Main Tex Noise", Float) = 0
		_MainTex("Main Tex", 2D) = "white" {}
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
			#pragma shader_feature_local _MAINTEXNOISE_UV _MAINTEXNOISE_U _MAINTEXNOISE_V

			#include "../SFX_URPCommon.hlsl"

			TEXTURE2D(_GridTexture); SAMPLER(sampler_GridTexture);
			TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);
			TEXTURE2D(_DistortionMap); SAMPLER(sampler_DistortionMap);
			TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

			CBUFFER_START(UnityPerMaterial)
				float2 _SphereCenter;
				float _SphereRadius;
				float _SphereHardness;
				float _GridOffsetX;
				float _GridOffsetY;
				float _GridTilling;
				float _GridTexturePow;
				float _NoiseDistortionTilling;
				float2 _NoiseDistortionSpeed;
				float _NoiseTilling;
				float _NoisePower;
				float _BlendGrid;
				float _NoiseDistortion;
				float _OpacityPower;
				float2 _MainTexSpeed;
				float _MainTextureTilling;
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
				float4 gridColor = _TintColor * grid;

				#if defined(_MAINTEXNOISE_U)
					float2 mainNoiseUV = uv.xx;
				#elif defined(_MAINTEXNOISE_V)
					float2 mainNoiseUV = uv.yy;
				#else
					float2 mainNoiseUV = uv;
				#endif

				float2 distortion = SAMPLE_TEXTURE2D(_DistortionMap, sampler_DistortionMap, (uv + _Time.y * _NoiseDistortionSpeed) * _NoiseDistortionTilling).rg;
				float2 noise = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, distortion + uv * _NoiseTilling).rg * _NoisePower;

				float sphereMask = saturate((distance(uv, _SphereCenter) - _SphereRadius) / saturate(_SphereHardness));
				float mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, _MainTexSpeed * _Time.y + _MainTextureTilling * (mainNoiseUV + noise)).r;
				float4 wallColor = _TintColor * mainTex * sphereMask + depthColor;

				float4 screenColor = SFXSampleSceneColor(screenUV + noise);
				float appearSmooth = smoothstep(0.5, 1.0, 1.0 - saturate(sphereMask));
				float4 appearMask = grid * appearSmooth * _TintColor;

				float3 emission = lerp(depthColor + gridColor,
					lerp(wallColor, wallColor * grid, _BlendGrid) + screenColor + appearMask,
					_NoiseDistortion).rgb;
				float alpha = saturate(_OpacityPower * i.color.a * lerp(gridColor.a, 1.0, _NoiseDistortion));

				emission = MixFog(emission, i.fogFactor);
				return half4(emission, alpha);
			}
			ENDHLSL
		}
	}
}
