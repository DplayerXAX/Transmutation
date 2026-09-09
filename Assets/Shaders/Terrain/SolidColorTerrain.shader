Shader "Capstone/Terrain/SolidColor"
{
    Properties
    {
        [MainColor] _Color("Base Color", Color) = (0.08, 0.1, 0.12, 1)
        _LineColor("Line Color", Color) = (1, 1, 1, 1)

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

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _LineColor;
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

            half4 Frag(Varyings input) : SV_Target
            {
                // XZ UV -> polar (r, a). Polar Scale maps world meters into the swirl range.
                float2 uv = (input.positionWS.xz - _Center.xz) * max(_PolarScale, 1e-5);
                float r = length(uv);
                float a = atan2(uv.y, uv.x); // [-PI, PI]
                float t = _Time.y * _RaySpeed;

                float swirl = 0.55 * exp(-r * 4.2) + 0.055 * r * sin(a * 2.0);
                // Travelling bends run from the throat towards the camera.
                // Amplitude vanishes at r=0 so grooves stay attached to the vanishing point.
                float movingBend =
                    smoothstep(0.015, 0.16, r) *
                    (0.43 * sin(r * 21.0 - t * 4.2 + sin(a * 7.0)) +
                     0.16 * sin(r * 49.0 - t * 7.1 - a * 5.0));

                float camDist = distance(input.positionWS, GetCameraPositionWS());
                float proximity = saturate(exp(-camDist * max(_RayPhaseProximityFalloff, 0.0)));
                float phaseScale = _RayPhaseScale + proximity * _RayPhaseProximityBoost;

                float travel = t * _RingTravel;
                float distort = max(_RingDistort, 0.0);
                float3 posWS = input.positionWS;
                float yRel = posWS.y - _FloorHeight;

                float wobble =
                    0.0055 * sin(a * 3.0 + r * 31.0) + 0.003 * sin(a * 11.0 - r * 19.0);
                float rr = r + wobble * distort;

                float phaseDistort = distort * (
                    0.42 * sin(a * 3.0) + 0.25 * sin(a * 9.0 + r * 22.0) +
                    0.18 * sin(a * 5.0 + r * 14.0) + 0.12 * sin(a * 13.0 - r * 27.0) +
                    _RingFreq * 0.5 * wobble
                );

                // Rays share the same distort field so they bend with the rings.
                float rayPhase = (a + swirl + wobble * distort) * phaseScale + movingBend + phaseDistort;
                float rayWave = abs(sin(rayPhase));
                float rayAA = max(fwidth(rayWave) * 0.56, 0.022);
                float rays = 1.0 - smoothstep(0.0, rayAA, rayWave);

                // Cross-contours: spacing expands away from the vanishing point;
                // decreasing phase (travel) pushes contours outward = forward motion cue.
                float ringCoord = _RingFreq * rr + _RingSqrtBoost * sqrt(max(rr, 0.0)) - travel;
                ringCoord += phaseDistort;

                float heightBlend = smoothstep(
                    _FloorHeight,
                    _FloorHeight + max(_FloorBlendWidth, 1e-4),
                    posWS.y
                );
                float viewProximity = saturate(exp(-camDist * max(_FloorViewFalloff, 0.0)));
                float floorBlend = heightBlend * lerp(1.0, viewProximity, saturate(_FloorViewInfluence));

                // Triplanar floor: blend XZ / XY / ZY by world normal to reduce stretch on slopes/walls.
                float3 triBlend = abs(normalize(input.normalWS));
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
                    _FloorFreq * 0.5 * floorWobble
                );

                float floorCoord = floorBase + distort * floorWarp + floorPhaseDistort;

                // Full blend into one phase field avoids two grids beating (moire).
                ringCoord = lerp(ringCoord, floorCoord, floorBlend);

                float ringWave = abs(sin(ringCoord));
                float ringAA = max(fwidth(ringWave) * 0.58, 0.025);
                float rings = 1.0 - smoothstep(0.0, ringAA, ringWave);

                float ringId = floor(ringCoord / 3.14159265 + 0.5);
                float arcId = floor((a + 3.1416) * 9.0);
                float ringKeep = step(_RingKeepThreshold, Hash21(float2(ringId, arcId) + 17.0));
                rings *= lerp(ringKeep, 1.0, _RingKeepMix);

                half lines = saturate(max((half)rings, (half)rays));
                half3 color = lerp(_Color.rgb, _LineColor.rgb, lines);
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

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling nomatrices nolightprobe nolightmap
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

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
                float3 normalWS : TEXCOORD0;
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                TerrainInstancing(input.positionOS, input.normalOS);

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
                half3 packedNormalWS = PackFloat2To888(remappedOct);
                return half4(packedNormalWS, 0.0);
            #else
                return half4(normalWS, 0.0);
            #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
