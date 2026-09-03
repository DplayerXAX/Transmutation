#ifndef CAPSTONE_VORONOI_TERRAIN_COMMON_INCLUDED
#define CAPSTONE_VORONOI_TERRAIN_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

float2 VoronoiTerrainHash22(float2 p)
{
    float3 p3 = frac(float3(p.x, p.y, p.x) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float VoronoiTerrainF1F2(float2 uv)
{
    float2 cellCoord = floor(uv);
    float2 uvFrac = frac(uv);

    float f1 = 8.0;
    float f2 = 8.0;

    [unroll]
    for (int j = -1; j <= 1; j++)
    {
        [unroll]
        for (int i = -1; i <= 1; i++)
        {
            float2 ringOffset = float2((float)i, (float)j);
            float2 seedUv = VoronoiTerrainHash22(cellCoord + ringOffset);
            float anim = _Time.y * _VoronoiAnimateSpeed;
            seedUv = 0.5 + 0.5 * sin(float2(anim, anim) + 6.2831853 * seedUv);
            float2 delta = ringOffset + seedUv - uvFrac;
            float distSq = dot(delta, delta);

            if (distSq < f1)
            {
                f2 = f1;
                f1 = distSq;
            }
            else if (distSq < f2)
            {
                f2 = distSq;
            }
        }
    }

    return saturate(sqrt(f2) - sqrt(f1));
}

half3 ApplyVoronoiTerrainOverlay(half3 albedo, float3 positionWS, inout half smoothness)
{
    float voronoi = VoronoiTerrainF1F2(positionWS.xz * _VoronoiScale + _VoronoiOffset.xz);
    voronoi = pow(saturate(voronoi * _VoronoiContrast), _VoronoiSharpness);

    half3 cellTint = lerp(_VoronoiColorA.rgb, _VoronoiColorB.rgb, voronoi);
    half3 overlay = albedo * cellTint;
    albedo = lerp(albedo, overlay, _VoronoiStrength);

    smoothness = lerp(smoothness, saturate(smoothness + (voronoi - 0.5) * _VoronoiSmoothnessVariation), _VoronoiStrength);
    return albedo;
}

#endif
