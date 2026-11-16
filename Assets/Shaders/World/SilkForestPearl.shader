Shader "Capstone/Silk Forest/Pearlescent Silk"
{
    Properties
    {
        [MainColor] _BaseColor("黏膜主体颜色", Color) = (0.67,0.78,0.74,1)
        _ShadowColor("组织阴影颜色", Color) = (0.085,0.105,0.145,1)
        _Iridescence("薄膜虹彩强度", Range(0,1)) = 0.6
        _Smoothness("湿润光滑程度", Range(0,1)) = 0.56
        _Emission("微弱生物发光", Range(0,1)) = 0.06
        _SilkSheen("丝质柔光强度", Range(0,1)) = 0.45
        _FibreStrength("细丝纹理强度", Range(0,1)) = 0.2
        _VertexTint("组织顶点染色", Range(0,1)) = 0.1
        _SilkStyle("丝质柔光混合（0为珠光）", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        Pass
        {
            Name "SilkForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _ShadowColor;
                half _Iridescence, _Smoothness, _Emission, _SilkSheen, _FibreStrength, _VertexTint, _SilkStyle;
            CBUFFER_END
            struct Input { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; };
            struct Varying { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; half4 color:COLOR; half fog:TEXCOORD2; };
            Varying Vert(Input v)
            {
                Varying o; o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS); o.color=v.color;
                o.fog=ComputeFogFactor(o.positionCS.z); return o;
            }
            half4 Frag(Varying i, FRONT_FACE_TYPE front:FRONT_FACE_SEMANTIC):SV_Target
            {
                half3 n=normalize(i.normalWS)*IS_FRONT_VFACE(front,1,-1);
                half3 v=GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half facing=saturate(dot(n,v));
                half diffuse=saturate(dot(n,light.direction))*light.shadowAttenuation;
                half3 phase=half3(0.1,2.2,4.3);
                half3 rainbow=0.5+0.5*cos(phase+(1-facing)*8.0+i.positionWS.y*0.24);
                half3 base=_BaseColor.rgb*lerp(half3(1,1,1),i.color.rgb,_VertexTint);
                // Broad wrap lighting keeps thin silk tissue luminous in shadow.
                half wrap=saturate((dot(n,light.direction)+0.55)/1.55)*lerp(0.6,1,light.shadowAttenuation);
                half3 col=lerp(_ShadowColor.rgb,base,0.38+wrap*0.62);
                col+=SampleSH(n)*base*0.42;
                col=lerp(col,col*(0.5+rainbow*1.1),_Iridescence*(0.22+pow(1-facing,1.7)*0.78));
                half spec=pow(saturate(dot(n,normalize(v+light.direction))),lerp(20,160,_Smoothness));
                float fibrePhase=dot(i.positionWS,float3(150,2,98))+sin(i.positionWS.y*8+i.positionWS.x*13)*2;
                half fibres=sin(fibrePhase)/(1+fwidth(fibrePhase)*2);
                col*=1-_FibreStrength*0.22+fibres*_FibreStrength*0.22;
                half3 tangent=half3(0,1,0)-n*n.y;
                if(dot(tangent,tangent)<0.001)tangent=half3(1,0,0)-n*n.x;
                tangent=normalize(tangent);
                half along=dot(tangent,normalize(v+light.direction));
                half sheen=pow(saturate(1-along*along),24)*(0.3+0.7*pow(1-facing,2));
                col+=light.color*(spec*0.12+sheen*_SilkSheen*(0.7+fibres*0.3))*lerp(0.5,1,light.shadowAttenuation);
                col+=rainbow*pow(1-facing,3)*(_Iridescence*0.25+_Emission);
                // Preserve the earlier pearlescent crown finish as an explicit style.
                half3 pearlBase=_BaseColor.rgb*i.color.rgb;
                half3 pearl=lerp(_ShadowColor.rgb,pearlBase,0.24+diffuse*0.76);
                pearl+=SampleSH(n)*pearlBase*0.42;
                pearl=lerp(pearl,pearl*(0.5+rainbow*1.1),_Iridescence*(0.22+pow(1-facing,1.7)*0.78));
                pearl*=0.96+fibres*0.04;
                pearl+=light.color*spec*light.shadowAttenuation*0.45;
                pearl+=rainbow*pow(1-facing,3)*(_Iridescence*0.25+_Emission);
                col=lerp(pearl,col,_SilkStyle);
                return half4(MixFog(col,i.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection, _LightPosition;
            float4 ShadowVert(float4 p:POSITION,float3 n:NORMAL):SV_POSITION
            {
                float3 ws=TransformObjectToWorld(p.xyz);
                float3 normal=TransformObjectToWorldNormal(n);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir=normalize(_LightPosition-ws);
                #else
                    float3 lightDir=_LightDirection;
                #endif
                float4 cs=TransformWorldToHClip(ApplyShadowBias(ws,normal,lightDir));
                #if UNITY_REVERSED_Z
                    cs.z=min(cs.z,UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z=max(cs.z,UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 ShadowFrag():SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 DepthVert(float4 p:POSITION):SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half4 DepthFrag():SV_Target { return 0; }
            ENDHLSL
        }
    }
}
