Shader "Capstone/World/Forest Brown Outline"
{
 Properties
 {
  _Color("Outline Colour",Color)=(0.075,0.035,0.018,1)
  _Width("Outline Width In Pixels",Range(0,3))=1.25
 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry-10" }
  Pass
  {
   Name "ForestOutline"
   Tags { "LightMode"="UniversalForward" }
   Cull Front ZWrite On ZTest LEqual
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
    half4 _Color;
    float _Width;
   CBUFFER_END
   struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
   struct Varyings {float4 positionCS:SV_POSITION;half fog:TEXCOORD0;};
   Varyings Vert(Attributes input)
   {
    Varyings output;
    float3 world=TransformObjectToWorld(input.positionOS.xyz);
    output.positionCS=TransformWorldToHClip(world);
    float3 normal=TransformObjectToWorldNormal(input.normalOS);
    float2 direction=mul((float3x3)UNITY_MATRIX_V,normal).xy;
    direction/=max(length(direction),1e-5);
    output.positionCS.xy+=direction*(_Width*2/_ScreenParams.xy)*output.positionCS.w;
    output.fog=ComputeFogFactor(output.positionCS.z);
    return output;
   }
   half4 Frag(Varyings input):SV_Target{return half4(MixFog(_Color.rgb,input.fog),1);}
   ENDHLSL
  }
 }
}
