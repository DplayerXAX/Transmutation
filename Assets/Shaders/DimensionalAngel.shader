Shader "Transmutation/Dimensional Angel" {
Properties { _AngelColor ("Body color",Color)=(1,1,1,.4) }
SubShader {
 Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
 Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 TEXTURE2D_X_FLOAT(_AngelWindowDepth);
 float4 _AngelColor;
 struct V {float4 pos:SV_POSITION;float3 normal:TEXCOORD0;float3 world:TEXCOORD1;};
 V vert(float4 p:POSITION,float3 n:NORMAL){V o;o.world=TransformObjectToWorld(p.xyz);o.pos=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(n);return o;}
 half4 body(V i){float rim=1-abs(dot(normalize(i.normal),GetWorldSpaceNormalizeViewDir(i.world)));return half4(_AngelColor.rgb*(.68+.7*rim),_AngelColor.a);}
 half4 window(V i):SV_Target {float entry=LOAD_TEXTURE2D_X(_AngelWindowDepth,uint2(i.pos.xy)).r;clip(-TransformWorldToView(i.world).z-entry-.001);return body(i);}
 half4 honey(V i):SV_Target{return body(i);}
 ENDHLSL
 Pass { Name "Window" Tags { "LightMode"="AngelWindow" }
 Stencil { Ref 1 ReadMask 1 WriteMask 0 Comp Equal }
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment window
 ENDHLSL
 }
 Pass { Name "Honey" Tags { "LightMode"="AngelHoney" }
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment honey
 ENDHLSL
 }
 Pass { Name "PhysicalWindow" Tags { "LightMode"="AngelPhysicalWindow" }
 Stencil { Ref 1 ReadMask 1 WriteMask 0 Comp Equal }
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment honey
 ENDHLSL
 }
}
}
