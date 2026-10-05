Shader "Capstone/Creatures/TentacleLines"
{
    // Matches the terrain's line language: a flat dark (or light) body crossed by thin
    // contour rings that radiate from the creature's centre and travel outward like a pulse.
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0.06, 0.06, 0.07, 1)
        _LineColor("Line Color", Color) = (1, 1, 1, 1)
        _ShadeLevels("Shade Levels", Range(1, 6)) = 3
        _ShadeStrength("Shade Strength", Range(0, 1)) = 0.35

        [Header(Contour Rings)]
        _RingFrequency("Rings Per Metre", Float) = 7
        _RingSpeed("Ring Travel Speed", Float) = 0.6
        _LineWidth("Line Width (pixels)", Range(0.3, 4)) = 1.2
        _RingKeep("Ring Keep (broken rings)", Range(0, 1)) = 0.75

        [Header(Rim)]
        _RimPower("Rim Power", Range(0.5, 8)) = 3
        _RimStrength("Rim Strength", Range(0, 1)) = 0.6

        [Header(Driven By Script)]
        _Center("Creature Centre (WS)", Vector) = (0, 0, 0, 0)
        _Activity("Activity", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _LineColor;
            half _ShadeLevels;
            half _ShadeStrength;
            float _RingFrequency;
            float _RingSpeed;
            half _LineWidth;
            half _RingKeep;
            half _RimPower;
            half _RimStrength;
            float4 _Center;
            half _Activity;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));

                // Posterized light: a few flat steps, like the FBM terrain's colour bands.
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half ndotl = saturate(dot(normalWS, mainLight.direction)) * mainLight.shadowAttenuation;
                half levels = max(_ShadeLevels, 1.0);
                half stepped = floor(ndotl * levels + 0.5) / levels;
                half3 color = _BaseColor.rgb * lerp(1.0 - _ShadeStrength, 1.0 + _ShadeStrength, stepped);

                // Contour rings around the centre, travelling outward; faster when the creature is busy.
                float distanceToCentre = distance(input.positionWS, _Center.xyz);
                float speed = _RingSpeed * (1.0 + 2.5 * _Activity);
                float ringCoord = distanceToCentre * _RingFrequency - _Time.y * speed;
                float ringWave = abs(frac(ringCoord) - 0.5) * 2.0;           // 0 at the line centre
                float width = max(fwidth(ringCoord) * 2.0 * _LineWidth, 1e-4);
                float ring = 1.0 - smoothstep(0.0, width, 1.0 - ringWave);
                // Drop some rings so the pattern reads as broken contours rather than stripes.
                float keep = step(1.0 - _RingKeep, Hash11(floor(ringCoord + 0.5) + 7.0));
                color = lerp(color, _LineColor.rgb, ring * keep);

                // Thin silhouette rim in the line colour.
                half rim = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _RimPower) * _RimStrength;
                color = lerp(color, _LineColor.rgb, saturate(rim));

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
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
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag

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

            Varyings NormalsVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 NormalsFrag(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
