Shader "Transmutation/Asura Blood Shell"
{
    Properties
    {
        _BaseColor ("Blood color", Color) = (0.96, 0.78, 0.3, 0.27)
        _Gap ("Gap in metres", Range(0, 0.02)) = 0.0035
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Gap;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz) + output.normalWS * _Gap;
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // No shell within 16 cm of the eye, including when an animated arm crosses it.
                clip(distance(input.positionWS, _WorldSpaceCameraPos) - 0.16);
                float3 view = normalize(_WorldSpaceCameraPos - input.positionWS);
                half rim = pow(1 - saturate(dot(normalize(input.normalWS), view)), 2);
                half3 color = lerp(_BaseColor.rgb, saturate(_BaseColor.rgb + 0.28), rim * 0.65);
                return half4(color, _BaseColor.a + rim * 0.18);
            }
            ENDHLSL
        }
    }
}
