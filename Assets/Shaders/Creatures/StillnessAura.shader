Shader "Capstone/Creature/StillnessAura"
{
    // The Geode Larva's stillness aura: an additive soap-film shell, bright at its rim and nearly clear
    // face-on, visible from inside too. _Fade scales it; slow bands drift across it like held breath.
    Properties
    {
        [HDR] _Color("Color", Color) = (0.5, 1.4, 1.1, 1)
        _Fade("Fade", Range(0, 1)) = 1
        _RimPower("Rim Power", Range(0.5, 8)) = 3
        _Bands("Bands Per Metre", Float) = 1.2
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Aura"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Fade;
                half _RimPower;
                float _Bands;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = pow(1.0h - abs(dot(normalWS, viewDirWS)), _RimPower);
                half bands = 0.75h + 0.25h * sin(input.positionWS.y * _Bands * 6.2831853 - _Time.y * 0.8);
                half3 color = _Color.rgb * (rim * bands + 0.03h) * _Fade;
                // Additive: fog fades it towards black, not towards the fog colour.
                color = MixFogColor(color, half3(0, 0, 0), input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
