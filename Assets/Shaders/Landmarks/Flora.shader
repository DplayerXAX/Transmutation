Shader "Capstone/Landmarks/Flora"
{
    // Abstract flowers, leaves and vines growing on the Bone Kiln. Colour comes from the mesh's vertex
    // colours; soft two-sided light, a pale rim, and thin colour fringes along the edges of each part
    // (matching the bones' marbled fringes). Petals and leaves are single sheets, so both sides are drawn.
    Properties
    {
        _ShadeAmount("Shading", Range(0, 1)) = 0.35
        _RimStrength("Rim", Range(0, 1)) = 0.3
        _FringeColor("Edge Fringe", Color) = (0.25, 0.85, 0.95, 1)
        _FringeStrength("Edge Fringe Strength", Range(0, 1)) = 0.35
        _Sway("Sway (metres)", Float) = 0.015
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half _ShadeAmount;
            half _RimStrength;
            half4 _FringeColor;
            half _FringeStrength;
            float _Sway;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                float2 uv : TEXCOORD2;
                float fog : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                // uv.x = 0 at the stem, 1 at the tip: tips sway a little.
                positionWS += float3(sin(_Time.y * 1.3 + positionWS.x * 0.7), 0.0, cos(_Time.y * 1.1 + positionWS.z * 0.7)) * _Sway * input.uv.x;
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.color = input.color;
                output.uv = input.uv;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS) * (frontFace ? 1.0 : -1.0);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half ndl = saturate(dot(normalWS, light.direction) * 0.5 + 0.5) * lerp(0.6, 1.0, light.shadowAttenuation);
                half3 colour = input.color.rgb * lerp(1.0 - _ShadeAmount, 1.0, ndl);
                half3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = pow(1.0 - saturate(abs(dot(normalWS, viewDir))), 3.0) * _RimStrength;
                colour += rim;
                // Thin coloured fringe along the sides of each sheet (uv.y = 0 or 1 at the edges).
                half edge = saturate(1.0 - min(input.uv.y, 1.0 - input.uv.y) * 12.0);
                colour = lerp(colour, _FringeColor.rgb, edge * _FringeStrength);
                colour = MixFog(colour, input.fog);
                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
