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
        _PatternContrast("Pattern Sharpness", Range(0.25, 6)) = 4
        _Distortion("Surface Distortion", Range(0, 0.2)) = 0.035
        _EmissionStrength("Emission Strength", Range(0, 5)) = 1.3
        [Toggle] _UseLighting("Use Lighting", Float) = 1

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
                float _UseLighting;
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

                // Slowly deform the coordinates first, producing curled streams rather
                // than regular surface waves. The two scales resemble ink folding in water.
                float3 warped = p;
                warped.x += sin(p.y * 1.35 + p.z * 0.75 + flowTime * 0.62) * 0.85;
                warped.y += sin(p.z * 1.15 - p.x * 0.55 - flowTime * 0.47) * 0.70;
                warped.z += sin(p.x * 1.25 + p.y * 0.65 + flowTime * 0.54) * 0.80;

                float broadInk =
                    sin(warped.x * 2.25 + sin(warped.y * 1.55 + flowTime * 0.35) * 1.45) * 0.62 +
                    sin(warped.y * 2.05 - warped.z * 1.20 - flowTime * 0.72) * 0.26 +
                    sin((warped.x + warped.z) * 3.70 + flowTime * 0.91) * 0.12;

                float fineInk = sin(
                    warped.x * 5.2 - warped.y * 2.1 + warped.z * 3.4 +
                    sin(warped.z * 2.8 - flowTime * 0.8) * 1.2
                );

                float field = broadInk + fineInk * 0.16;
                float sharpness01 = saturate((_PatternContrast - 0.25) / 5.75);
                float softEdge = lerp(0.32, 0.035, sharpness01);
                float inkBody = smoothstep(-softEdge, softEdge, field);

                // A narrow bright filament gives the body of ink a crisp curling edge.
                float filamentWidth = lerp(0.24, 0.07, sharpness01);
                float filament = 1.0 - smoothstep(0.0, filamentWidth, abs(field));

                return saturate(inkBody * 0.82 + filament * 0.38);
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
                half3 receivedLight = ambientLight + directLight;
                half3 surfaceLighting = lerp(half3(1.0, 1.0, 1.0), receivedLight, saturate(_UseLighting));
                half3 litColor = movingColor * surfaceLighting;

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
