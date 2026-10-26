// Shared helpers for the QFX Sci-Fi VFX shaders ported from Built-in surface shaders to URP Unlit.
// Requires "Depth Texture" and "Opaque Texture" to be enabled on the active URP asset.
#ifndef SFX_URP_COMMON_INCLUDED
#define SFX_URP_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

struct SFXAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 uv0 : TEXCOORD0; // zw carries particle custom data
    float4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct SFXVaryings
{
    float4 positionCS : SV_POSITION;
    float4 uv0 : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
    float fogFactor : TEXCOORD3;
    float4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

SFXVaryings SFXVertex(SFXAttributes v, float3 positionOffsetOS)
{
    SFXVaryings o = (SFXVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

    VertexPositionInputs positionInputs = GetVertexPositionInputs(v.positionOS.xyz + positionOffsetOS);
    o.positionCS = positionInputs.positionCS;
    o.positionWS = positionInputs.positionWS;
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    o.uv0 = v.uv0;
    o.color = v.color;
    o.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
    return o;
}

// Replacement for the Built-in GrabPass texture
float4 SFXSampleSceneColor(float2 screenUV)
{
    return float4(SampleSceneColor(screenUV), 1.0);
}

// Soft intersection with the scene depth (ASE "Depth Fade" node)
float SFXDepthFade(float4 positionCS, float depthDistance, float fadeExp)
{
    float2 screenUV = GetNormalizedScreenSpaceUV(positionCS);
    float sceneEyeDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
    float fragmentEyeDepth = LinearEyeDepth(positionCS.z, _ZBufferParams);
    float distanceDepth = abs((sceneEyeDepth - fragmentEyeDepth) / depthDistance);
    return pow(saturate(1.0 - distanceDepth), fadeExp);
}

float SFXFresnel(float3 normalWS, float3 positionWS, float fresnelScale, float fresnelPower)
{
    float3 viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);
    float NdotV = dot(normalWS, viewDirWS);
    return saturate(fresnelScale * pow(max(1.0 - NdotV, 0.0), fresnelPower));
}

// World space triplanar grid projection used by the energy / laser walls
float4 SFXGridTriplanar(TEXTURE2D_PARAM(gridTexture, gridSampler), float3 positionWS, float3 normalWS,
    float tilling, float2 offset, float texturePow)
{
    float3 weights = abs(normalWS);
    weights *= weights;

    float4 x = pow(SAMPLE_TEXTURE2D(gridTexture, gridSampler, positionWS.yz * tilling + offset), texturePow.xxxx);
    float4 y = pow(SAMPLE_TEXTURE2D(gridTexture, gridSampler, positionWS.zx * tilling + offset), texturePow.xxxx);
    float4 z = pow(SAMPLE_TEXTURE2D(gridTexture, gridSampler, positionWS.xy * tilling + offset), texturePow.xxxx);

    return (weights.x * x + weights.y * y + weights.z * z) / (weights.x + weights.y + weights.z);
}

float3 SFXObjectWorldPosition()
{
    return GetObjectToWorldMatrix()._m03_m13_m23;
}

// Matches the original ASE "object scale" (row lengths of the object to world matrix)
float3 SFXObjectScale()
{
    float4x4 m = GetObjectToWorldMatrix();
    return float3(length(m[0].xyz), length(m[1].xyz), length(m[2].xyz));
}

#endif
