// URP Unlit port of the original Amplify Shader Editor surface shader.
// Requires "Depth Texture" and "Opaque Texture" on the active URP asset.
Shader "QFX/SFX/Shield/EnergyShield"
{
	Properties
	{
		_Cutoff( "Mask Clip Value", Float ) = 0.5
		[HDR]_TintColor("Tint Color", Color) = (0,0,0,0)
		[Toggle]_MaskAppearInvert("Mask Appear Invert", Float) = 0
		[KeywordEnum(None,X,Y,Z)] _MaskAppearAxis("Mask Appear Axis", Float) = 2
		_MaskAppearProgress("Mask Appear Progress", Float) = -0.03
		_MaskAppearStrength("Mask Appear Strength", Range( 0 , 1)) = 0.5
		[HDR]_DisotrionColor("Disotrion Color", Color) = (0,0,0,0)
		_DissolveAmount("Dissolve Amount", Range( 0 , 1)) = 0
		[HideInInspector]_ObjectWorldPosition("Object World Position", Vector) = (0,0,0,0)
		_NoiseMap("Noise Map", 2D) = "white" {}
		_DistortionMap("Distortion Map", 2D) = "white" {}
		_NoiseDistortionPower("Noise Distortion Power", Range( 0 , 2)) = 0.2179676
		_NoiseDistortionTilling("Noise Distortion Tilling", Range( 0 , 10)) = 1.142912
		_NoiseDistortionSpeed("Noise Distortion Speed", Vector) = (0,0.2,0,0)
		_NoisePower("Noise Power", Range( 0 , 2)) = 2
		_NoiseOffset("Noise Offset", Range( 0 , 1)) = 0
		_NoiseTilling("Noise Tilling", Range( 0 , 10)) = 0.894883
		_NoiseSpeed("Noise Speed", Vector) = (0.2,0,0,0)
		[HDR]_DepthColor("Depth Color", Color) = (1,1,1,1)
		_DepthDistance("Depth Distance", Float) = 0
		_DepthFadeExp("Depth Fade Exp", Range( 0.2 , 10)) = 5.316663
		_FresnelScale("Fresnel Scale", Range( 0 , 1)) = 0.510905
		_FresnelPower("Fresnel Power", Range( 0 , 5)) = 2
		_HitTexture("Hit Texture", 2D) = "white" {}
		_HitWaveRampMap("Hit Wave Ramp Map", 2D) = "white" {}
		[HDR]_HitColor("Hit Color", Color) = (0,0,0,0)
		_HitWaveMaxRadius("Hit Wave Max Radius", Float) = 0
		_HitWaveFade("Hit Wave Fade", Range( 0.05 , 1)) = 0.05
		_VertexOffsetAmount("Vertex Offset Amount", Float) = 0
	}

	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "TransparentCutout" "Queue" = "Transparent+0" "IgnoreProjector" = "True" }

		Pass
		{
			Name "Unlit"
			Tags { "LightMode" = "UniversalForward" }

			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite On
			Cull Off

			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			#pragma multi_compile_instancing
			#pragma shader_feature_local _MASKAPPEARAXIS_NONE _MASKAPPEARAXIS_X _MASKAPPEARAXIS_Y _MASKAPPEARAXIS_Z

			#include "../SFX_URPCommon.hlsl"

			#define HIT_MAX_COUNT 10

			TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);
			TEXTURE2D(_DistortionMap); SAMPLER(sampler_DistortionMap);
			TEXTURE2D(_HitTexture); SAMPLER(sampler_HitTexture);
			TEXTURE2D(_HitWaveRampMap); SAMPLER(sampler_HitWaveRampMap);

			// Set at runtime by SFX_EnergyShieldController (Material.SetVectorArray / SetFloatArray)
			float4 _HitPositions[HIT_MAX_COUNT];
			float _HitRadii[HIT_MAX_COUNT];

			CBUFFER_START(UnityPerMaterial)
				float _Cutoff;
				float4 _TintColor;
				float _MaskAppearInvert;
				float _MaskAppearProgress;
				float _MaskAppearStrength;
				float4 _DisotrionColor;
				float _DissolveAmount;
				float3 _ObjectWorldPosition;
				float4 _DistortionMap_ST;
				float _NoiseDistortionPower;
				float _NoiseDistortionTilling;
				float2 _NoiseDistortionSpeed;
				float _NoisePower;
				float _NoiseOffset;
				float _NoiseTilling;
				float2 _NoiseSpeed;
				float4 _DepthColor;
				float _DepthDistance;
				float _DepthFadeExp;
				float _FresnelScale;
				float _FresnelPower;
				float4 _HitTexture_ST;
				float4 _HitColor;
				float _HitWaveMaxRadius;
				float _HitWaveFade;
				float _VertexOffsetAmount;
			CBUFFER_END

			float GetHits(float3 positionWS, float hitTextureR, float2 distortionRG)
			{
				float hitIntensity = 0;

				[unroll]
				for (int k = 0; k < HIT_MAX_COUNT; k++)
				{
					float hitDistance = distance(_HitPositions[k].xyz, positionWS) / _HitWaveMaxRadius;
					float hitFade = smoothstep(0.0, _HitWaveFade, 1.0 - (hitDistance - (_HitRadii[k] * 2.0 - 1.0)));
					float ramp = SAMPLE_TEXTURE2D(_HitWaveRampMap, sampler_HitWaveRampMap, float2(hitFade, 0)).r;
					float wave = ramp - ramp * SAMPLE_TEXTURE2D(_HitWaveRampMap, sampler_HitWaveRampMap, hitFade - distortionRG).r;
					hitIntensity += saturate(1.0 - hitDistance) * saturate(wave - hitTextureR);
				}

				return saturate(hitIntensity);
			}

			SFXVaryings vert(SFXAttributes v)
			{
				return SFXVertex(v, _VertexOffsetAmount * v.normalOS);
			}

			half4 frag(SFXVaryings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(i);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

				float2 uv = i.uv0.xy;
				float3 positionWS = i.positionWS;
				float3 normalWS = normalize(i.normalWS);
				float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);

				float distortion = SAMPLE_TEXTURE2D(_DistortionMap, sampler_DistortionMap, (uv + _Time.y * _NoiseDistortionSpeed) * _NoiseDistortionTilling).r * _NoiseDistortionPower;

				// Appear mask
				float3 maskCoords = lerp(_ObjectWorldPosition - positionWS, positionWS - _ObjectWorldPosition, _MaskAppearInvert) / SFXObjectScale()
					+ (_MaskAppearProgress + i.uv0.z);
				#if defined(_MASKAPPEARAXIS_NONE)
					float3 maskAxis = maskCoords;
				#elif defined(_MASKAPPEARAXIS_X)
					float3 maskAxis = maskCoords.xxx;
				#elif defined(_MASKAPPEARAXIS_Z)
					float3 maskAxis = maskCoords.zzz;
				#else
					float3 maskAxis = maskCoords.yyy;
				#endif
				float3 mask = smoothstep(float3(0, 0, 0), _MaskAppearStrength.xxx, maskAxis - distortion);

				// Hit waves
				float hitTextureR = SAMPLE_TEXTURE2D(_HitTexture, sampler_HitTexture, uv * _HitTexture_ST.xy + _HitTexture_ST.zw).r;
				float2 distortionRG = SAMPLE_TEXTURE2D(_DistortionMap, sampler_DistortionMap, uv * _DistortionMap_ST.xy + _DistortionMap_ST.zw).rg;
				float hits = GetHits(positionWS, hitTextureR, distortionRG);

				// Distorted background + depth intersection
				float noise = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, distortion + (uv + _Time.y * _NoiseSpeed) * _NoiseTilling).r * _NoisePower - _NoiseOffset;
				float4 screenColor = SFXSampleSceneColor(screenUV + noise);
				float4 backColor = SFXDepthFade(i.positionCS, _DepthDistance, _DepthFadeExp) * _DepthColor + _DisotrionColor * screenColor;

				float fresnel = SFXFresnel(normalWS, positionWS, _FresnelScale, _FresnelPower);
				float4 shieldColor = lerp(backColor, _TintColor * fresnel, fresnel);
				float4 faceColor = IS_FRONT_VFACE(face, true, false) ? _HitColor * hits + shieldColor : backColor;

				// Dissolved parts fall back to the tint color
				float4 dissolveMask = step(_DissolveAmount.xxxx, float4(mask, 0.0));
				float3 emission = lerp(_TintColor, faceColor, dissolveMask).rgb;
				float alpha = saturate(shieldColor.a * i.color.a * mask.x);

				clip(mask.x - _Cutoff);

				emission = MixFog(emission, i.fogFactor);
				return half4(emission, alpha);
			}
			ENDHLSL
		}
	}
}
