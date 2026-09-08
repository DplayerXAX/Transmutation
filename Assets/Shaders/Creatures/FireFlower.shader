Shader "Capstone/Creature/FireFlower"
{
    Properties
    {
        _Growth("Growth / Line Stretch", Range(0, 1)) = 0.75
        _LineStretch("Maximum Line Length", Range(1, 100)) = 24
        _GrowthDirection("Growth Direction (Object Space)", Vector) = (0, 1, 0, 0)
        _LineWarp("Line Bend Amount", Range(0, 2)) = 0.35
        _WarpFrequency("Line Bend Frequency", Range(0.1, 12)) = 3
        _WaveSpeed("Wave Motion Speed", Range(0, 5)) = 0.6
        _WaveDetail("Fractal Wave Detail", Range(0, 1)) = 0.5
        _StaticScale("Static Grain Size", Range(8, 160)) = 70
        _StaticSpeed("Static Speed", Range(1, 60)) = 24
        _StaticContrast("Static Contrast", Range(0.25, 4)) = 1.6
        _BaseAlpha("Opacity", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Static"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Growth;
                float _LineStretch;
                float4 _GrowthDirection;
                float _LineWarp;
                float _WarpFrequency;
                float _WaveSpeed;
                float _WaveDetail;
                float _StaticScale;
                float _StaticSpeed;
                float _StaticContrast;
                float _BaseAlpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
            };

            // Give every surface cell a new black-to-white value each frame.
            float RandomValue(float3 cell, float frame)
            {
                float4 seed = float4(cell, frame);
                seed = frac(seed * 0.1031);
                seed += dot(seed, seed.wzxy + 33.33);
                return frac((seed.x + seed.y) * (seed.z + seed.w));
            }

            // Smooth spatial noise: time moves through the field instead of reseeding it.
            float SmoothNoise(float3 position)
            {
                float3 cell = floor(position);
                float3 blend = frac(position);
                blend = blend * blend * (3.0 - 2.0 * blend);
                float lower = lerp(
                    lerp(RandomValue(cell, 0), RandomValue(cell + float3(1, 0, 0), 0), blend.x),
                    lerp(RandomValue(cell + float3(0, 1, 0), 0), RandomValue(cell + float3(1, 1, 0), 0), blend.x), blend.y);
                float upper = lerp(
                    lerp(RandomValue(cell + float3(0, 0, 1), 0), RandomValue(cell + float3(1, 0, 1), 0), blend.x),
                    lerp(RandomValue(cell + float3(0, 1, 1), 0), RandomValue(cell + float3(1, 1, 1), 0), blend.x), blend.y);
                return lerp(lower, upper, blend.z) * 2.0 - 1.0;
            }

            float FractalWave(float3 position)
            {
                float detail = saturate(_WaveDetail);
                float fineWeight = detail * detail;
                float wave = SmoothNoise(position);
                wave += detail * SmoothNoise(position * 2.03 + float3(7.1, 3.7, 9.2));
                wave += fineWeight * SmoothNoise(position * 4.11 + float3(13.4, 17.2, 5.8));
                return wave / (1.0 + detail + fineWeight);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float frame = floor(_Time.y * _StaticSpeed);
                float growth = saturate(_Growth);
                float3 direction = _GrowthDirection.xyz;
                float directionLengthSq = dot(direction, direction);
                direction = directionLengthSq > 0.0001
                    ? direction * rsqrt(max(directionLengthSq, 0.0001))
                    : float3(0, 1, 0);

                // Compress sampling along growth so the original cells become long blocks.
                // At zero growth these coordinates exactly match the Fire Bubble shader.
                float along = dot(input.positionOS, direction);
                float stretch = lerp(1.0, max(1.0, _LineStretch), growth);
                float3 samplePosition = input.positionOS
                    + direction * along * (rcp(stretch) - 1.0);

                // Use both transverse axes so neither pair of side faces misses the bend.
                float3 referenceAxis = abs(direction.y) < 0.99
                    ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 bendAxis = normalize(cross(direction, referenceAxis));
                float3 secondBendAxis = cross(direction, bendAxis);
                // Transverse variation also gives end faces a moving, warped pattern.
                // Sample the original position to keep the field continuous around edges.
                float3 wavePosition = float3(
                    dot(input.positionOS, bendAxis) * 0.65,
                    along,
                    dot(input.positionOS, secondBendAxis) * 0.65) * _WarpFrequency;
                float waveTime = _Time.y * _WaveSpeed;
                float3 drift = float3(0.31, -0.73, 0.47) * waveTime;
                float bend = FractalWave(wavePosition + drift);
                float secondBend = FractalWave(wavePosition + float3(19.1, 5.3, 11.7) - drift);
                samplePosition += (bendAxis * bend + secondBendAxis * secondBend)
                    * (_LineWarp * growth);
                float3 cell = floor(samplePosition * _StaticScale);
                float noise = RandomValue(cell, frame);

                noise = saturate((noise - 0.5) * _StaticContrast + 0.5);
                return half4(noise.xxx, _BaseAlpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
