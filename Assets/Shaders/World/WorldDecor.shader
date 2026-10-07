Shader "Capstone/World/Decor"
{
    // Lit line shader for the procedural world decorations.
    // Mesh data: uv0.x = 0 at the base .. 1 at the tip, uv0.y = random value per piece.
    // Organic pieces sway and breathe in the vertex stage; stone pieces stay still.
    Properties
    {
        [MainColor] _Color("Lit Base Color", Color) = (0.2, 0.2, 0.21, 1)
        _ShadowColor("Shadow Base Color", Color) = (0.02, 0.02, 0.03, 1)
        _LineColor("Line Color", Color) = (1, 1, 1, 1)
        _LineColor2("Line Sheen Color", Color) = (1, 1, 1, 1)
        _ShadowLineDim("Line Brightness In Shadow", Range(0, 1)) = 0.45
        _LineWidth("Line Width (pixels)", Range(0.3, 4)) = 1.2

        [Header(Lighting)]
        _MainLightAmount("Sun Amount", Range(0, 2)) = 1
        _FakeLightDir("Fill Light Direction", Vector) = (0.3, 1, 0.2, 0)
        _FakeLightAmount("Fill Light Amount", Range(0, 2)) = 0.25
        _ShadeLevels("Shade Levels", Range(1, 6)) = 3
        _RimPower("Rim Power", Range(0.5, 8)) = 3
        _RimStrength("Rim Strength", Range(0, 1)) = 0.5

        [Header(Hatching)]
        _HatchColor("Hatch Color", Color) = (0.45, 0.45, 0.47, 1)
        _HatchStrength("Hatch Strength", Range(0, 1)) = 0.8
        _HatchScale("Hatch Lines Per Metre", Float) = 9
        _HatchWidth("Hatch Width (pixels)", Range(0.3, 4)) = 1

        [Header(Pattern)]
        _RingCount("Rings Along Length", Float) = 0
        _RingSpeed("Ring Travel Speed", Float) = 0
        _RingKeep("Ring Keep (broken rings)", Range(0, 1)) = 1
        _StrataFrequency("Height Strata Per Metre", Float) = 0
        _SheenAmount("Line Sheen Amount", Range(0, 1)) = 0
        _InvertChance("Inverted Pieces (white <-> black)", Range(0, 1)) = 0
        _DotChance("Pieces Shaded With Halftone Dots", Range(0, 1)) = 0
        _DotScale("Halftone Dots Per Metre", Float) = 12
        _DotColor("Halftone Dot Colour", Color) = (0.02, 0.02, 0.03, 1)
        _GridChance("Pieces With A Digital Grid", Range(0, 1)) = 0
        _GridScale("Grid Lines Per Metre", Float) = 8

        [Header(Motion)]
        _SwayAmount("Sway (metres at tip)", Float) = 0
        _SwaySpeed("Sway Speed", Float) = 0.7
        _BreathAmount("Breath (metres)", Float) = 0
        _BreathSpeed("Breath Speed", Float) = 1.2
        _BreathWaves("Breath Waves Along Length", Float) = 1.5

        [Header(Glitch)]
        _GlitchAmount("Glitch Amount", Range(0, 1)) = 0
        _GlitchColorA("Glitch Colour A", Color) = (0.25, 0.8, 0.85, 1)
        _GlitchColorB("Glitch Colour B", Color) = (0.85, 0.25, 0.6, 1)
        _GlitchSplit("Colour Split (fraction of line spacing)", Range(0, 0.5)) = 0.12
        _GlitchTint("Body Tint On Some Pieces", Range(0, 1)) = 0.3
        _GlitchBands("Tear Bands Per Metre", Float) = 3
        _GlitchRate("Tear Changes Per Second", Float) = 4
        _GlitchShift("Tear Shift (metres)", Float) = 0.12

        [Header(Haze)]
        _HazeColor("Haze Color (alpha = strength)", Color) = (0, 0, 0, 0)
        _HazeStart("Haze Start", Float) = 10
        _HazeEnd("Haze End", Float) = 80
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

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half4 _ShadowColor;
            half4 _LineColor;
            half4 _LineColor2;
            half _ShadowLineDim;
            half _LineWidth;
            half _MainLightAmount;
            float4 _FakeLightDir;
            half _FakeLightAmount;
            half _ShadeLevels;
            half _RimPower;
            half _RimStrength;
            half4 _HatchColor;
            half _HatchStrength;
            float _HatchScale;
            half _HatchWidth;
            float _RingCount;
            float _RingSpeed;
            half _RingKeep;
            float _StrataFrequency;
            half _SheenAmount;
            half _InvertChance;
            half _DotChance;
            float _DotScale;
            half4 _DotColor;
            half _GridChance;
            float _GridScale;
            half _GlitchAmount;
            half4 _GlitchColorA;
            half4 _GlitchColorB;
            float _GlitchSplit;
            half _GlitchTint;
            float _GlitchBands;
            float _GlitchRate;
            float _GlitchShift;
            float _SwayAmount;
            float _SwaySpeed;
            float _BreathAmount;
            float _BreathSpeed;
            float _BreathWaves;
            half4 _HazeColor;
            float _HazeStart;
            float _HazeEnd;
        CBUFFER_END

        float Hash11(float p)
        {
            p = frac(p * 0.1031);
            p *= p + 33.33;
            p *= p + p;
            return frac(p);
        }

        // Glitch tears: thin horizontal bands that switch on for a moment, a few at a time,
        // at different moments in different areas. Returns 1 inside an active band.
        half GlitchBand(float3 positionWS, out float bandRandom)
        {
            float2 cell = floor(positionWS.xz / 8.0);
            float cellRandom = Hash11(cell.x * 7.13 + cell.y * 3.37);
            float band = floor(positionWS.y * _GlitchBands);
            float tick = floor(_Time.y * _GlitchRate + cellRandom * 10.0);
            float h = Hash11(band * 1.37 + tick * 7.91 + cellRandom * 113.0);
            bandRandom = Hash11(h * 91.7 + 0.5);
            return step(1.0 - _GlitchAmount * 0.06, h);
        }

        // Living motion: the tip sways, and a slow swelling wave runs from base to tip.
        // Glitch tears shove slices of the piece sideways.
        float3 AnimateDecor(float3 positionWS, float3 normalWS, float2 uv)
        {
            float bandRandom;
            half tear = GlitchBand(positionWS, bandRandom);
            positionWS.xz += tear * (bandRandom - 0.5) * 2.0 * _GlitchShift * float2(1.0, 0.35);

            float t = uv.x;
            float phase = uv.y * 6.2831853 + dot(positionWS.xz, float2(0.11, 0.07));
            float time = _Time.y;

            float2 sway = float2(
                sin(time * _SwaySpeed + phase) + 0.4 * sin(time * _SwaySpeed * 2.3 + phase * 1.7),
                cos(time * _SwaySpeed * 0.77 + phase * 1.3));
            positionWS.xz += sway * _SwayAmount * t * t;

            float wave = sin(time * _BreathSpeed - t * _BreathWaves * 6.2831853 + phase);
            positionWS += normalWS * wave * _BreathAmount * (0.35 + 0.65 * t);
            return positionWS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Shaders/World/LineWorldLighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = AnimateDecor(TransformObjectToWorld(input.positionOS.xyz), normalWS, input.uv);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = normalWS;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // Rings along the length (some broken) plus horizontal strata, at given pattern coordinates.
            half PatternLines(float ringCoord, float strataCoord, float pieceRandom)
            {
                float keep = step(1.0 - _RingKeep, Hash11(floor(ringCoord + 0.5) + pieceRandom * 91.0));
                half rings = LineWorldStripe(ringCoord, _LineWidth) * keep * step(0.001, _RingCount);
                half strata = LineWorldStripe(strataCoord, _LineWidth) * step(0.001, _StrataFrequency);
                return max(rings, strata);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);

                half ao;
                half light = LineWorldLight(input.positionWS, normalWS, screenUV,
                    _MainLightAmount, _FakeLightDir.xyz, _FakeLightAmount, _ShadeLevels, ao);

                half3 color = lerp(_ShadowColor.rgb, _Color.rgb, light) * lerp(0.6h, 1.0h, ao);

                // Each piece shades in one of three ways, so it never reads as the ground's material:
                // hatching, halftone dots, or hatching plus a fine digital grid.
                float pick = frac(input.uv.y * 13.37);
                half useDots = step(1.0 - _DotChance, pick);
                half useGrid = step(pick, _GridChance) * (1.0h - useDots);

                half hatch = LineWorldHatch(input.positionWS, light, _HatchScale, _HatchWidth) * _HatchStrength * (1.0h - useDots);
                color = lerp(color, _HatchColor.rgb, hatch);

                // Halftone: a 3D lattice of dots that grow in the dark, like cheap print.
                float dotDistance = length(frac(input.positionWS * _DotScale) - 0.5);
                float dotRadius = sqrt(saturate(1.0 - light * 0.9)) * 0.48;
                float dotFw = max(fwidth(dotDistance), 1e-4);
                half dots = 1.0h - smoothstep(dotRadius - dotFw, dotRadius + dotFw, dotDistance);
                // Far away the dots blur into their average tone instead of shimmering.
                dots = lerp(dots, saturate(dotRadius * dotRadius * 4.2), saturate(dotFw * 6.0 - 0.5));
                half3 printed = lerp(_Color.rgb * lerp(0.6h, 1.0h, ao), _DotColor.rgb, dots);
                color = lerp(color, printed, useDots);

                // Digital grid in the glitch palette, dimmer in shadow.
                float3 gridCoord = input.positionWS * _GridScale;
                half grid = max(max(LineWorldStripe(gridCoord.x, _LineWidth * 0.8), LineWorldStripe(gridCoord.y, _LineWidth * 0.8)),
                    LineWorldStripe(gridCoord.z, _LineWidth * 0.8));
                half3 gridColor = frac(input.uv.y * 5.1) < 0.5 ? _GlitchColorA.rgb : _GlitchColorB.rgb;
                color = lerp(color, gridColor * lerp(0.45h, 0.9h, light), grid * useGrid * 0.75h);

                // Some pieces carry a faint body tint from the glitch palette.
                half3 pieceTint = frac(input.uv.y * 7.3) < 0.5 ? _GlitchColorA.rgb : _GlitchColorB.rgb;
                half tinted = step(0.6, frac(input.uv.y * 3.7)) * _GlitchTint * _GlitchAmount;
                color = lerp(color, Luminance(color) * pieceTint * 1.8h, tinted);

                // Moving rings get a random offset per piece; still rings stay on the plate seams.
                float ringCoord = input.uv.x * _RingCount - _Time.y * _RingSpeed + input.uv.y * 3.0 * saturate(_RingSpeed * 100.0);
                // Strata like contour lines on stone, slightly wavy.
                float strataCoord = input.positionWS.y * _StrataFrequency
                    + 0.25 * sin(dot(input.positionWS.xz, float2(1.3, 0.9)) + input.uv.y * 6.0);
                half lines = PatternLines(ringCoord, strataCoord, input.uv.y);

                // Colour split: the same lines drawn slightly shifted, once per glitch colour.
                float split = _GlitchSplit * _GlitchAmount;
                half splitA = PatternLines(ringCoord + split, strataCoord + split, input.uv.y) * (1.0h - lines);
                half splitB = PatternLines(ringCoord - split, strataCoord - split, input.uv.y) * (1.0h - lines);

                half rim = pow(1.0h - saturate(dot(normalWS, viewDirWS)), _RimPower) * _RimStrength;
                lines = max(lines, saturate(rim));

                half lineLight = lerp(_ShadowLineDim, 1.0h, light);
                half3 lineColor = LineWorldSheen(_LineColor.rgb, _LineColor2.rgb, normalWS, viewDirWS, input.positionWS, _SheenAmount);
                color = lerp(color, lineColor * lineLight, lines);
                color = lerp(color, _GlitchColorA.rgb * lineLight, splitA * _GlitchAmount);
                color = lerp(color, _GlitchColorB.rgb * lineLight, splitB * _GlitchAmount);

                // Active tear bands: palette colour with coarse scanlines.
                float bandRandom;
                half tear = GlitchBand(input.positionWS, bandRandom);
                half3 tearColor = bandRandom < 0.5 ? _GlitchColorA.rgb : _GlitchColorB.rgb;
                half scan = step(0.5, frac(input.positionCS.y * 0.25));
                color = lerp(color, tearColor * lerp(0.35h, 0.8h, scan), tear * 0.65h);

                // Some pieces are drawn as a negative: white body, black lines.
                color = lerp(color, 1.0h - color, step(input.uv.y, _InvertChance));

                color = LineWorldHaze(color, input.positionWS, _HazeColor, _HazeStart, _HazeEnd);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = AnimateDecor(TransformObjectToWorld(input.positionOS.xyz), normalWS, input.uv);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            float4 DepthVert(Attributes input) : SV_POSITION
            {
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                return TransformWorldToHClip(AnimateDecor(TransformObjectToWorld(input.positionOS.xyz), normalWS, input.uv));
            }

            half DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
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
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(
                    AnimateDecor(TransformObjectToWorld(input.positionOS.xyz), output.normalWS, input.uv));
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
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

    FallBack Off
}
