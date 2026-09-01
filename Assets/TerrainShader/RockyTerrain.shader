Shader "Capstone/Terrain/RockyTerrain"
{
    Properties
    {
        [Header(Shape)]
        _Height("Terrain Height", Range(0, 20)) = 3
        _NoiseScale("Large Rock Scale", Range(0.01, 2)) = 0.18
        _DetailScale("Small Detail Scale", Range(0.1, 10)) = 1.4
        _DetailStrength("Small Detail Strength", Range(0, 1)) = 0.28
        _RidgeStrength("Rocky Ridge Strength", Range(0, 1)) = 0.45
        _Seed("Noise Offset", Vector) = (0, 0, 0, 0)

        [Header(Color)]
        _LowColor("Valley Color", Color) = (0.13, 0.11, 0.09, 1)
        _HighColor("Peak Color", Color) = (0.42, 0.38, 0.31, 1)
        _CliffColor("Cliff Color", Color) = (0.22, 0.20, 0.18, 1)
        _CliffStart("Cliff Slope", Range(0, 1)) = 0.55
        _Roughness("Roughness", Range(0, 1)) = 0.82
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Height;
                float _NoiseScale;
                float _DetailScale;
                float _DetailStrength;
                float _RidgeStrength;
                float4 _Seed;
                half4 _LowColor;
                half4 _HighColor;
                half4 _CliffColor;
                float _CliffStart;
                float _Roughness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float height01 : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 local = frac(p);
                float2 smoothLocal = local * local * (3.0 - 2.0 * local);

                float bottom = lerp(Hash21(cell), Hash21(cell + float2(1, 0)), smoothLocal.x);
                float top = lerp(Hash21(cell + float2(0, 1)), Hash21(cell + 1.0), smoothLocal.x);
                return lerp(bottom, top, smoothLocal.y);
            }

            float FractalNoise(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.55;
                float totalAmplitude = 0.0;

                [unroll]
                for (int octave = 0; octave < 5; octave++)
                {
                    value += ValueNoise(p) * amplitude;
                    totalAmplitude += amplitude;
                    p = mul(float2x2(1.6, 1.2, -1.2, 1.6), p) + 17.17;
                    amplitude *= 0.5;
                }

                return value / totalAmplitude;
            }

            float TerrainHeight(float2 positionXZ)
            {
                float2 p = positionXZ * _NoiseScale + _Seed.xz;
                float broad = FractalNoise(p);
                float detail = FractalNoise(p * _DetailScale + 31.7);

                // Inverted absolute noise introduces broken, stone-like ridges.
                float ridge = 1.0 - abs(FractalNoise(p * 0.72 + 9.4) * 2.0 - 1.0);
                ridge *= ridge;

                float terrain = broad;
                terrain += (detail - 0.5) * _DetailStrength;
                terrain = lerp(terrain, terrain * 0.72 + ridge * 0.42, _RidgeStrength);
                return terrain * _Height;
            }

            void BuildTerrainVertex(float3 originalPositionOS, out float3 positionWS, out float3 normalWS, out float height)
            {
                float3 originalPositionWS = TransformObjectToWorld(originalPositionOS);
                float sampleStep = max(0.01, 0.08 / max(_NoiseScale, 0.01));
                float centre = TerrainHeight(originalPositionWS.xz);
                float left = TerrainHeight(originalPositionWS.xz - float2(sampleStep, 0));
                float right = TerrainHeight(originalPositionWS.xz + float2(sampleStep, 0));
                float back = TerrainHeight(originalPositionWS.xz - float2(0, sampleStep));
                float front = TerrainHeight(originalPositionWS.xz + float2(0, sampleStep));

                height = centre;
                positionWS = originalPositionWS + float3(0, centre, 0);
                normalWS = normalize(float3(left - right, sampleStep * 2.0, back - front));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float height;
                BuildTerrainVertex(input.positionOS.xyz, output.positionWS, output.normalWS, height);

                output.positionHCS = TransformWorldToHClip(output.positionWS);
                output.height01 = saturate(height / max(_Height, 0.001));
                output.shadowCoord = TransformWorldToShadowCoord(output.positionWS);
                output.fogFactor = ComputeFogFactor(output.positionHCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(input.shadowCoord);

                float slope = 1.0 - saturate(normalWS.y);
                float cliff = smoothstep(_CliffStart, min(1.0, _CliffStart + 0.2), slope);
                half3 groundColor = lerp(_LowColor.rgb, _HighColor.rgb, input.height01);
                half3 albedo = lerp(groundColor, _CliffColor.rgb, cliff);

                float diffuse = saturate(dot(normalWS, mainLight.direction));
                half3 direct = mainLight.color * diffuse * mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                half3 ambient = SampleSH(normalWS);

                // A small broad highlight keeps the rock readable without making it glossy.
                float3 viewDirection = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                float3 halfDirection = SafeNormalize(mainLight.direction + viewDirection);
                float specularPower = lerp(64.0, 4.0, _Roughness);
                float specular = pow(saturate(dot(normalWS, halfDirection)), specularPower) * (1.0 - _Roughness);

                half3 color = albedo * (ambient + direct) + mainLight.color * specular;
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
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Height;
                float _NoiseScale;
                float _DetailScale;
                float _DetailStrength;
                float _RidgeStrength;
                float4 _Seed;
                half4 _LowColor;
                half4 _HighColor;
                half4 _CliffColor;
                float _CliffStart;
                float _Roughness;
            CBUFFER_END

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 local = frac(p);
                float2 smoothLocal = local * local * (3.0 - 2.0 * local);
                return lerp(
                    lerp(Hash21(cell), Hash21(cell + float2(1, 0)), smoothLocal.x),
                    lerp(Hash21(cell + float2(0, 1)), Hash21(cell + 1.0), smoothLocal.x),
                    smoothLocal.y
                );
            }

            float FractalNoise(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.55;
                float totalAmplitude = 0.0;
                [unroll] for (int octave = 0; octave < 5; octave++)
                {
                    value += ValueNoise(p) * amplitude;
                    totalAmplitude += amplitude;
                    p = mul(float2x2(1.6, 1.2, -1.2, 1.6), p) + 17.17;
                    amplitude *= 0.5;
                }
                return value / totalAmplitude;
            }

            float TerrainHeight(float2 positionXZ)
            {
                float2 p = positionXZ * _NoiseScale + _Seed.xz;
                float broad = FractalNoise(p);
                float detail = FractalNoise(p * _DetailScale + 31.7);
                float ridge = 1.0 - abs(FractalNoise(p * 0.72 + 9.4) * 2.0 - 1.0);
                ridge *= ridge;
                float terrain = broad + (detail - 0.5) * _DetailStrength;
                terrain = lerp(terrain, terrain * 0.72 + ridge * 0.42, _RidgeStrength);
                return terrain * _Height;
            }

            struct ShadowAttributes { float4 positionOS : POSITION; };
            struct ShadowVaryings { float4 positionHCS : SV_POSITION; };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS.y += TerrainHeight(positionWS.xz);
                output.positionHCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack Off
}
