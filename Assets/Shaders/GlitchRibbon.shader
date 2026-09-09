Shader "Capstone/GlitchRibbon"
{
    Properties
    {
        _Speed("Flow Speed (Units Per Second)", Float) = 8
        _BandDensity("Bands Per Unit", Range(0.2, 20)) = 3
        _StripeDensity("Vertical Streaks Per Unit", Range(1, 300)) = 90
        _BlockDensity("Color Blocks Per Unit", Range(0.2, 30)) = 5
        _ColorAmount("Colored Bands", Range(0, 1)) = 0.65
        _StreakStrength("Streak Contrast", Range(0, 1)) = 0.8
        _Spectrum("Full Spectrum Colors", Range(0, 1)) = 0.8
        _Seed("Pattern Seed", Float) = 1
        [Toggle] _UseXY("Use XY Surface (Quad Instead Of Plane)", Float) = 0
        _DarkColor("Dark Color", Color) = (0.008, 0.012, 0.014, 1)
        _LightColor("Light Color", Color) = (0.88, 0.9, 0.86, 1)
        _ColorA("Signal Red", Color) = (0.85, 0.025, 0.09, 1)
        _ColorB("Signal Yellow", Color) = (1, 0.78, 0.015, 1)
        _ColorC("Signal Green", Color) = (0.03, 0.85, 0.28, 1)
        _ColorD("Signal Violet", Color) = (0.45, 0.025, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "DisableBatching"="True" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "GlitchRibbon"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DarkColor, _LightColor, _ColorA, _ColorB, _ColorC, _ColorD;
                float _Speed, _BandDensity, _StripeDensity, _BlockDensity;
                float _ColorAmount, _StreakStrength, _Seed, _UseXY, _Spectrum;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 surface : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Measure along the object's axes, including Transform/parent scale.
                // Coordinates stay attached to the mesh when it rotates or moves.
                float widthScale = length(TransformObjectToWorldDir(float3(1, 0, 0), false));
                float3 lengthAxis = _UseXY > 0.5 ? float3(0, 1, 0) : float3(0, 0, 1);
                float lengthScale = length(TransformObjectToWorldDir(lengthAxis, false));
                output.surface = float2(input.positionOS.x * widthScale,
                    dot(input.positionOS.xyz, lengthAxis) * lengthScale);
                return output;
            }

            #include "GlitchPattern.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}


