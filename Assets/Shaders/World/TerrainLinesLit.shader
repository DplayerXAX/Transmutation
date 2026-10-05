Shader "Capstone/World/TerrainLinesLit"
{
    // Same polar ray / ring line pattern as Capstone/Terrain/SolidColor, but lit:
    // stepped sun light, received shadows, SSAO and hatching in the dark parts.
    // The inner world uses it with no sun, a fake fill light, an optical sheen and haze.
    Properties
    {
        [MainColor] _Color("Lit Base Color", Color) = (0.16, 0.17, 0.19, 1)
        _ShadowColor("Shadow Base Color", Color) = (0.02, 0.02, 0.03, 1)
        _LineColor("Line Color", Color) = (1, 1, 1, 1)
        _LineColor2("Line Sheen Color", Color) = (1, 1, 1, 1)
        _ShadowLineDim("Line Brightness In Shadow", Range(0, 1)) = 0.4

        [Header(Lighting)]
        _MainLightAmount("Sun Amount", Range(0, 2)) = 1
        _FakeLightDir("Fill Light Direction", Vector) = (0.3, 1, 0.2, 0)
        _FakeLightAmount("Fill Light Amount", Range(0, 2)) = 0.25
        _ShadeLevels("Shade Levels", Range(1, 6)) = 3

        [Header(Hatching)]
        _HatchColor("Hatch Color", Color) = (0.42, 0.42, 0.44, 1)
        _HatchStrength("Hatch Strength", Range(0, 1)) = 0.8
        _HatchScale("Hatch Lines Per Metre", Float) = 3
        _HatchWidth("Hatch Width (pixels)", Range(0.3, 4)) = 1

        [Header(Optical)]
        _SheenAmount("Line Sheen Amount", Range(0, 1)) = 0
        _HazeColor("Haze Color (alpha = strength)", Color) = (0, 0, 0, 0)
        _HazeStart("Haze Start", Float) = 10
        _HazeEnd("Haze End", Float) = 80

        [Header(Polar)]
        _Center("Center XZ", Vector) = (0, 0, 0, 0)
        _PolarScale("Polar Scale", Float) = 0.02
        _RingFreq("Ring Freq", Float) = 46
        _RingSqrtBoost("Ring Sqrt Boost", Float) = 6
        _RingTravel("Ring Travel", Float) = 7
        _RingDistort("Ring Distort", Float) = 4
        _RayPhaseScale("Ray Phase Scale", Float) = 29.25
        _RayPhaseProximityBoost("Phase Proximity Boost", Float) = 8
        _RayPhaseProximityFalloff("Phase Proximity Falloff", Float) = 0.15
        _RaySpeed("Ray Travel Speed", Float) = 1

        [Header(Floor Rings)]
        _FloorHeight("Floor Height (World Y)", Float) = 0
        _FloorBlendWidth("Floor Blend Width", Float) = 2
        _FloorFreq("Floor Coord Freq", Float) = 82
        _FloorTravel("Floor Travel", Float) = 0.96
        _FloorViewFalloff("Floor View Falloff", Float) = 0.08
        _FloorViewInfluence("Floor View Influence", Range(0, 1)) = 1
        _FloorTriplanarSharpness("Floor Triplanar Sharpness", Range(1, 8)) = 4
        _RingKeepThreshold("Ring Keep Threshold", Range(0, 1)) = 0.14
        _RingKeepMix("Ring Keep Mix", Range(0, 1)) = 0.67
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
            half _MainLightAmount;
            float4 _FakeLightDir;
            half _FakeLightAmount;
            half _ShadeLevels;
            half4 _HatchColor;
            half _HatchStrength;
            float _HatchScale;
            half _HatchWidth;
            half _SheenAmount;
            half4 _HazeColor;
            float _HazeStart;
            float _HazeEnd;
            float4 _Center;
            float _PolarScale;
            float _RingFreq;
            float _RingSqrtBoost;
            float _RingTravel;
            float _RingDistort;
            float _RayPhaseScale;
            float _RayPhaseProximityBoost;
            float _RayPhaseProximityFalloff;
            float _RaySpeed;
            float _FloorHeight;
            float _FloorBlendWidth;
            float _FloorFreq;
            float _FloorTravel;
            float _FloorViewFalloff;
            float _FloorViewInfluence;
            float _FloorTriplanarSharpness;
            float _RingKeepThreshold;
            float _RingKeepMix;
        CBUFFER_END
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
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                return output;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float FloorWarp2D(float2 p, float axis, float f0, float f1, float fCross0, float fCross1)
            {
                return 2.2 * sin(p.x * f0 + axis * 1.3) +
                       2.2 * sin(p.y * f1 - axis * 0.9) +
                       1.0 * sin(p.x * fCross0 + p.y * fCross1 + axis * 9.0);
            }

            // The original terrain line pattern (rays + rings), returned as a 0..1 mask.
            half TerrainLines(float3 posWS, float3 normalWS)
            {
                float2 uv = (posWS.xz - _Center.xz) * max(_PolarScale, 1e-5);
                float r = length(uv);
                float a = atan2(uv.y, uv.x);
                float t = _Time.y * _RaySpeed;

                float swirl = 0.55 * exp(-r * 4.2) + 0.055 * r * sin(a * 2.0);
                float movingBend =
                    smoothstep(0.015, 0.16, r) *
                    (0.43 * sin(r * 21.0 - t * 4.2 + sin(a * 7.0)) +
                     0.16 * sin(r * 49.0 - t * 7.1 - a * 5.0));

                float camDist = distance(posWS, GetCameraPositionWS());
                float proximity = saturate(exp(-camDist * max(_RayPhaseProximityFalloff, 0.0)));
                float phaseScale = _RayPhaseScale + proximity * _RayPhaseProximityBoost;

                float travel = t * _RingTravel;
                float distort = max(_RingDistort, 0.0);
                float yRel = posWS.y - _FloorHeight;

                float wobble = 0.0055 * sin(a * 3.0 + r * 31.0) + 0.003 * sin(a * 11.0 - r * 19.0);
                float rr = r + wobble * distort;

                float phaseDistort = distort * (
                    0.42 * sin(a * 3.0) + 0.25 * sin(a * 9.0 + r * 22.0) +
                    0.18 * sin(a * 5.0 + r * 14.0) + 0.12 * sin(a * 13.0 - r * 27.0) +
                    _RingFreq * 0.5 * wobble);

                float rayPhase = (a + swirl + wobble * distort) * phaseScale + movingBend + phaseDistort;
                float rayWave = abs(sin(rayPhase));
                float rayAA = max(fwidth(rayWave) * 0.56, 0.022);
                float rays = 1.0 - smoothstep(0.0, rayAA, rayWave);

                float ringCoord = _RingFreq * rr + _RingSqrtBoost * sqrt(max(rr, 0.0)) - travel + phaseDistort;

                float heightBlend = smoothstep(_FloorHeight, _FloorHeight + max(_FloorBlendWidth, 1e-4), posWS.y);
                float viewProximity = saturate(exp(-camDist * max(_FloorViewFalloff, 0.0)));
                float floorBlend = heightBlend * lerp(1.0, viewProximity, saturate(_FloorViewInfluence));

                float3 triBlend = abs(normalize(normalWS));
                triBlend = pow(max(triBlend, 1e-4), max(_FloorTriplanarSharpness, 1.0));
                triBlend /= max(triBlend.x + triBlend.y + triBlend.z, 1e-5);

                float fx = 8.0 + 1.7 * sin(yRel * 0.37);
                float fy = 8.0 + 1.9 * cos(yRel * 0.33);
                float fz = 8.0 + 2.1 * cos(yRel * 0.29);
                float fCrossX = 23.0 + 3.0 * sin(yRel * 0.51);
                float fCrossY = 11.0 + 2.4 * sin(yRel * 0.47);
                float fCrossZ = 9.0 + 2.0 * cos(yRel * 0.43);

                float3 p = float3(posWS.x - _Center.x, yRel, posWS.z - _Center.z);
                float floorBase =
                    triBlend.y * (_FloorFreq * p.y) +
                    triBlend.z * (_FloorFreq * p.x) +
                    triBlend.x * (_FloorFreq * p.z);
                floorBase -= travel * _FloorTravel;

                float warpXZ = FloorWarp2D(p.xz, p.y, fx, fz, fCrossX, fCrossZ);
                float warpXY = FloorWarp2D(p.xy, p.z, fx, fy, fCrossX, fCrossY);
                float warpZY = FloorWarp2D(p.zy, p.x, fz, fy, fCrossZ, fCrossY);
                float floorWarp = warpXZ * triBlend.y + warpXY * triBlend.z + warpZY * triBlend.x;

                float floorWobble =
                    0.0055 * sin(a * 3.0 + r * 31.0 + yRel * 7.3) +
                    0.003 * sin(a * 11.0 - r * 19.0 - yRel * 5.1);
                float floorPhaseDistort = distort * (
                    0.42 * sin(a * 3.0 + yRel * 2.4) +
                    0.25 * sin(a * 9.0 + r * 22.0 - yRel * 1.8) +
                    0.18 * sin(a * 5.0 + r * 14.0 + yRel * 3.1) +
                    0.12 * sin(a * 13.0 - r * 27.0 + yRel * 4.7) +
                    _FloorFreq * 0.5 * floorWobble);

                float floorCoord = floorBase + distort * floorWarp + floorPhaseDistort;
                ringCoord = lerp(ringCoord, floorCoord, floorBlend);

                float ringWave = abs(sin(ringCoord));
                float ringAA = max(fwidth(ringWave) * 0.58, 0.025);
                float rings = 1.0 - smoothstep(0.0, ringAA, ringWave);

                float ringId = floor(ringCoord / 3.14159265 + 0.5);
                float arcId = floor((a + 3.1416) * 9.0);
                float ringKeep = step(_RingKeepThreshold, Hash21(float2(ringId, arcId) + 17.0));
                rings *= lerp(ringKeep, 1.0, _RingKeepMix);

                return saturate(max((half)rings, (half)rays));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);

                half ao;
                half light = LineWorldLight(input.positionWS, normalWS, screenUV,
                    _MainLightAmount, _FakeLightDir.xyz, _FakeLightAmount, _ShadeLevels, ao);

                half3 baseColor = lerp(_ShadowColor.rgb, _Color.rgb, light) * lerp(0.6h, 1.0h, ao);
                half hatch = LineWorldHatch(input.positionWS, light, _HatchScale, _HatchWidth) * _HatchStrength;
                half3 color = lerp(baseColor, _HatchColor.rgb, hatch);

                half3 lineColor = LineWorldSheen(_LineColor.rgb, _LineColor2.rgb, normalWS, viewDirWS, input.positionWS, _SheenAmount);
                lineColor *= lerp(_ShadowLineDim, 1.0h, light);
                color = lerp(color, lineColor, TerrainLines(input.positionWS, normalWS));

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
            };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
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

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
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
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
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
