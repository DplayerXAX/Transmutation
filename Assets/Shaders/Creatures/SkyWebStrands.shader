Shader "Capstone/Creatures/SkyWebStrands"
{
    // Intention: opaque black silk outlining D12 cages and their connecting strands.
    // Vertex color distinguishes supports; static geometry, one inexpensive forward pass.
    Properties
    {
        _SilkColor("Silk Color", Color) = (0, 0, 0, 1)
        _SupportColor("Support Color", Color) = (0, 0, 0, 1)
        _ShadeStrength("Shade Strength", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "Silk"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SilkColor;
                half4 _SupportColor;
                half _ShadeStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                half supportMask : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            // Vertex-color luminance -> support mask [0,1] -> color composition.
            half CreateSupportMask(half3 vertexColor)
            {
                return 1.0h - step(0.5h, vertexColor.r);
            }

            // World-space surface normal -> directional shade [0,1] -> final silk color.
            half3 CompositeSilkColor(half3 normalWS, half supportMask)
            {
                Light light = GetMainLight();
                half shade = saturate(dot(normalize(normalWS), light.direction));
                half3 color = lerp(_SilkColor.rgb, _SupportColor.rgb, supportMask);
                return color * lerp(1.0h - _ShadeStrength, 1.0h, shade);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.supportMask = CreateSupportMask(input.color.rgb);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 color = CompositeSilkColor(input.normalWS, input.supportMask);
                return half4(MixFog(color, input.fogFactor), 1.0h);
            }
            ENDHLSL
        }
    }
}
