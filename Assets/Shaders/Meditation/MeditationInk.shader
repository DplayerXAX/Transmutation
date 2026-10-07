Shader "Capstone/Meditation/Ink"
{
    // Paper-and-ink look for the reflection space: the inverse of the dark line world.
    // Pale surfaces, dark contours at grazing angles, hatching on the shaded side.
    // Optional: player drawing in _InkTex (red channel), concentric rings, ragged uv edges.
    Properties
    {
        _PaperColor("Paper Color", Color) = (0.9, 0.89, 0.86, 1)
        _InkColor("Ink Color", Color) = (0.07, 0.07, 0.08, 1)
        [NoScaleOffset] _InkTex("Drawing (R = ink)", 2D) = "black" {}
        _DrawingAmount("Drawing Amount", Range(0, 1)) = 0

        [Header(Shading)]
        _LightDir("Light Direction", Vector) = (0.4, 1, 0.3, 0)
        _ShadeColor("Shade Color", Color) = (0.62, 0.61, 0.6, 1)
        _ShadeLevels("Shade Levels", Range(1, 6)) = 3
        _RimPower("Contour Power", Range(0.5, 8)) = 2.5
        _RimInk("Contour Ink", Range(0, 1)) = 0.85

        [Header(Hatching)]
        _HatchScale("Hatch Lines Per Metre", Float) = 14
        _HatchWidth("Hatch Width (pixels)", Range(0.3, 4)) = 1
        _HatchStrength("Hatch Strength", Range(0, 1)) = 0.7

        [Header(Pattern)]
        _RingCount("Rings (uv distance from centre)", Float) = 0
        _RingKeep("Ring Keep", Range(0, 1)) = 1
        _EdgeRagged("Ragged UV Edge", Range(0, 0.3)) = 0
        _EdgeRound("Round Edge (0 square, 1 circle)", Range(0, 1)) = 0
        _Breath("Breath (metres)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/Shaders/Meditation/MeditationNoise.hlsl"

        TEXTURE2D(_InkTex);
        SAMPLER(sampler_InkTex);

        CBUFFER_START(UnityPerMaterial)
            half4 _PaperColor;
            half4 _InkColor;
            half _DrawingAmount;
            float4 _LightDir;
            half4 _ShadeColor;
            half _ShadeLevels;
            half _RimPower;
            half _RimInk;
            float _HatchScale;
            half _HatchWidth;
            half _HatchStrength;
            float _RingCount;
            half _RingKeep;
            half _EdgeRagged;
            half _EdgeRound;
            float _Breath;
        CBUFFER_END

        float3 InkPosition(float3 positionOS, float3 normalOS)
        {
            float3 positionWS = TransformObjectToWorld(positionOS);
            float3 normalWS = TransformObjectToWorldNormal(normalOS);
            float wave = sin(_Time.y * 0.9 + dot(positionWS, float3(1.3, 2.1, 0.7)));
            return positionWS + normalWS * wave * _Breath;
        }

        // Cuts an irregular, torn outline into the uv square.
        void ClipEdge(float2 uv)
        {
            if (_EdgeRagged <= 0.0) return;
            float2 c = abs(uv - 0.5) * 2.0;
            float edge = lerp(max(c.x, c.y), length(c), _EdgeRound);
            float n = MedFbm3(float3(uv * 7.0, 0.5));
            clip(1.0 - _EdgeRagged * (0.3 + n) - edge);
        }
        ENDHLSL

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float frontMask : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = InkPosition(input.positionOS.xyz, input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.frontMask = input.uv1.x;
                return output;
            }

            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                ClipEdge(input.uv);

                float3 n = normalize(input.normalWS) * (frontFace ? 1.0 : -1.0);
                float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 l = normalize(_LightDir.xyz);

                // Stepped diffuse.
                float diffuse = saturate(dot(n, l) * 0.5 + 0.5);
                float levels = max(_ShadeLevels, 1.0);
                float stepped = floor(diffuse * levels + 0.5) / levels;
                half3 color = lerp(_ShadeColor.rgb, _PaperColor.rgb, stepped);

                // Hatching only on the shaded side.
                float hatchValue = dot(input.positionWS, float3(0.7, 0.5, -0.5)) * _HatchScale;
                float hatch = MedLine(hatchValue, _HatchWidth) * saturate((0.6 - diffuse) * 2.5);
                color = lerp(color, _InkColor.rgb, hatch * _HatchStrength);

                // Concentric rings, broken in places (used by the floor).
                if (_RingCount > 0.0)
                {
                    float r = length(input.uv - 0.5) * 2.0;
                    float wobble = MedNoise3(float3(input.uv * 5.0, 1.7)) * 0.35;
                    float ring = MedLine(r * _RingCount + wobble, 1.2);
                    float keep = step(1.0 - _RingKeep, MedNoise3(float3(input.uv * 9.0, 4.2)));
                    color = lerp(color, _InkColor.rgb, ring * keep * 0.8);
                }

                // Dark contour where the surface turns away.
                float rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
                color = lerp(color, _InkColor.rgb, smoothstep(0.35, 0.75, rim) * _RimInk);

                // Player drawing on top of everything, fading out around the front (uv1.x).
                half ink = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, input.uv).r * _DrawingAmount * input.frontMask;
                color = lerp(color, _InkColor.rgb, ink);

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
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(InkPosition(input.positionOS.xyz, input.normalOS));
                output.uv = input.uv;
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                ClipEdge(input.uv);
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
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(InkPosition(input.positionOS.xyz, input.normalOS));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                ClipEdge(input.uv);
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
