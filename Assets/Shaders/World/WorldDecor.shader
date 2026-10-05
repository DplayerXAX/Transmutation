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

        [Header(Motion)]
        _SwayAmount("Sway (metres at tip)", Float) = 0
        _SwaySpeed("Sway Speed", Float) = 0.7
        _BreathAmount("Breath (metres)", Float) = 0
        _BreathSpeed("Breath Speed", Float) = 1.2
        _BreathWaves("Breath Waves Along Length", Float) = 1.5

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
            float _SwayAmount;
            float _SwaySpeed;
            float _BreathAmount;
            float _BreathSpeed;
            float _BreathWaves;
            half4 _HazeColor;
            float _HazeStart;
            float _HazeEnd;
        CBUFFER_END

        // Living motion: the tip sways, and a slow swelling wave runs from base to tip.
        float3 AnimateDecor(float3 positionWS, float3 normalWS, float2 uv)
        {
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

        float Hash11(float p)
        {
            p = frac(p * 0.1031);
            p *= p + 33.33;
            p *= p + p;
            return frac(p);
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

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);

                half ao;
                half light = LineWorldLight(input.positionWS, normalWS, screenUV,
                    _MainLightAmount, _FakeLightDir.xyz, _FakeLightAmount, _ShadeLevels, ao);

                half3 color = lerp(_ShadowColor.rgb, _Color.rgb, light) * lerp(0.6h, 1.0h, ao);
                half hatch = LineWorldHatch(input.positionWS, light, _HatchScale, _HatchWidth) * _HatchStrength;
                color = lerp(color, _HatchColor.rgb, hatch);

                // Rings along the length (shell plates, growth rings), some of them broken.
                // Moving rings get a random offset per piece; still rings stay on the plate seams.
                float ringCoord = input.uv.x * _RingCount - _Time.y * _RingSpeed + input.uv.y * 3.0 * saturate(_RingSpeed * 100.0);
                float keep = step(1.0 - _RingKeep, Hash11(floor(ringCoord + 0.5) + input.uv.y * 91.0));
                half lines = LineWorldStripe(ringCoord, _LineWidth) * keep * step(0.001, _RingCount);

                // Horizontal strata like contour lines on stone, slightly wavy.
                float strata = input.positionWS.y * _StrataFrequency
                    + 0.25 * sin(dot(input.positionWS.xz, float2(1.3, 0.9)) + input.uv.y * 6.0);
                lines = max(lines, LineWorldStripe(strata, _LineWidth) * step(0.001, _StrataFrequency));

                half rim = pow(1.0h - saturate(dot(normalWS, viewDirWS)), _RimPower) * _RimStrength;
                lines = max(lines, saturate(rim));

                half3 lineColor = LineWorldSheen(_LineColor.rgb, _LineColor2.rgb, normalWS, viewDirWS, input.positionWS, _SheenAmount);
                lineColor *= lerp(_ShadowLineDim, 1.0h, light);
                color = lerp(color, lineColor, lines);

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
