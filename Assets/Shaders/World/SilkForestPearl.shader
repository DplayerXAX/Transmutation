Shader "Capstone/Silk Forest/Pearlescent Silk"
{
    Properties
    {
        [MainColor] _BaseColor("Pearl body", Color) = (0.67,0.78,0.74,1)
        _ShadowColor("Deep organic shadow", Color) = (0.085,0.105,0.145,1)
        _Iridescence("Oil film iridescence", Range(0,1)) = 0.6
        _Smoothness("Wet gloss", Range(0,1)) = 0.84
        _Emission("Subtle bioluminescence", Range(0,1)) = 0.06
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
                half _Iridescence, _Smoothness, _Emission;
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
                half3 base=_BaseColor.rgb*i.color.rgb;
                half3 col=lerp(_ShadowColor.rgb,base,0.24+diffuse*0.76);
                col+=SampleSH(n)*base*0.42;
                col=lerp(col,col*(0.5+rainbow*1.1),_Iridescence*(0.22+pow(1-facing,1.7)*0.78));
                half spec=pow(saturate(dot(n,normalize(v+light.direction))),lerp(20,160,_Smoothness));
                col+=light.color*spec*light.shadowAttenuation*0.8;
                col+=rainbow*pow(1-facing,3)*(_Iridescence*0.25+_Emission);
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
