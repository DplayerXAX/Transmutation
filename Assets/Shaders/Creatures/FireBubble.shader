Shader "Capstone/Creature/FireBubble"
{
    Properties
    {
        _StaticScale("Static Grain Size", Range(8, 160)) = 70
        _StaticSpeed("Static Speed", Range(1, 60)) = 24
        _StaticContrast("Static Contrast", Range(0.25, 4)) = 1.6
        _BaseAlpha("Opacity", Range(0, 1)) = 1
        _ColorFlashChance("Color Flash Chance", Range(0, 1)) = 0.2
        _ColorFlashRate("Color Flash Rate", Range(0.1, 20)) = 4
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
                float _StaticScale;
                float _StaticSpeed;
                float _StaticContrast;
                float _BaseAlpha;
                float _ColorFlashChance;
                float _ColorFlashRate;
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
                float3 cell = floor(input.positionOS * _StaticScale);
                float noise = RandomValue(cell, frame);

                noise = saturate((noise - 0.5) * _StaticContrast + 0.5);
                // Keep the original grain; occasionally color the same cells.
                float flashTick = floor(_Time.y * _ColorFlashRate);
                float colored = RandomValue(float3(17, 53, 91), flashTick) < _ColorFlashChance ? 1.0 : 0.0;
                float hue = RandomValue(cell + float3(7, 19, 31), frame);
                float3 tint = saturate(abs(frac(hue + float3(0, 0.666667, 0.333333)) * 6 - 3) - 1);
                half3 color = lerp(noise.xxx, noise * tint, colored);
                return half4(color, _BaseAlpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
