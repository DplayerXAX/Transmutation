// Soft additive halo round a glowing creature: a camera-facing quad, NOT fogged, so lanterns read from far away.
// Depth tested, so rock in front still hides it.
Shader "Capstone/Cave/GlowHalo"
{
    Properties
    {
        [HDR] _Color("Halo Color", Color) = (0.2, 1, 1, 1)
        _Size("Size (m)", Float) = 2
        _Glow("Glow Amount", Range(0, 3)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Size;
                half _Glow;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A i)
            {
                V o;
                float3 centre = TransformObjectToWorld(float3(0, 0, 0));
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                float2 c = i.uv * 2 - 1;
                float3 p = centre + (right * c.x + up * c.y) * _Size * 0.5;
                // Pull the quad a little towards the camera so it is not cut by the body it surrounds.
                p += normalize(GetCameraPositionWS() - centre) * _Size * 0.2;
                o.positionCS = TransformWorldToHClip(p);
                o.uv = c;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half r = saturate(1 - length(i.uv));
                half a = r * r * (0.6 + 0.4 * r);
                return half4(_Color.rgb * a * _Glow, 0);
            }
            ENDHLSL
        }
    }
}
