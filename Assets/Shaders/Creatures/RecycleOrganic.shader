Shader "Transmutation/Recycle Organic"
{
    Properties
    {
        _Bloom("Bloom",Range(0,1))=1 _Life("Remaining life",Range(0,1))=1
        _Clock("Simulation clock",Float)=0 _Pulse("Feeding and production pulse",Float)=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float _Bloom,_Life,_Clock,_Pulse;
        CBUFFER_END
        struct Attributes { float4 positionOS:POSITION;float3 normalOS:NORMAL;half4 color:COLOR;float2 uv:TEXCOORD0;float4 origin:TEXCOORD1;float4 timing:TEXCOORD2; };
        struct Varyings { float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half4 color:COLOR;float3 uv:TEXCOORD2; };
        Varyings Vert(Attributes v)
        {
            Varyings o;
            float progress=v.timing.y>0.5?saturate((_Clock-v.origin.w)/max(0.01,v.timing.x)):
                saturate((_Bloom-v.origin.w)/max(0.01,1-v.origin.w));
            progress=progress*progress*(3-2*progress);
            if(v.timing.y<0.5)progress*=lerp(0.65,1,saturate(_Life*5));
            float3 p=lerp(v.origin.xyz,v.positionOS.xyz,progress);
            o.positionWS=TransformObjectToWorld(p);o.positionCS=TransformWorldToHClip(o.positionWS);
            o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.color=v.color;o.uv=float3(v.uv,v.timing.z);
            return o;
        }
        ENDHLSL
        Pass
        {
            Name "OrganicForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            half4 Frag(Varyings i,bool front:SV_IsFrontFace):SV_Target
            {
                half3 normal=normalize(i.normalWS)*(front?1:-1);
                Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 colour=i.color.rgb;
                if(i.uv.z>0.5)
                {
                    float central=exp(-abs(i.uv.y)*65);
                    float branches=1-smoothstep(0.015,0.055,abs(frac(i.uv.x*8+abs(i.uv.y)*2.6)-0.5));
                    float vein=max(central,branches*saturate(1-abs(i.uv.y))*0.75);
                    colour=lerp(colour,colour*0.20,vein*0.75);
                }
                half gray=dot(colour,half3(0.2126,0.7152,0.0722));
                colour=lerp(gray.xxx*0.8+0.13,colour,saturate(_Life));
                half diffuse=saturate(dot(normal,light.direction));
                half3 lit=colour*(SampleSH(normal)+0.12+light.color*(diffuse*0.8+0.12)*light.shadowAttenuation);
                float wave=pow(saturate(sin(i.uv.x*5-_Clock*3)),8)*_Pulse;
                half3 glow=colour*i.color.a*(0.15+wave*1.5)*_Life*_Life;
                return half4(lit+glow,1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment ShadowFrag
            half4 ShadowFrag(Varyings i):SV_Target {return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthFrag
            half4 DepthFrag(Varyings i):SV_Target {return 0;}
            ENDHLSL
        }
    }
}
