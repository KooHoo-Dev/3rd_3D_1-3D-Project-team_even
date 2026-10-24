// URP Unlit port of the original Amplify Shader Editor surface shader.
// Requires "Depth Texture" and "Opaque Texture" on the active URP asset.
Shader "QFX/SFX/Area/Area"
{
	Properties
	{
		[HDR]_EmissionColor("Emission Color", Color) = (1,1,1,1)
		[HDR]_DepthColor("Depth Color", Color) = (1,1,1,1)
		_DepthDistance("Depth Distance", Float) = 0
		_DepthFadeExp("Depth Fade Exp", Range( 0.2 , 10)) = 5.316663
		_NoiseMap("Noise Map", 2D) = "white" {}
		_NoiseTilling("Noise Tilling", Float) = 0.5
		_NoisePower("Noise Power", Float) = 0.82
		_NoiseDistortionMap("Noise Distortion Map", 2D) = "white" {}
		_NoiseDistortionTilling("Noise Distortion Tilling", Float) = 0.5
		_NoiseDistortionPower("Noise Distortion Power", Float) = 0
		_Vector0("Noise Scroll", Vector) = (0,0,0,0)
		_DistortionPower("Distortion Power", Range( 0 , 5)) = 0
		_FresnelScale("Fresnel Scale", Range( 0 , 1)) = 0.510905
		_FresnelPower("Fresnel Power", Range( 0 , 5)) = 2
		[Toggle]_MaskAppearInvert("Mask Appear Invert", Float) = 0
		[KeywordEnum(None,X,Y,Z)] _MaskAppearAxis("Mask Appear Axis", Float) = 2
		_MaskAppearProgress("Mask Appear Progress", Float) = -0.03
		_MaskAppearStrength("Mask Appear Strength", Range( 0 , 1)) = 0.5
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
			#pragma shader_feature_local _MASKAPPEARAXIS_NONE _MASKAPPEARAXIS_X _MASKAPPEARAXIS_Y _MASKAPPEARAXIS_Z

			#include "../SFX_URPCommon.hlsl"

			TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);
			TEXTURE2D(_NoiseDistortionMap); SAMPLER(sampler_NoiseDistortionMap);

			CBUFFER_START(UnityPerMaterial)
				float4 _EmissionColor;
				float4 _DepthColor;
				float _DepthDistance;
				float _DepthFadeExp;
				float _NoiseTilling;
				float _NoisePower;
				float _NoiseDistortionTilling;
				float _NoiseDistortionPower;
				float2 _Vector0;
				float _DistortionPower;
				float _FresnelScale;
				float _FresnelPower;
				float _MaskAppearInvert;
				float _MaskAppearProgress;
				float _MaskAppearStrength;
			CBUFFER_END

			float4 TriplanarSampling(float3 positionWS, float3 normalWS, float tilling)
			{
				float3 projNormal = abs(normalWS);
				projNormal /= projNormal.x + projNormal.y + projNormal.z;
				float3 nsign = sign(normalWS);
				float4 xNorm = SAMPLE_TEXTURE2D(_NoiseDistortionMap, sampler_NoiseDistortionMap, tilling * positionWS.zy * float2(nsign.x, 1.0));
				float4 yNorm = SAMPLE_TEXTURE2D(_NoiseDistortionMap, sampler_NoiseDistortionMap, tilling * positionWS.xz * float2(nsign.y, 1.0));
				float4 zNorm = SAMPLE_TEXTURE2D(_NoiseDistortionMap, sampler_NoiseDistortionMap, tilling * positionWS.xy * float2(-nsign.z, 1.0));
				return xNorm * projNormal.x + yNorm * projNormal.y + zNorm * projNormal.z;
			}

			SFXVaryings vert(SFXAttributes v)
			{
				return SFXVertex(v, float3(0, 0, 0));
			}

			half4 frag(SFXVaryings i) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(i);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

				float3 positionWS = i.positionWS;
				float3 normalWS = normalize(i.normalWS);
				float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);

				// Triplanar world space noise
				float4 triplanar = TriplanarSampling(positionWS, normalWS, _NoiseDistortionTilling);
				float4 noiseCoords = float4(positionWS * _NoiseTilling, 0.0) + float4(_Time.y * _Vector0, 0.0, 0.0) + triplanar * _NoiseDistortionPower;
				float3 blendWeights = abs(normalWS);
				blendWeights *= blendWeights;
				float noiseBlend =
					blendWeights.x * SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, noiseCoords.xy).r +
					blendWeights.y * SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, noiseCoords.xz).r +
					blendWeights.z * SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, noiseCoords.yz).r;
				float noise = noiseBlend * _NoisePower;

				float4 screenColor = SFXSampleSceneColor(screenUV + noise);
				float depthFade = SFXDepthFade(i.positionCS, _DepthDistance, _DepthFadeExp);
				float fresnel = SFXFresnel(normalWS, positionWS, _FresnelScale, _FresnelPower);

				float3 emission = (screenColor * _DistortionPower + depthFade * _DepthColor + _EmissionColor * fresnel).rgb;

				// Appear mask
				float3 objectPosition = SFXObjectWorldPosition();
				float3 maskCoords = lerp(objectPosition - positionWS, positionWS - objectPosition, _MaskAppearInvert) / SFXObjectScale()
					+ (_MaskAppearProgress + i.uv0.z);
				#if defined(_MASKAPPEARAXIS_X) || defined(_MASKAPPEARAXIS_NONE)
					float maskAxis = maskCoords.x;
				#elif defined(_MASKAPPEARAXIS_Z)
					float maskAxis = maskCoords.z;
				#else
					float maskAxis = maskCoords.y;
				#endif
				float mask = smoothstep(0.0, _MaskAppearStrength, maskAxis - noise);

				float alpha = saturate(fresnel * mask * i.color.a);

				emission = MixFog(emission, i.fogFactor);
				return half4(emission, alpha);
			}
			ENDHLSL
		}
	}
}
