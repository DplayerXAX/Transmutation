Shader "Capstone/Effects/HeatMap"
{
    Properties
    {
        [Header(Fresnel)]
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 2.5
        _FresnelBias("Fresnel Bias", Range(0, 1)) = 0.15
        _FresnelWeight("Fresnel Weight", Range(0, 1)) = 0.55

        [Header(Noise)]
        _NoiseScale("Noise Scale", Range(0.1, 8)) = 1.4
        _WarpStrength("Domain Warp", Range(0, 2)) = 0.65
        _NoiseWeight("Noise Weight", Range(0, 1)) = 0.65
        _NoiseSpeed("Noise Speed", Vector) = (0.08, 0.05, 0, 0)

        [Header(Color Ramp)]
        [Toggle] _UseRampTex("Use Ramp Texture", Float) = 0
        [NoScaleOffset] _RampTex("Color Ramp", 2D) = "white" {}
        [HDR] _Color0("Cold - Deep Purple", Color) = (0.15, 0.0, 0.35, 1)
        [HDR] _Color1("Cool - Blue", Color) = (0.1, 0.4, 2.5, 1)
        [HDR] _Color2("Mid - Cyan", Color) = (0.0, 2.5, 2.5, 1)
        [HDR] _Color3("Warm - Green", Color) = (0.3, 2.8, 0.5, 1)
        [HDR] _Color4("Hot - Yellow", Color) = (3.0, 2.8, 0.2, 1)
        [HDR] _Color5("Hotter - Orange", Color) = (4.0, 1.5, 0.0, 1)
        [HDR] _Color6("Hottest - Red", Color) = (5.0, 0.2, 0.1, 1)
        _HDRIntensity("HDR Intensity", Range(0, 8)) = 1.5
        _Saturation("Saturation", Range(0, 4)) = 1.75
        _Contrast("Color Contrast", Range(0.5, 4)) = 1.45
        _HeatContrast("Heat Band Sharpness", Range(0.5, 4)) = 1.6

        [Header(Ramp Spacing)]
        _RampStop1("Blue Position", Range(0, 1)) = 0.167
        _RampStop2("Cyan Position", Range(0, 1)) = 0.333
        _RampStop3("Green Position", Range(0, 1)) = 0.5
        _RampStop4("Yellow Position", Range(0, 1)) = 0.667
        _RampStop5("Orange Position", Range(0, 1)) = 0.833

        [Header(Grain)]
        _GrainScale("Grain Size", Range(8, 200)) = 90
        _GrainAmount("Grain Amount", Range(0, 1)) = 0.35
        _GrainSpeed("Grain Speed", Range(1, 60)) = 28

        [Header(Alpha)]
        _BaseAlpha("Opacity", Range(0, 1)) = 0.85
        _AlphaFromRamp("Alpha From Ramp", Range(0, 1)) = 1
        _AlphaCutoff("Alpha Cutoff", Range(0, 0.5)) = 0.05

        [Header(Rendering)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull Mode (Back = cull back faces)", Float) = 2
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
            Name "HeatMap"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_RampTex);
            SAMPLER(sampler_RampTex);

            CBUFFER_START(UnityPerMaterial)
                float _FresnelPower;
                float _FresnelBias;
                float _FresnelWeight;
                float _NoiseScale;
                float _WarpStrength;
                float _NoiseWeight;
                float4 _NoiseSpeed;
                float4 _Color0;
                float4 _Color1;
                float4 _Color2;
                float4 _Color3;
                float4 _Color4;
                float4 _Color5;
                float4 _Color6;
                float _HDRIntensity;
                float _Saturation;
                float _Contrast;
                float _HeatContrast;
                float _RampStop1;
                float _RampStop2;
                float _RampStop3;
                float _RampStop4;
                float _RampStop5;
                float _GrainScale;
                float _GrainAmount;
                float _GrainSpeed;
                float _BaseAlpha;
                float _AlphaFromRamp;
                float _AlphaCutoff;
                float _UseRampTex;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
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
                for (int octave = 0; octave < 4; octave++)
                {
                    value += ValueNoise(p) * amplitude;
                    totalAmplitude += amplitude;
                    p = mul(float2x2(1.6, 1.2, -1.2, 1.6), p) + 17.17;
                    amplitude *= 0.5;
                }

                return value / totalAmplitude;
            }

            float DomainWarpedNoise(float3 position, float2 scroll)
            {
                float2 p = position.xz * _NoiseScale + scroll;
                float2 warp = float2(
                    FractalNoise(p + 13.7),
                    FractalNoise(p + 47.3)
                );
                p += (warp * 2.0 - 1.0) * _WarpStrength;

                float primary = FractalNoise(p);
                float secondary = FractalNoise(p * 1.9 + 8.4);
                return saturate(primary * 0.72 + secondary * 0.28);
            }

            float RandomGrain(float3 cell, float frame)
            {
                float4 seed = float4(cell, frame);
                seed = frac(seed * 0.1031);
                seed += dot(seed, seed.wzxy + 33.33);
                return frac((seed.x + seed.y) * (seed.z + seed.w));
            }

            void GetRampStops(out float s0, out float s1, out float s2, out float s3, out float s4, out float s5, out float s6)
            {
                s0 = 0.0;
                s1 = saturate(_RampStop1);
                s2 = saturate(max(s1 + 0.001, _RampStop2));
                s3 = saturate(max(s2 + 0.001, _RampStop3));
                s4 = saturate(max(s3 + 0.001, _RampStop4));
                s5 = saturate(max(s4 + 0.001, _RampStop5));
                s6 = 1.0;
            }

            float4 SampleRampColors(float t, float4 c0, float4 c1, float4 c2, float4 c3, float4 c4, float4 c5, float4 c6)
            {
                t = saturate(t);

                float s0, s1, s2, s3, s4, s5, s6;
                GetRampStops(s0, s1, s2, s3, s4, s5, s6);

                if (t <= s1)
                {
                    float localT = (t - s0) / max(s1 - s0, 0.0001);
                    return lerp(c0, c1, localT);
                }
                if (t <= s2)
                {
                    float localT = (t - s1) / max(s2 - s1, 0.0001);
                    return lerp(c1, c2, localT);
                }
                if (t <= s3)
                {
                    float localT = (t - s2) / max(s3 - s2, 0.0001);
                    return lerp(c2, c3, localT);
                }
                if (t <= s4)
                {
                    float localT = (t - s3) / max(s4 - s3, 0.0001);
                    return lerp(c3, c4, localT);
                }
                if (t <= s5)
                {
                    float localT = (t - s4) / max(s5 - s4, 0.0001);
                    return lerp(c4, c5, localT);
                }

                float localT = (t - s5) / max(s6 - s5, 0.0001);
                return lerp(c5, c6, localT);
            }

            float4 SampleProceduralRamp(float t)
            {
                return SampleRampColors(t, _Color0, _Color1, _Color2, _Color3, _Color4, _Color5, _Color6);
            }

            float HeatToRampUV(float t)
            {
                t = saturate(t);

                float s0, s1, s2, s3, s4, s5, s6;
                GetRampStops(s0, s1, s2, s3, s4, s5, s6);

                if (t <= s1)
                {
                    float localT = (t - s0) / max(s1 - s0, 0.0001);
                    return lerp(0.0 / 6.0, 1.0 / 6.0, localT);
                }
                if (t <= s2)
                {
                    float localT = (t - s1) / max(s2 - s1, 0.0001);
                    return lerp(1.0 / 6.0, 2.0 / 6.0, localT);
                }
                if (t <= s3)
                {
                    float localT = (t - s2) / max(s3 - s2, 0.0001);
                    return lerp(2.0 / 6.0, 3.0 / 6.0, localT);
                }
                if (t <= s4)
                {
                    float localT = (t - s3) / max(s4 - s3, 0.0001);
                    return lerp(3.0 / 6.0, 4.0 / 6.0, localT);
                }
                if (t <= s5)
                {
                    float localT = (t - s4) / max(s5 - s4, 0.0001);
                    return lerp(4.0 / 6.0, 5.0 / 6.0, localT);
                }

                float localT = (t - s5) / max(s6 - s5, 0.0001);
                return lerp(5.0 / 6.0, 1.0, localT);
            }

            float3 ApplySaturation(float3 color, float saturation)
            {
                float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
                return lerp(luma.xxx, color, saturation);
            }

            float3 ApplyContrast(float3 color, float contrast)
            {
                return saturate((color - 0.5) * contrast + 0.5);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float fresnel = 1.0 - saturate(dot(normalWS, viewDirWS));
                fresnel = pow(saturate(fresnel + _FresnelBias), _FresnelPower);

                float2 scroll = _Time.y * _NoiseSpeed.xy;
                float noise = DomainWarpedNoise(input.positionWS, scroll);
                float heat = saturate(fresnel * _FresnelWeight + noise * _NoiseWeight);
                heat = saturate((heat - 0.5) * _HeatContrast + 0.5);

                float4 proceduralRamp = SampleProceduralRamp(heat);
                float4 textureRamp = SAMPLE_TEXTURE2D(_RampTex, sampler_RampTex, float2(HeatToRampUV(heat), 0.5));
                float4 rampColor = lerp(proceduralRamp, textureRamp, saturate(_UseRampTex));
                rampColor.rgb *= _HDRIntensity;
                rampColor.rgb = ApplySaturation(rampColor.rgb, _Saturation);
                rampColor.rgb = ApplyContrast(rampColor.rgb, _Contrast);

                float frame = floor(_Time.y * _GrainSpeed);
                float3 grainCell = floor(input.positionOS * _GrainScale);
                float grain = RandomGrain(grainCell, frame);
                grain = lerp(0.5, grain, _GrainAmount);
                rampColor.rgb *= lerp(1.0 - _GrainAmount * 0.25, 1.0 + _GrainAmount * 0.35, grain);

                float rampAlpha = lerp(1.0, rampColor.a, _AlphaFromRamp);
                float alpha = saturate((heat - _AlphaCutoff) / max(1.0 - _AlphaCutoff, 0.001));
                alpha *= rampAlpha * _BaseAlpha;

                return half4(rampColor.rgb, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
