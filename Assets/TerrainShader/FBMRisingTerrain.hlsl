#ifndef CAPSTONE_FBM_RISING_INCLUDED
#define CAPSTONE_FBM_RISING_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

CBUFFER_START(FBMRisingSettings)
float _TerrainMode;
float _Frequency, _RiseHeight, _Threshold, _EdgeWidth, _Warp, _Roughness;
float _Speed, _Motion, _Phase, _Posterize, _Terracing;
float _GroundGray, _PeakGray, _SideGray, _ColorSoftness;
CBUFFER_END
float3 _LightDirection;
float3 _LightPosition;

float FHash(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}
float FNoise(float3 p)
{
    float3 i = floor(p), f = frac(p);
    f = f*f*f*(f*(f*6-15)+10);
    return lerp(lerp(lerp(FHash(i), FHash(i+float3(1,0,0)),f.x),
                     lerp(FHash(i+float3(0,1,0)),FHash(i+float3(1,1,0)),f.x),f.y),
                lerp(lerp(FHash(i+float3(0,0,1)),FHash(i+float3(1,0,1)),f.x),
                     lerp(FHash(i+float3(0,1,1)),FHash(i+1),f.x),f.y),f.z);
}
float Fbm(float3 p)
{
    float sum=0, a=0.5, weight=0;
    int octaves = _TerrainMode > 0.5 ? 5 : 7;
    [loop] for(int j=0;j<octaves;j++)
    {
        sum += a*FNoise(p); weight += a;
        p = float3(p.y+p.z, -p.x+p.z, p.x+p.y)*1.43 + 11.7;
        a *= _Roughness;
    }
    return sum/max(weight,0.001);
}
float SmoothRise(float3 positionWS)
{
    float time = _Phase + _Time.y * _Speed * _Motion;
    float3 p;
    if (_TerrainMode > 0.5)
    {
        p = float3(positionWS.xz*_Frequency, time*0.17);
        float2 warp = float2(FNoise(p*0.7+19.3), FNoise(p*0.7+47.1)) - 0.5;
        p.xy += warp*_Warp;
    }
    else
    {
        // Object-anchored volumetric field gives every face detail and follows rotation.
        p = TransformWorldToObject(positionWS)*_Frequency;
        float3 drift = time*float3(0.11,-0.07,0.13);
        float3 warp = float3(FNoise(p*0.7+drift+19.3),
                            FNoise(p*0.7-drift+47.1),
                            FNoise(p*0.7+drift.yzx+73.9))-0.5;
        p += warp*_Warp + drift*0.35;
    }
    // Changing the third noise coordinate makes islands grow/shrink, not just slide.
    return smoothstep(_Threshold, _Threshold+max(_EdgeWidth,0.01), Fbm(p));
}
float Rise(float3 positionWS)
{
    float h = SmoothRise(positionWS);
    float levels=max(2,round(_Posterize));
    float stepped=floor(h*(levels-1)+0.5)/(levels-1);
    return lerp(h,stepped,_Terracing);
}
struct FAttributes
{
    float4 positionOS:POSITION;
    float3 normalOS:NORMAL;
    float2 uv:TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct FVaryings
{
    float4 positionCS:SV_POSITION;
    float3 positionWS:TEXCOORD0;
    float2 uv:TEXCOORD1;
    float rise:TEXCOORD2;
    float3 baseNormalWS:TEXCOORD3;
    float3 basePositionWS:TEXCOORD4;
    UNITY_VERTEX_OUTPUT_STEREO
};
FVaryings FVert(FAttributes input)
{
    FVaryings o=(FVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    if (_TerrainMode > 0.5)
        TerrainInstancing(input.positionOS,input.normalOS,input.uv);
    float3 ws=TransformObjectToWorld(input.positionOS.xyz);
    o.baseNormalWS=TransformObjectToWorldNormal(input.normalOS);
    o.basePositionWS=ws;
    o.rise=Rise(ws);
    float3 displacementDirection = _TerrainMode > 0.5 ? float3(0,1,0) : normalize(o.baseNormalWS);
    ws += displacementDirection*o.rise*_RiseHeight;
    o.positionWS=ws;
    o.uv=input.uv;
#ifdef FBM_SHADOW_PASS
    float3 lightDirectionWS=_LightDirection;
    #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
        lightDirectionWS=normalize(_LightPosition-ws);
    #endif
    o.positionCS=TransformWorldToHClip(ApplyShadowBias(ws,TransformObjectToWorldNormal(input.normalOS),lightDirectionWS));
    #if UNITY_REVERSED_Z
        o.positionCS.z=min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE*o.positionCS.w);
    #else
        o.positionCS.z=max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE*o.positionCS.w);
    #endif
#else
    o.positionCS=TransformWorldToHClip(ws);
#endif
    return o;
}
void FHoles(FVaryings i)
{
#ifdef _ALPHATEST_ON
    ClipHoles(i.uv);
#endif
}
float3 FNormal(FVaryings i)
{
    // Shade the continuous FBM surface instead of revealing each mesh triangle.
    float e=0.01/max(_Frequency,0.001);
    float3 baseNormal=normalize(i.baseNormalWS);
    if (_TerrainMode > 0.5)
    {
        float dx=(SmoothRise(i.basePositionWS+float3(e,0,0))-SmoothRise(i.basePositionWS-float3(e,0,0)))*_RiseHeight/(2*e);
        float dz=(SmoothRise(i.basePositionWS+float3(0,0,e))-SmoothRise(i.basePositionWS-float3(0,0,e)))*_RiseHeight/(2*e);
        return normalize(baseNormal-float3(dx,0,dz)*abs(baseNormal.y));
    }
    // Derive relief in the local surface plane, including vertical and underside faces.
    float3 axis=abs(baseNormal.y)<0.9 ? float3(0,1,0) : float3(1,0,0);
    float3 tangent=normalize(cross(axis,baseNormal));
    float3 bitangent=cross(baseNormal,tangent);
    float dt=(SmoothRise(i.basePositionWS+tangent*e)-SmoothRise(i.basePositionWS-tangent*e))*_RiseHeight/(2*e);
    float db=(SmoothRise(i.basePositionWS+bitangent*e)-SmoothRise(i.basePositionWS-bitangent*e))*_RiseHeight/(2*e);
    return normalize(baseNormal-tangent*dt-bitangent*db);
}
half4 FFrag(FVaryings i):SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    FHoles(i);
    float3 n=FNormal(i);
    // Evaluate color per pixel so the pattern does not inherit the vertex grid.
    float h=SmoothRise(i.basePositionWS);
    float occupied=smoothstep(0.01,0.08,h);
    float alignment = _TerrainMode > 0.5 ? n.y : dot(n,normalize(i.baseNormalWS));
    float slope=saturate((1-alignment)*2.5);
    // Gray floor, pale steep sides, black caps: readable physical relief.
    float gray=lerp(_GroundGray,lerp(_PeakGray,_SideGray,slope),occupied);
    float bands=max(2,round(_Posterize));
    // Soft, pixel-filtered transitions retain tonal bands without hard jaggies.
    float scaled=saturate(gray)*(bands-1);
    float width=max(0.5*_ColorSoftness, max(fwidth(scaled)*0.5,0.0001));
    float tone=0;
    [unroll] for(int band=0;band<7;band++)
    {
        if(band < bands-1)
            tone += smoothstep(band+0.5-width,band+0.5+width,scaled);
    }
    gray=tone/(bands-1);
    return half4(gray.xxx,1);
}
half4 FDepth(FVaryings i):SV_Target { FHoles(i); return 0; }
half4 FNormals(FVaryings i):SV_Target
{
    FHoles(i);
    float3 n=FNormal(i);
#ifdef _GBUFFER_NORMALS_OCT
    float2 oct=PackNormalOctQuadEncode(n);
    return half4(PackFloat2To888(saturate(oct*0.5+0.5)),0);
#else
    return half4(n,0);
#endif
}
#endif
