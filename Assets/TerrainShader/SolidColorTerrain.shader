Shader "Capstone/Terrain/SolidColor"
{
    Properties
    {
        [MainColor] _Color("Terrain Color", Color) = (0.45, 0.52, 0.28, 1)

        [Header(Fresnel)]
        _FresnelColor("Fresnel Color", Color) = (0.55, 0.75, 1.0, 1)
        _FresnelPower("Fresnel Power", Range(0.1, 8)) = 3
        _FresnelIntensity("Fresnel Intensity", Range(0, 2)) = 1
        [Normal] _FresnelNormalMap("Fresnel Normal Map", 2D) = "bump" {}
        _FresnelNormalScale("Normal Tiling", Float) = 0.05
        _FresnelNormalStrength("Normal Strength", Range(0, 2)) = 1
        _FresnelScrollSpeed("Scroll Speed (XY)", Vector) = (0.05, 0.02, 0, 0)

        // Required by the Terrain engine
        [HideInInspector] _Control("Control (RGBA)", 2D) = "red" {}
        [HideInInspector] _TerrainHolesTexture("Holes Map (RGB)", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry-100"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "TerrainCompatible" = "True"
        }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_FresnelNormalMap);
            SAMPLER(sampler_FresnelNormalMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _FresnelColor;
                half _FresnelPower;
                half _FresnelIntensity;
                float4 _FresnelNormalMap_ST;
                float _FresnelNormalScale;
                half _FresnelNormalStrength;
                float4 _FresnelScrollSpeed;
            CBUFFER_END

            CBUFFER_START(_Terrain)
                #ifdef UNITY_INSTANCING_ENABLED
                float4 _TerrainHeightmapRecipSize;
                float4 _TerrainHeightmapScale;
                #endif
            CBUFFER_END

            #ifdef UNITY_INSTANCING_ENABLED
            TEXTURE2D(_TerrainHeightmapTexture);
            TEXTURE2D(_TerrainNormalmapTexture);
            #endif

            UNITY_INSTANCING_BUFFER_START(Terrain)
                UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData)
            UNITY_INSTANCING_BUFFER_END(Terrain)

            void TerrainInstancing(inout float4 positionOS, inout float3 normalOS)
            {
            #ifdef UNITY_INSTANCING_ENABLED
                float2 patchVertex = positionOS.xy;
                float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);
                float2 sampleCoords = (patchVertex.xy + instanceData.xy) * instanceData.z;
                float height = UnpackHeightmap(_TerrainHeightmapTexture.Load(int3(sampleCoords, 0)));

                positionOS.xz = sampleCoords * _TerrainHeightmapScale.xz;
                positionOS.y = height * _TerrainHeightmapScale.y;
                normalOS = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb * 2.0 - 1.0;
            #endif
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                TerrainInstancing(input.positionOS, input.normalOS);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 geoNormalWS = normalize(input.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                float2 fresnelUV = input.positionWS.xz * _FresnelNormalScale
                    + _FresnelScrollSpeed.xy * _Time.y;
                half3 normalTS = UnpackNormal(
                    SAMPLE_TEXTURE2D(_FresnelNormalMap, sampler_FresnelNormalMap, fresnelUV)
                );
                normalTS.xy *= _FresnelNormalStrength;

                // Perturb geometric normal in world XZ for scrolling fresnel only.
                float3 fresnelNormalWS = normalize(float3(
                    geoNormalWS.x + normalTS.x,
                    geoNormalWS.y,
                    geoNormalWS.z + normalTS.y
                ));

                half fresnel = pow(saturate(1.0h - saturate(dot(fresnelNormalWS, viewDirWS))), _FresnelPower);
                half3 color = _Color.rgb + _FresnelColor.rgb * fresnel * _FresnelIntensity;
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
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            CBUFFER_START(_Terrain)
                #ifdef UNITY_INSTANCING_ENABLED
                float4 _TerrainHeightmapRecipSize;
                float4 _TerrainHeightmapScale;
                #endif
            CBUFFER_END

            #ifdef UNITY_INSTANCING_ENABLED
            TEXTURE2D(_TerrainHeightmapTexture);
            TEXTURE2D(_TerrainNormalmapTexture);
            #endif

            UNITY_INSTANCING_BUFFER_START(Terrain)
                UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData)
            UNITY_INSTANCING_BUFFER_END(Terrain)

            void TerrainInstancing(inout float4 positionOS, inout float3 normalOS)
            {
            #ifdef UNITY_INSTANCING_ENABLED
                float2 patchVertex = positionOS.xy;
                float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);
                float2 sampleCoords = (patchVertex.xy + instanceData.xy) * instanceData.z;
                float height = UnpackHeightmap(_TerrainHeightmapTexture.Load(int3(sampleCoords, 0)));

                positionOS.xz = sampleCoords * _TerrainHeightmapScale.xz;
                positionOS.y = height * _TerrainHeightmapScale.y;
                normalOS = _TerrainNormalmapTexture.Load(int3(sampleCoords, 0)).rgb * 2.0 - 1.0;
            #endif
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                TerrainInstancing(input.positionOS, input.normalOS);

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
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
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

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(_Terrain)
                #ifdef UNITY_INSTANCING_ENABLED
                float4 _TerrainHeightmapRecipSize;
                float4 _TerrainHeightmapScale;
                #endif
            CBUFFER_END

            #ifdef UNITY_INSTANCING_ENABLED
            TEXTURE2D(_TerrainHeightmapTexture);
            TEXTURE2D(_TerrainNormalmapTexture);
            #endif

            UNITY_INSTANCING_BUFFER_START(Terrain)
                UNITY_DEFINE_INSTANCED_PROP(float4, _TerrainPatchInstanceData)
            UNITY_INSTANCING_BUFFER_END(Terrain)

            void TerrainInstancing(inout float4 positionOS)
            {
            #ifdef UNITY_INSTANCING_ENABLED
                float2 patchVertex = positionOS.xy;
                float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(Terrain, _TerrainPatchInstanceData);
                float2 sampleCoords = (patchVertex.xy + instanceData.xy) * instanceData.z;
                float height = UnpackHeightmap(_TerrainHeightmapTexture.Load(int3(sampleCoords, 0)));

                positionOS.xz = sampleCoords * _TerrainHeightmapScale.xz;
                positionOS.y = height * _TerrainHeightmapScale.y;
            #endif
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                TerrainInstancing(input.positionOS);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
