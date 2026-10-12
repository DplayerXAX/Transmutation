Shader "Capstone/World/ErosionRipple"
{
    Properties { _Color("Color", Color)=(0.4,0.9,1,0.7) _Age("Age", Float)=0 _Strength("Strength",Float)=1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1,-1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; float _Age,_Strength;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            V Vert(A i) { V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;return o; }
            half4 Frag(V i):SV_Target
            {
                float radius=length(i.uv-.5)*2;
                float ring=1-smoothstep(.025,.06,abs(radius-(_Age*.7+.1)));
                return half4(_Color.rgb,_Color.a*ring*saturate(1-_Age)*saturate(_Strength));
            }
            ENDHLSL
        }
    }
}
