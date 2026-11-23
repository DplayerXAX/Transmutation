// Glowing creature parts in the dark tunnels. Unlit, HDR colour, brighter at the rim, and NOT fogged,
// so a glow can be seen far down a tunnel where black fog hides the rock.
Shader "Capstone/Cave/Glow"
{
    Properties
    {
        [HDR] _Color("Glow Color", Color) = (0.2, 1, 1, 1)
        _Core("Core Brightness", Range(0, 2)) = 0.6
        _Rim("Rim Brightness", Range(0, 4)) = 1.6
        _Glow("Glow Amount", Range(0, 3)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Core, _Rim, _Glow;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };
            V vert(A i)
            {
                V o;
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(p);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half f = 1 - saturate(abs(dot(normalize(i.normalWS), normalize(i.viewWS))));
                return half4(_Color.rgb * (_Core + _Rim * f * f) * _Glow, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 vert(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
