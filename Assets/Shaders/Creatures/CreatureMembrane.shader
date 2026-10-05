Shader "Capstone/Creatures/Membrane"
{
    // Opaque, wet skin around a creature skeleton. It hides the bones except where it tears:
    // slowly drifting holes cut through the skin, edged with a bright lip, and the raw inside
    // of the skin (back faces) shows as dark flesh around whatever bone lies beneath.
    // Uses mesh UVs from TentacleCreature: uv0 = stable surface position, uv1.x = limb id.
    Properties
    {
        [MainColor] _BaseColor("Skin Color", Color) = (0.05, 0.05, 0.06, 1)
        _LineColor("Line Color", Color) = (1, 1, 1, 1)
        _InnerColor("Inner Flesh Color", Color) = (0.32, 0.02, 0.05, 1)
        _ShadeLevels("Shade Levels", Range(1, 6)) = 3
        _ShadeStrength("Shade Strength", Range(0, 1)) = 0.3

        [Header(Tears)]
        _TearAmount("Tear Amount", Range(0, 0.6)) = 0.16
        _TearScale("Tear Scale", Float) = 1.6
        _TearDrift("Tear Drift Speed", Float) = 0.04
        _TearLip("Tear Lip Width", Range(0, 0.2)) = 0.05

        [Header(Silhouette)]
        _RimPower("Rim Power", Range(0.5, 8)) = 3
        _RimBrightness("Rim Brightness", Range(0, 1)) = 0.3

        [Header(Wet Highlight)]
        _HighlightSize("Highlight Size", Range(0, 0.2)) = 0.04

        [Header(Contour Rings)]
        _RingFrequency("Rings Per Metre", Float) = 5
        _RingSpeed("Ring Travel Speed", Float) = 0.5
        _LineWidth("Line Width (pixels)", Range(0.3, 4)) = 1
        _RingKeep("Ring Keep (broken rings)", Range(0, 1)) = 0.45
        _LineOpacity("Line Opacity", Range(0, 1)) = 0.75

        [Header(Driven By Script)]
        _Center("Creature Centre (WS)", Vector) = (0, 0, 0, 0)
        _Activity("Activity", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _LineColor;
            half4 _InnerColor;
            half _ShadeLevels;
            half _ShadeStrength;
            half _TearAmount;
            float _TearScale;
            float _TearDrift;
            half _TearLip;
            half _RimPower;
            half _RimBrightness;
            half _HighlightSize;
            float _RingFrequency;
            float _RingSpeed;
            half _LineWidth;
            half _RingKeep;
            half _LineOpacity;
            float4 _Center;
            half _Activity;
        CBUFFER_END

        float Hash31(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return frac((p.x + p.y) * p.z);
        }

        float ValueNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            float3 u = f * f * (3.0 - 2.0 * f);
            float n000 = Hash31(i), n100 = Hash31(i + float3(1, 0, 0));
            float n010 = Hash31(i + float3(0, 1, 0)), n110 = Hash31(i + float3(1, 1, 0));
            float n001 = Hash31(i + float3(0, 0, 1)), n101 = Hash31(i + float3(1, 0, 1));
            float n011 = Hash31(i + float3(0, 1, 1)), n111 = Hash31(i + float3(1, 1, 1));
            return lerp(lerp(lerp(n000, n100, u.x), lerp(n010, n110, u.x), u.y),
                        lerp(lerp(n001, n101, u.x), lerp(n011, n111, u.x), u.y), u.z);
        }

        // 0..1; values above (1 - _TearAmount) are holes.
        float TearField(float3 surface, float limb)
        {
            float3 p = surface * _TearScale + float3(limb * 17.3, limb * 5.1, _Time.y * _TearDrift);
            return ValueNoise(p) * 0.65 + ValueNoise(p * 2.3 + 11.7) * 0.35;
        }

        // Discards torn skin; returns how close this point is to a tear edge (0..1).
        float ApplyTears(float3 surface, float limb)
        {
            float threshold = 1.0 - _TearAmount;
            float tear = TearField(surface, limb);
            clip(threshold - tear);
            return saturate(1.0 - (threshold - tear) / max(_TearLip, 1e-4));
        }

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float3 surface : TEXCOORD0;
            float2 limb : TEXCOORD1;
        };
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            // Both sides: through a tear you see the raw inside of the skin.
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 surface : TEXCOORD2;
                float limb : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            float Hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.surface = input.surface;
                output.limb = input.limb.x;
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                float lip = ApplyTears(input.surface, input.limb);

                float3 normalWS = normalize(input.normalWS) * (isFrontFace ? 1.0 : -1.0);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));

                half ndotl = saturate(dot(normalWS, mainLight.direction)) * mainLight.shadowAttenuation;
                half levels = max(_ShadeLevels, 1.0);
                half stepped = floor(ndotl * levels + 0.5) / levels;
                half shade = lerp(1.0 - _ShadeStrength, 1.0 + _ShadeStrength, stepped);

                if (!isFrontFace)
                {
                    // Inside of the skin: flat dark flesh, darker away from the opening.
                    half3 inner = _InnerColor.rgb * lerp(0.45, 1.0, lip) * shade;
                    return half4(MixFog(inner, input.fogFactor), 1.0);
                }

                half3 color = _BaseColor.rgb * shade;

                half rim = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _RimPower);
                color = lerp(color, _LineColor.rgb, rim * _RimBrightness);

                // Travelling contour rings from the body centre; faster when the creature is busy.
                float distanceToCentre = distance(input.positionWS, _Center.xyz);
                float speed = _RingSpeed * (1.0 + 2.5 * _Activity);
                float ringCoord = distanceToCentre * _RingFrequency - _Time.y * speed;
                float ringWave = abs(frac(ringCoord) - 0.5) * 2.0;
                float width = max(fwidth(ringCoord) * 2.0 * _LineWidth, 1e-4);
                float ring = 1.0 - smoothstep(0.0, width, 1.0 - ringWave);
                ring *= step(1.0 - _RingKeep, Hash11(floor(ringCoord + 0.5) + 3.0)) * _LineOpacity;
                color = lerp(color, _LineColor.rgb, ring);

                // Bright, hard lip around each tear.
                color = lerp(color, _LineColor.rgb, step(0.5, lip));

                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                half highlight = step(1.0 - _HighlightSize, saturate(dot(normalWS, halfDir))) * mainLight.shadowAttenuation;
                color = lerp(color, _LineColor.rgb, highlight);

                return half4(MixFog(color, input.fogFactor), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 surface : TEXCOORD0;
                float limb : TEXCOORD1;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
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
                output.positionCS = positionCS;
                output.surface = input.surface;
                output.limb = input.limb.x;
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                ApplyTears(input.surface, input.limb);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 surface : TEXCOORD0;
                float limb : TEXCOORD1;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.surface = input.surface;
                output.limb = input.limb.x;
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                ApplyTears(input.surface, input.limb);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 surface : TEXCOORD1;
                float limb : TEXCOORD2;
            };

            Varyings NormalsVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.surface = input.surface;
                output.limb = input.limb.x;
                return output;
            }

            half4 NormalsFrag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                ApplyTears(input.surface, input.limb);
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS) * (isFrontFace ? 1.0 : -1.0);
                return half4(normalWS, 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
