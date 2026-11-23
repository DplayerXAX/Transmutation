// Thin glowing line (the baton's sounding thread): additive vertex colour, not fogged.
Shader "Capstone/Cave/GlowLine"
{
    Properties
    {
        _Glow("Glow Amount", Range(0, 4)) = 2
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("Depth Test", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        ZTest [_ZTest]
        Cull Off
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _Glow;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; };
            V vert(A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.color = i.color; return o; }
            half4 frag(V i) : SV_Target { return half4(i.color.rgb * _Glow, 0); }
            ENDHLSL
        }
    }
}
