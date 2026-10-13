Shader "Capstone/Landmarks/BoneComb"
{
    // Ribs like a comb jelly: a faint, glassy blue body that glows brightest at its edges (where you look
    // across it), with rows of tiny comb plates along it that catch the light in running rainbow sparkles.
    // Transparent and self-lit, so the ribs stand out sharply against the dark world.
    Properties
    {
        _Body("Body (alpha = opacity in the middle)", Color) = (0.12, 0.3, 0.85, 0.16)
        _Rim("Edge Glow", Color) = (0.55, 0.85, 1, 1)
        _RimPower("Edge Sharpness", Range(0.5, 8)) = 2.4
        _RimStrength("Edge Glow Strength", Range(0, 4)) = 1.8
        _Core("Inner Light", Color) = (0.75, 0.9, 1, 1)
        _CoreStrength("Inner Light Strength", Range(0, 2)) = 0.35
        _CombRows("Comb Plates Along A Rib", Float) = 140
        _CombSpeed("Sparkle Run Speed", Float) = 0.35
        _CombStrength("Sparkle Strength", Range(0, 4)) = 2.2
        _GlowColor("Heat Glow Color", Color) = (0.15, 0.75, 0.8, 1)
        _GlowStrength("Heat Glow Strength", Range(0, 4)) = 0.25
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Body;
                half4 _Rim;
                half _RimPower;
                half _RimStrength;
                half4 _Core;
                half _CoreStrength;
                float _CombRows;
                float _CombSpeed;
                half _CombStrength;
                half4 _GlowColor;
                half _GlowStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                return output;
            }

            half3 Rainbow(float h)
            {
                h = frac(h);
                return saturate(half3(abs(h * 6.0 - 3.0) - 1.0, 2.0 - abs(h * 6.0 - 2.0), 2.0 - abs(h * 6.0 - 4.0)));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                half facing = saturate(abs(dot(normalWS, viewDir)));
                half rim = pow(1.0 - facing, _RimPower);

                // Glassy body: faint in the middle, opaque and bright at the edges.
                half alpha = saturate(lerp(_Body.a, 1.0, rim * 0.85));
                half3 colour = _Body.rgb * alpha;
                colour += _Rim.rgb * rim * _RimStrength;
                colour += _Core.rgb * pow(facing, 6.0) * _CoreStrength;

                // Comb plates: fine rows along the rib; a bright run of rainbow travels along them.
                float along = input.uv.x;
                float plate = smoothstep(0.55, 0.95, 0.5 + 0.5 * sin(along * _CombRows * 6.2832));
                float run = pow(saturate(0.5 + 0.5 * sin((along * 2.5 - _Time.y * _CombSpeed + input.uv.y * 3.0) * 6.2832)), 6.0);
                half3 sparkle = Rainbow(along * 3.0 - _Time.y * 0.2 + facing) * plate * run * _CombStrength * (0.35 + rim);
                colour += sparkle;
                alpha = saturate(alpha + dot(sparkle, 0.33));

                // Heat (from the kiln) warms the glow.
                colour += _GlowColor.rgb * _GlowStrength * rim;
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
