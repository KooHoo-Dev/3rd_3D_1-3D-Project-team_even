// URP Unlit port of the original Amplify Shader Editor surface shader.
// Requires "Opaque Texture" on the active URP asset.
Shader "QFX/SFX/Shield/ObjectShield"
{
	Properties
	{
		[HDR]_TintColor("Tint Color", Color) = (1,1,1,1)
		[HDR]_CracksColor("Cracks Color", Color) = (0,0,0,0)
		[HDR]_FresnelColor("Fresnel Color", Color) = (1,1,1,1)
		_NoiseMap("Noise Map", 2D) = "white" {}
		_DistortionMap("Distortion Map", 2D) = "white" {}
		_NoiseDistortionPower("Noise Distortion Power", Range( 0 , 2)) = 0.2179676
		_NoiseDistortionTilling("Noise Distortion Tilling", Range( 0 , 10)) = 1.142912
		_NoiseDistortionSpeed("Noise Distortion Speed", Vector) = (0,0.2,0,0)
		_NoisePower("Noise Power", Range( 0 , 2)) = 2
		_NoiseOffset("Noise Offset", Range( 0 , 1)) = 0
		_NoiseTilling("Noise Tilling", Range( 0 , 10)) = 0.894883
		_NoiseSpeed("Noise Speed", Vector) = (0.2,0,0,0)
		_PatternTexture1("Pattern Texture 1", 2D) = "white" {}
		_PatternTexture2("Pattern Texture 2", 2D) = "white" {}
		_PatternTexture3("Pattern Texture 3", 2D) = "white" {}
		_PatternTexture1Speed("Pattern Texture 1 Speed", Vector) = (0.5,0,0,0)
		_PatternTexture2Speed("Pattern Texture 2 Speed", Vector) = (0.5,0,0,0)
		_PatternTexture3Speed("Pattern Texture 3 Speed", Vector) = (0.5,0,0,0)
		_PatternPower1("Pattern Power 1", Range( 0.1 , 20)) = 0.1
		_PatternPower2("Pattern Power 2", Range( 0.1 , 20)) = 0.1
		_PatternPower3("Pattern Power 3", Range( 0.1 , 20)) = 0.1
		_CracksAdjust("Cracks Adjust", Range( 0.01 , 0.99)) = 0
		_CracksPower("Cracks Power", Range( 0 , 1.5)) = 0
		_CracksTexture("Cracks Texture", 2D) = "white" {}
		_FresnelScale("Fresnel Scale", Range( 0 , 1)) = 0.510905
		_FresnelPower("Fresnel Power", Range( 0 , 5)) = 2
		[Toggle]_FresnelOpacity("Fresnel Opacity", Float) = 0
		_MaskAppearProgress("Mask Appear Progress", Float) = 1.002935
		_VertexOffset("Vertex Offset", Float) = 0
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
			Cull Back

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog
			#pragma multi_compile_instancing

			#include "../SFX_URPCommon.hlsl"

			TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);
			TEXTURE2D(_DistortionMap); SAMPLER(sampler_DistortionMap);
			TEXTURE2D(_PatternTexture1); SAMPLER(sampler_PatternTexture1);
			TEXTURE2D(_PatternTexture2); SAMPLER(sampler_PatternTexture2);
			TEXTURE2D(_PatternTexture3); SAMPLER(sampler_PatternTexture3);
			TEXTURE2D(_CracksTexture); SAMPLER(sampler_CracksTexture);

			CBUFFER_START(UnityPerMaterial)
				float4 _TintColor;
				float4 _CracksColor;
				float4 _FresnelColor;
				float _NoiseDistortionPower;
				float _NoiseDistortionTilling;
				float2 _NoiseDistortionSpeed;
				float _NoisePower;
				float _NoiseOffset;
				float _NoiseTilling;
				float2 _NoiseSpeed;
				float4 _PatternTexture1_ST;
				float4 _PatternTexture2_ST;
				float4 _PatternTexture3_ST;
				float2 _PatternTexture1Speed;
				float2 _PatternTexture2Speed;
				float2 _PatternTexture3Speed;
				float _PatternPower1;
				float _PatternPower2;
				float _PatternPower3;
				float _CracksAdjust;
				float _CracksPower;
				float4 _CracksTexture_ST;
				float _FresnelScale;
				float _FresnelPower;
				float _FresnelOpacity;
				float _MaskAppearProgress;
				float _VertexOffset;
			CBUFFER_END

			SFXVaryings vert(SFXAttributes v)
			{
				// The original shader offsets the object space position along the world space normal
				return SFXVertex(v, _VertexOffset * TransformObjectToWorldNormal(v.normalOS));
			}

			half4 frag(SFXVaryings i) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(i);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

				float2 uv = i.uv0.xy;
				float3 positionWS = i.positionWS;
				float3 normalWS = normalize(i.normalWS);
				float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);

				float fresnel = SFXFresnel(normalWS, positionWS, _FresnelScale, _FresnelPower);

				float distortion = SAMPLE_TEXTURE2D(_DistortionMap, sampler_DistortionMap, (uv + _Time.y * _NoiseDistortionSpeed) * _NoiseDistortionTilling).r * _NoiseDistortionPower;
				float noise = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, distortion + (uv + _Time.y * _NoiseSpeed) * _NoiseTilling).r * _NoisePower - _NoiseOffset;

				float pattern1 = pow(SAMPLE_TEXTURE2D(_PatternTexture1, sampler_PatternTexture1, _PatternTexture1Speed * _Time.y + uv * _PatternTexture1_ST.xy + _PatternTexture1_ST.zw).r, _PatternPower1);
				float pattern2 = pow(SAMPLE_TEXTURE2D(_PatternTexture2, sampler_PatternTexture2, _PatternTexture2Speed * _Time.y + uv * _PatternTexture2_ST.xy + _PatternTexture2_ST.zw).r, _PatternPower2);
				float pattern3 = pow(SAMPLE_TEXTURE2D(_PatternTexture3, sampler_PatternTexture3, _PatternTexture3Speed * _Time.y + uv * _PatternTexture3_ST.xy + _PatternTexture3_ST.zw).r, _PatternPower3);
				float pattern = pattern1 * pattern2 + pattern3 * saturate(noise);

				float4 screenColor = SFXSampleSceneColor(screenUV + noise);

				float cracksTex = SAMPLE_TEXTURE2D(_CracksTexture, sampler_CracksTexture, uv * _CracksTexture_ST.xy + _CracksTexture_ST.zw).r;
				float cracks = smoothstep(0.0, _CracksAdjust, pow(cracksTex, _CracksPower));

				// Appear mask rises along the object's Y axis
				float maskAppear = saturate((SFXObjectWorldPosition() - positionWS).y + _MaskAppearProgress - noise);

				float4 color = _FresnelColor * fresnel
					+ (_TintColor * pattern * fresnel + screenColor)
					+ fresnel * (saturate(cracks - 0.2) * _CracksColor);
				float3 emission = (color * maskAppear).rgb;
				float alpha = saturate(maskAppear * lerp(1.0, fresnel, _FresnelOpacity));

				emission = MixFog(emission, i.fogFactor);
				return half4(emission, alpha);
			}
			ENDHLSL
		}
	}
}
