Shader "Capstone/Creature/FireBubble"
{
    Properties
    {
        [HDR] _LowHeatColor("Low Heat Color", Color) = (0.22, 0.005, 0.015, 1)
        [HDR] _HighHeatColor("High Heat Color", Color) = (2.8, 0.035, 0.01, 1)
        [HDR] _VeinColor("Moving Vein Color", Color) = (4.0, 0.12, 0.015, 1)

        _Heat("Heat", Float) = 13
        _StillHeat("Still Heat", Float) = 13
        _MaximumHeat("Maximum Visual Heat", Float) = 25
        _FlowTime("Flow Time", Float) = 0

        _PatternScale("Pattern Scale", Range(0.1, 10)) = 2.5
        _PatternContrast("Pattern Contrast", Range(0.25, 6)) = 2
        _Distortion("Surface Distortion", Range(0, 0.2)) = 0.035
        _EmissionStrength("Emission Strength", Range(0, 5)) = 1.3

        _BaseAlpha("Base Alpha", Range(0, 1)) = 0.72
        _FresnelPower("Fresnel Power", Range(0.25, 8)) = 2.5
        _FresnelAlpha("Fresnel Alpha", Range(0, 1)) = 0.25
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
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _LowHeatColor;
                half4 _HighHeatColor;
                half4 _VeinColor;
                float _Heat;
                float _StillHeat;
                float _MaximumHeat;
                float _FlowTime;
                float _PatternScale;
                float _PatternContrast;
                float _Distortion;
                float _EmissionStrength;
                float _BaseAlpha;
                float _FresnelPower;
                float _FresnelAlpha;
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
                float4 shadowCoord : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            float Heat01()
            {
                return saturate((_Heat - _StillHeat) / max(0.001, _MaximumHeat - _StillHeat));
            }

            float FlowPattern(float3 positionOS, float flowTime)
            {
                float3 p = positionOS * _PatternScale;

                float waveA = sin(p.y * 2.7 + sin(p.x * 1.9 + flowTime * 0.73) + flowTime * 1.4);
                float waveB = sin(p.x * 3.1 - p.z * 1.4 - flowTime * 1.1);
                float waveC = sin((p.x + p.y + p.z) * 2.2 + flowTime * 1.8);

                float combined = waveA * 0.48 + waveB * 0.32 + waveC * 0.2;
                combined = combined * 0.5 + 0.5;

                return pow(saturate(combined), _PatternContrast);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;

                float heat01 = Heat01();
                float pattern = FlowPattern(input.positionOS.xyz, _FlowTime);
                float displacement = (pattern - 0.5) * _Distortion * heat01;
                float3 displacedPositionOS = input.positionOS.xyz + input.normalOS * displacement;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(displacedPositionOS);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.positionOS = input.positionOS.xyz;
                output.shadowCoord = GetShadowCoord(positionInputs);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float heat01 = Heat01();
                float pattern = FlowPattern(input.positionOS, _FlowTime);
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));

                Light mainLight = GetMainLight(input.shadowCoord);

                // Half-Lambert keeps the unlit side softly visible instead of fully black.
                float halfLambert = saturate(dot(normalWS, mainLight.direction) * 0.5 + 0.5);
                halfLambert *= halfLambert;

                float mainAttenuation = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                half3 directLight = mainLight.color * halfLambert * mainAttenuation;
                half3 ambientLight = SampleSH(normalWS) * 0.45;

                half3 heatColor = lerp(_LowHeatColor.rgb, _HighHeatColor.rgb, heat01);
                half3 movingColor = lerp(heatColor * 0.55, _VeinColor.rgb, pattern);
                half3 litColor = movingColor * (ambientLight + directLight);

                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirectionWS)), _FresnelPower);
                half3 emission = movingColor * _EmissionStrength * (0.2 + heat01 * 0.8) * (0.35 + pattern);
                half3 finalColor = litColor + emission + _VeinColor.rgb * fresnel * 0.12;

                finalColor = MixFog(finalColor, input.fogFactor);

                float alpha = saturate(_BaseAlpha + fresnel * _FresnelAlpha + pattern * 0.08);
                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
