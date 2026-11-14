Shader "Capstone/World/BasiltVine"
{
    Properties
    {
        _BaseColor("Bark", Color) = (0.15,0.23,0.16,1)
        _FiberColor("Pale Fibres", Color) = (0.43,0.54,0.34,1)
        _ShadowColor("Deep Bark", Color) = (0.025,0.045,0.029,1)
        _GrainScale("Bark Grain", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _FiberColor, _ShadowColor;
            float _GrainScale;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct V { float4 positionCS:SV_POSITION; float3 p:TEXCOORD0; float3 n:TEXCOORD1; half fog:TEXCOORD2; };
            V Vert(A i)
            {
                V o; o.p=TransformObjectToWorld(i.positionOS.xyz);o.n=TransformObjectToWorldNormal(i.normalOS);
                o.positionCS=TransformWorldToHClip(o.p);o.fog=ComputeFogFactor(o.positionCS.z);return o;
            }
            half4 Frag(V i):SV_Target
            {
                float3 n=normalize(i.n),p=i.p;
                float3 w=pow(abs(n),4);w/=max(dot(w,1),0.001);
                float3 q=float3(p.z+p.y*2.1,p.x*0.71+p.z*0.23,p.x+p.y*1.8)*_GrainScale;
                q+=sin(q*0.17+p.y*0.24)*1.6;
                float3 grain=abs(sin(q*6.283));
                float3 footprint=max(fwidth(grain),0.01);
                float3 fibers=(1-smoothstep(0.025,0.10+footprint,grain))*(1-smoothstep(0.3,0.8,footprint));
                float lines=dot(fibers,w);
                float mottling=sin(p.x*0.4+p.z*0.33)*sin(p.y*0.5-p.z*0.16)*0.07;
                Light sun=GetMainLight(TransformWorldToShadowCoord(p));
                float light=saturate(dot(n,sun.direction))*sun.shadowAttenuation;
                float shade=0.34+0.66*floor(light*3+0.5)/3;
                half3 bark=lerp(_BaseColor.rgb,_FiberColor.rgb,lines*0.6+saturate(n.y)*0.16+mottling);
                half3 color=lerp(_ShadowColor.rgb,bark,shade);
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
