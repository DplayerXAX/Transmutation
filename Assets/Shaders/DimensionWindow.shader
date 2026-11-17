Shader "Transmutation/Dimension Window" {
SubShader {
 Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
 Pass {
 Tags { "LightMode"="UniversalForward" }
 Cull Back ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct V { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
 V vert(float4 p:POSITION,float2 uv:TEXCOORD0){V o;o.pos=TransformObjectToHClip(p.xyz);o.uv=uv;return o;}
 half4 frag(V i):SV_Target {float2 e=min(i.uv,1-i.uv);float edge=1-smoothstep(.003,.016,min(e.x,e.y));return half4(.9,.96,1,edge*.35);}
 ENDHLSL
 }
 Pass {
 Name "WindowEntry" Tags { "LightMode"="DimensionalEntry" }
 Cull Back ZTest LEqual ZWrite Off Blend One One BlendOp Min
 Stencil { Ref 1 ReadMask 1 WriteMask 1 Comp Always Pass Replace }
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct V { float4 pos:SV_POSITION; float depth:TEXCOORD0; };
 V vert(float4 p:POSITION){V o;o.pos=TransformObjectToHClip(p.xyz);o.depth=-TransformWorldToView(TransformObjectToWorld(p.xyz)).z;return o;}
 float frag(V i):SV_Target{return i.depth;}
 ENDHLSL
 }
 Pass {
 Name "WhiteInterior" Tags { "LightMode"="DimensionalWhite" }
 Cull Off ZTest Always ZWrite Off
 Stencil { Ref 1 ReadMask 1 WriteMask 0 Comp Equal }
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 float4 vert(uint id:SV_VertexID):SV_POSITION { return GetFullScreenTriangleVertexPosition(id); }
 half4 frag():SV_Target { return half4(1,1,1,1); }
 ENDHLSL
 }
}
}
