Shader "Capstone/Terrain/FBM Rising Posterized"
{
    Properties
    {
        [Toggle] _TerrainMode ("Unity Terrain (off for regular mesh)", Float) = 1
        [Header(Formations)]
        _Frequency ("Pattern Frequency (higher = smaller shapes)", Range(0.001,5)) = 0.14
        _RiseHeight ("Rise Height (world units)", Range(0,30)) = 2
        _Threshold ("Island Threshold", Range(0.1,0.8)) = 0.46
        _EdgeWidth ("Island Edge Softness", Range(0.02,0.4)) = 0.17
        _Warp ("Domain Warp", Range(0,4)) = 1.3
        _Roughness ("FBM Detail", Range(0.1,0.7)) = 0.43
        [Header(Animation)]
        _Speed ("Animation Speed (zero is static)", Range(0,2)) = 0.3
        _Motion ("Animation Amount (zero is static)", Range(0,1)) = 1
        _Phase ("Static Phase", Float) = 0
        [Header(Posterization)]
        _Posterize ("Gray Levels", Range(2,8)) = 4
        _ColorSoftness ("Color Band Softness", Range(0,1)) = 0.65
        _Terracing ("Height Terracing (zero = smooth)", Range(0,1)) = 0
        _GroundGray ("Ground Gray", Range(0,1)) = 0.66
        _PeakGray ("Raised Tops Gray", Range(0,1)) = 0.015
        _SideGray ("Raised Sides Gray", Range(0,1)) = 0.95
        [HideInInspector] _TerrainHolesTexture ("Holes", 2D) = "white" {}
        [HideInInspector] _MainTex ("Base", 2D) = "gray" {}
        [HideInInspector] _BaseColor ("Base Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry-100" "TerrainCompatible"="True" }
        HLSLINCLUDE
        #pragma target 4.5
        #pragma multi_compile_instancing
        #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
        #pragma multi_compile_fragment _ _ALPHATEST_ON
        ENDHLSL
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex FVert
            #pragma fragment FFrag
            #include "FBMRisingTerrain.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex FVert
            #pragma fragment FDepth
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define FBM_SHADOW_PASS
            #include "FBMRisingTerrain.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex FVert
            #pragma fragment FDepth
            #include "FBMRisingTerrain.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex FVert
            #pragma fragment FNormals
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "FBMRisingTerrain.hlsl"
            ENDHLSL
        }
    }
    Dependency "BaseMapShader" = "Capstone/Terrain/FBM Rising Posterized"
    Fallback Off
}
