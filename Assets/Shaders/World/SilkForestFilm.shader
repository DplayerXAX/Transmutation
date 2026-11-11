Shader "Capstone/Silk Forest/Thin Film"
{
    Properties
    {
        [MainColor] _BaseColor("Thin translucent slime", Color) = (0.42,0.77,0.7,0.18)
        _Iridescence("Film interference", Range(0,1)) = 0.8
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Iridescence;
            CBUFFER_END
            struct Input { float4 p:POSITION; float3 n:NORMAL; half4 color:COLOR; };
            struct Varying { float4 p:SV_POSITION; float3 ws:TEXCOORD0; half3 n:TEXCOORD1; half4 color:COLOR; half fog:TEXCOORD2; };
            Varying Vert(Input v)
            {
                Varying o; o.ws=TransformObjectToWorld(v.p.xyz); o.p=TransformWorldToHClip(o.ws);
                o.n=TransformObjectToWorldNormal(v.n); o.color=v.color; o.fog=ComputeFogFactor(o.p.z); return o;
            }
            half4 Frag(Varying i):SV_Target
            {
                half facing=abs(dot(normalize(i.n),GetWorldSpaceNormalizeViewDir(i.ws)));
                half3 rainbow=0.5+0.5*cos(half3(0,2,4)+(1-facing)*10+i.ws.y*0.55+i.ws.x*0.27);
                half veins=pow(0.5+0.5*sin(i.ws.y*24+i.ws.x*9+sin(i.ws.z*14)),12);
                half3 col=lerp(_BaseColor.rgb,rainbow,_Iridescence*0.78)+veins*0.08;
                half alpha=(_BaseColor.a+pow(1-facing,2)*0.26)*i.color.a;
                return half4(MixFog(col,i.fog),saturate(alpha));
            }
            ENDHLSL
        }
    }
}
