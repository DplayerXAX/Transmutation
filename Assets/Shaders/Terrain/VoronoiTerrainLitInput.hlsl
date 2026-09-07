#ifndef CAPSTONE_VORONOI_TERRAIN_LIT_INPUT_INCLUDED
#define CAPSTONE_VORONOI_TERRAIN_LIT_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitInput.hlsl"

CBUFFER_START(CapstoneVoronoiTerrain)
    float _VoronoiScale;
    float _VoronoiContrast;
    float _VoronoiSharpness;
    float _VoronoiStrength;
    float _VoronoiSmoothnessVariation;
    float _VoronoiAnimateSpeed;
    float4 _VoronoiOffset;
    half4 _VoronoiColorA;
    half4 _VoronoiColorB;
CBUFFER_END

#endif
