Shader "Transmutation/Webbed Player Skin"
{
    Properties
    {
        _BaseColor("Green", Color) = (0.08,0.8,0.55,0.38)
        _BlueColor("Blue", Color) = (0.025,0.42,0.72,0.38)
        _Opacity("Opacity", Range(0,1)) = 0.38
        [HDR] _EmissionColor("Player fluorescence", Color) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _BlueColor, _EmissionColor;
            float _Opacity;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; };
            V Vert(A v)
            {
                V o; o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS); o.uv=v.uv; return o;
            }
            half4 Frag(V i):SV_Target
            {
                float3 n=normalize(i.normalWS), v=GetWorldSpaceNormalizeViewDir(i.positionWS);
                float rim=pow(1-saturate(dot(n,v)),2.5);
                Light light=GetMainLight();
                float gradient=saturate(i.uv.y*0.8+i.uv.x*0.3);
                half3 color=lerp(_BlueColor.rgb,_BaseColor.rgb,gradient);
                color*=0.55+SampleSH(n)+light.color*(0.35*saturate(dot(n,light.direction)));
                color+=float3(0.07,0.65,0.7)*rim*0.6;
                float spec=pow(saturate(dot(n,normalize(v+light.direction))),70);
                color += _EmissionColor.rgb * lerp(_BlueColor.rgb,_BaseColor.rgb,gradient) * (0.65 + 0.35 * rim);
                return half4(color+spec*light.color*0.6,saturate(_Opacity+rim*0.28));
            }
            ENDHLSL
        }
    }
}
