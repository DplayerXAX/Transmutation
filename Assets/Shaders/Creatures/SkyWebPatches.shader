Shader "Capstone/Creatures/SkyWebPatches"
{
    // Intention: sparse double-sided silk membranes inside irregular cells.
    // Mesh normals and view direction shape the transparency; no textures or deformation.
    Properties
    {
        _BaseColor("Patch Color", Color) = (0.65, 0.79, 0.9, 1)
        _Opacity("Opacity", Range(0, 0.5)) = 0.12
        _RimStrength("Grazing Opacity", Range(0, 0.5)) = 0.12
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "Silk Patches"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Opacity;
                half _RimStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            // World-space normal/view direction -> double-sided opacity [0,1] -> composition.
            half CreatePatchOpacity(half3 normalWS, float3 positionWS)
            {
                half3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
                half rim = 1.0h - abs(dot(normalize(normalWS), viewDirectionWS));
                return saturate(_Opacity + rim * rim * _RimStrength);
            }

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
                half opacity = CreatePatchOpacity(input.normalWS, input.positionWS);
                return half4(MixFog(_BaseColor.rgb, input.fogFactor), opacity);
            }
            ENDHLSL
        }
    }
}
