Shader "Capstone/Skybox/WhiteNoise"
{
    Properties
    {
        [MainColor] _Color("Tint", Color) = (1, 1, 1, 1)
        _NoiseScale("Noise Scale", Float) = 1
        _NoiseStrength("Noise Strength", Range(0, 1)) = 1
        _NoiseSpeed("Noise Speed", Float) = 24
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            Name "Skybox"
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _NoiseScale;
                half _NoiseStrength;
                float _NoiseSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            float Hash31(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            // Extra scramble so neighboring frames/pixels stay decorrelated.
            float WhiteNoise(float2 pixel, float frame)
            {
                float n = Hash31(float3(pixel, frame));
                n = Hash31(float3(pixel.yx + 17.13, n * 43758.5453 + frame));
                return n;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Use raw pixel coords from clip space for stable screen-space cells.
                float2 pixelCoord = floor(input.positionCS.xy * max(_NoiseScale, 1e-4));
                float frame = floor(_Time.y * max(_NoiseSpeed, 0.0));

                // Time is a 3rd hash axis (not a spatial offset), so each frame is a new pattern.
                float noise = WhiteNoise(pixelCoord, frame);

                half3 color = lerp(1.0h, noise, _NoiseStrength) * _Color.rgb;
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
