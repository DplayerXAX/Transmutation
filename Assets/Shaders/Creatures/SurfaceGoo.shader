Shader "Transmutation/Surface Goo"
{
    Properties { _BaseColor("Colour", Color) = (0.12,0.35,0.85,1) _Spread("Spread", Range(0,1)) = 1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+2" }
        Pass
        {
            Name "SurfaceGoo"
            Tags { "LightMode"="UniversalForward" }
            Offset -1,-1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor; float _Spread;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float radial:TEXCOORD2; };
            Varyings Vert(Attributes v)
            {
                Varyings o; o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS); o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                o.radial=v.uv.x; return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                clip(_Spread*1.12+0.02-i.radial);
                half3 n=normalize(i.normalWS), view=GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half diffuse=saturate(dot(n,light.direction));
                half spec=pow(saturate(dot(n,normalize(light.direction+view))),64);
                half rim=pow(1-saturate(dot(n,view)),3);
                half3 colour=_BaseColor.rgb*(SampleSH(n)+light.color*diffuse*light.shadowAttenuation+0.12);
                return half4(colour+light.color*spec*0.55+_BaseColor.rgb*rim*0.3,1);
            }
            ENDHLSL
        }
    }
}
