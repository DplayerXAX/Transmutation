Shader "Capstone/Creature/CorrodingCrystal"
{
    // A glowing crystal that erosive water eats away (driven by CorrodingMesh).
    // Mesh data: vertex colour r = corrosion 0..1, g = fizz (recent erosion) 0..1.
    // Pits open in object space as corrosion rises, with a bright acid front at their edge.
    Properties
    {
        _BaseColor("Crystal", Color) = (0.6, 0.95, 1, 1)
        [HDR] _EmissionColor("Glow", Color) = (0.6, 2.2, 2.4, 1)
        _EtchColor("Etched", Color) = (0.09, 0.11, 0.12, 1)
        [HDR] _FizzColor("Acid Front", Color) = (1.6, 2.4, 0.9, 1)
        _PitScale("Pits Per Metre", Float) = 4.5
        _FrontWidth("Front Width", Range(0.01, 0.4)) = 0.09
        _Glint("Glint", Range(0, 2)) = 0.8
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _EmissionColor;
            half4 _EtchColor;
            half4 _FizzColor;
            float _PitScale;
            half _FrontWidth;
            half _Glint;
        CBUFFER_END

        float CorrodeHash(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return frac((p.x + p.y) * p.z);
        }

        // Smooth value noise 0..1 in 3D.
        float CorrodeNoise(float3 p)
        {
            float3 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(CorrodeHash(i), CorrodeHash(i + float3(1, 0, 0)), f.x),
                             lerp(CorrodeHash(i + float3(0, 1, 0)), CorrodeHash(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(CorrodeHash(i + float3(0, 0, 1)), CorrodeHash(i + float3(1, 0, 1)), f.x),
                             lerp(CorrodeHash(i + float3(0, 1, 1)), CorrodeHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
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
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                half2 state : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                output.state = input.color.rg;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                half corrosion = input.state.x, fizz = input.state.y;

                // Pits: noise thresholds that open one by one as corrosion rises.
                float3 p = input.positionOS * _PitScale;
                float pits = CorrodeNoise(p) * 0.65 + CorrodeNoise(p * 2.7 + 17.0) * 0.35;
                float level = corrosion * 1.2 - 0.1;
                half etched = smoothstep(pits - 0.04, pits + 0.04, level);
                half front = saturate(1.0 - abs(level - pits) / _FrontWidth) * step(0.01, corrosion);
                half flicker = 0.55 + 0.45 * sin(_Time.y * 37.0 + pits * 60.0) * sin(_Time.y * 23.0 + pits * 41.0);

                half3 albedo = lerp(_BaseColor.rgb, _EtchColor.rgb, etched);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half3 lit = albedo * (mainLight.color * saturate(dot(normalWS, mainLight.direction)) * mainLight.shadowAttenuation
                    + SampleSH(normalWS));
            #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                for (uint i = 0u; i < count; i++)
                {
                    Light light = GetAdditionalLight(i, input.positionWS);
                    lit += albedo * light.color * light.distanceAttenuation * saturate(dot(normalWS, light.direction));
                }
            #endif
                half glint = pow(saturate(dot(reflect(-viewDirWS, normalWS), mainLight.direction)), 32.0h) * _Glint * (1.0h - etched);
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDirWS)), 3.0h);

                // Intact crystal glows; etched pits go dark but keep a faint ember deep inside.
                half3 color = lit + glint * mainLight.color;
                // Kept low so lighting still shows the facets and the carved hollows.
                color += _EmissionColor.rgb * lerp(1.0h, 0.12h, etched) * (0.22h + 0.6h * fresnel);
                color += _FizzColor.rgb * front * (0.35h + fizz * 1.6h) * flicker;
                color += _FizzColor.rgb * etched * fizz * 0.35h * flicker;
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

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
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

            struct Attributes { float4 positionOS : POSITION; };

            float4 DepthVert(Attributes input) : SV_POSITION { return TransformObjectToHClip(input.positionOS.xyz); }

            half DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                return half4(PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5)), 0.0);
            #else
                return half4(normalWS, 0.0);
            #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
