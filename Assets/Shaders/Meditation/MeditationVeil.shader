Shader "Capstone/Meditation/Veil"
{
    // Inside-out sphere around the camera. Everything beyond its wobbling surface is
    // hidden behind blank paper, so shrinking it "erases" the world from far to near.
    // _Cover dissolves the paper in with brush-like streaks (0 = invisible, 1 = solid).
    Properties
    {
        _PaperColor("Paper Color", Color) = (0.86, 0.85, 0.82, 1)
        _InkColor("Ink Color", Color) = (0.08, 0.08, 0.09, 1)
        _Cover("Cover", Range(0, 1)) = 1
        _Wobble("Surface Wobble (fraction of radius)", Range(0, 0.4)) = 0.25
        _NoiseFrequency("Wobble Frequency", Float) = 2.2
        _NoiseSpeed("Wobble Speed", Float) = 0.15
        _LineCount("Ink Lines Around", Float) = 46
        _LineWidth("Ink Line Width (pixels)", Range(0.3, 4)) = 1.1
        _LineKeep("Ink Line Amount", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry+50"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        Cull Front

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/Shaders/Meditation/MeditationNoise.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _PaperColor;
            half4 _InkColor;
            half _Cover;
            half _Wobble;
            float _NoiseFrequency;
            float _NoiseSpeed;
            float _LineCount;
            half _LineWidth;
            half _LineKeep;
        CBUFFER_END

        // Pushes each point of the unit sphere in or out with slow noise.
        float3 VeilPosition(float3 positionOS, out float3 dir)
        {
            dir = normalize(positionOS);
            float3 drift = float3(1.0, 0.6, 0.8) * _Time.y * _NoiseSpeed;
            float n = MedFbm3(dir * _NoiseFrequency + drift);
            return dir * (1.0 + _Wobble * (n * 2.0 - 1.0));
        }

        // Streaky dissolve so the paper arrives like brush strokes, not a fade.
        void ClipCover(float3 dir)
        {
            float streak = MedFbm3(float3(dir.x * 2.5, dir.y * 16.0, dir.z * 2.5));
            clip(_Cover * 1.15 - streak - 0.08);
        }
        ENDHLSL

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 dir;
                float3 p = VeilPosition(input.positionOS.xyz, dir);
                output.positionCS = TransformObjectToHClip(p);
                output.dir = dir;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.dir);
                ClipCover(dir);

                // Sparse, broken horizontal ink lines, slightly wavy.
                float wave = MedNoise3(dir * 3.0) * 0.8;
                float lines = MedLine(dir.y * _LineCount + wave, _LineWidth);
                float keep = step(1.0 - _LineKeep, MedNoise3(dir * 7.0 + 3.1));
                half3 color = lerp(_PaperColor.rgb, _InkColor.rgb, lines * keep * 0.7);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                float3 dir;
                output.positionCS = TransformObjectToHClip(VeilPosition(input.positionOS.xyz, dir));
                output.dir = dir;
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                ClipCover(normalize(input.dir));
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                float3 dir;
                output.positionCS = TransformObjectToHClip(VeilPosition(input.positionOS.xyz, dir));
                output.dir = dir;
                // Inside surface faces the centre.
                output.normalWS = -TransformObjectToWorldNormal(dir);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                ClipCover(normalize(input.dir));
                float3 normalWS = normalize(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOct = saturate(octNormalWS * 0.5 + 0.5);
                return half4(PackFloat2To888(remappedOct), 0.0);
            #else
                return half4(normalWS, 0.0);
            #endif
            }
            ENDHLSL
        }
    }
}
