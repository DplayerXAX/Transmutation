Shader "Capstone/World/ErosiveWater"
{
    Properties
    {
        _Color("Still water", Color) = (0.035,0.17,0.2,1)
        _FlowColor("Flowing water", Color) = (0.15,0.7,0.78,1)
        _LineColor("Flow lines", Color) = (0.85,1,1,1)
        _FlowVelocity("Velocity", Vector) = (0,0,0,0)
        _ContactPoint("Contact", Vector) = (0,0,0,0)
        _ContactNormal("Contact normal", Vector) = (0,0,0,0)
        _ErosionStrength("Erosion", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Shaders/World/LineWorldLighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color, _FlowColor, _LineColor;
            CBUFFER_END
            UNITY_INSTANCING_BUFFER_START(Fluid)
                UNITY_DEFINE_INSTANCED_PROP(float4, _FlowVelocity)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ContactPoint)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ContactNormal)
                UNITY_DEFINE_INSTANCED_PROP(float, _ErosionStrength)
            UNITY_INSTANCING_BUFFER_END(Fluid)
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float fog:TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V Vert(A input)
            {
                V o; UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input,o);
                o.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(input.normalOS);
                o.fog=ComputeFogFactor(o.positionCS.z); return o;
            }
            half4 Frag(V i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 velocity=UNITY_ACCESS_INSTANCED_PROP(Fluid,_FlowVelocity).xyz;
                float speed=length(velocity);
                float3 direction=speed>0.01?velocity/speed:float3(0,0,1);
                float3 normal=normalize(i.normalWS);
                float3 view=GetWorldSpaceNormalizeViewDir(i.positionWS);
                float ao;
                half light=LineWorldLight(i.positionWS,normal,GetNormalizedScreenSpaceUV(i.positionCS),1,float3(.3,1,.2),.5,3,ao);
                half3 color=lerp(_Color.rgb,_FlowColor.rgb,saturate(speed*.2))*(.45+.55*light);
                float wave=dot(i.positionWS,direction)*9-_Time.y*speed*9;
                half stripe=LineWorldStripe(wave+sin(dot(i.positionWS,float3(2,3,1)))*.25,1);
                float3 contact=UNITY_ACCESS_INSTANCED_PROP(Fluid,_ContactPoint).xyz;
                float3 contactNormal=UNITY_ACCESS_INSTANCED_PROP(Fluid,_ContactNormal).xyz;
                float strength=UNITY_ACCESS_INSTANCED_PROP(Fluid,_ErosionStrength);
                float3 offset=i.positionWS-contact;
                float plane=dot(offset,contactNormal);
                float distance=length(offset-contactNormal*plane);
                half ripple=LineWorldStripe(distance*10-_Time.y*3,1)*saturate(1-abs(plane)*8)*length(contactNormal);
                half rim=pow(1-saturate(dot(normal,view)),3);
                color=lerp(color,_LineColor.rgb,saturate(stripe*.35+rim*.55+ripple*saturate(strength)));
                return half4(MixFog(color,i.fog),1);
            }
            ENDHLSL
        }
    }
}
