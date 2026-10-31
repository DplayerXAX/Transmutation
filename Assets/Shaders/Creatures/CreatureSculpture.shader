Shader "Capstone/Creature/Sculpture"
{
    Properties { _BaseColor("Tint",Color)=(1,1,1,1) _EmissionColor("Emission",Color)=(0,0,0,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _EmissionColor;
            CBUFFER_END
            struct A { float4 vertex:POSITION; float3 normal:NORMAL; half4 color:COLOR; };
            struct V { float4 position:SV_POSITION; float3 world:TEXCOORD0; half3 normal:TEXCOORD1; half4 color:COLOR; };
            V Vert(A i) { V o; o.position=TransformObjectToHClip(i.vertex.xyz); o.world=TransformObjectToWorld(i.vertex.xyz); o.normal=TransformObjectToWorldNormal(i.normal); o.color=i.color; return o; }
            half4 Frag(V i):SV_Target
            {
                half3 n=normalize(i.normal), view=normalize(GetWorldSpaceViewDir(i.world));
                Light light=GetMainLight();
                half shade=.55+.45*smoothstep(-.2,.6,dot(n,light.direction));
                half rim=pow(1-saturate(abs(dot(n,view))),3);
                half lines=smoothstep(.91,.98,abs(sin((i.world.y+i.world.x*.17)*24)))*.12;
                half3 color=i.color.rgb*_BaseColor.rgb*(shade-lines);
                color=lerp(color,half3(.76,.9,.84),rim*.38)+_EmissionColor.rgb;
                return half4(color,1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
