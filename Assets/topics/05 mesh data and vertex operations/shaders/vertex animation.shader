Shader "shader lab/week 5/vertex animation" {
    Properties {
        _frequency ("frequency", Range(2, 100)) = 15.5
        _displacement ("displacement", Range(0, 0.1)) = 0.05
    }

    SubShader {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float _frequency;
            float _displacement;
            CBUFFER_END

            struct MeshData {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Interpolators {
                float4 vertex : SV_POSITION;
                float4 uv : TEXCOORD0;
                float disp : TEXCOORD1;
            };

            Interpolators vert (MeshData v) {
                Interpolators o;
                
                float disp = sin(((v.uv.x + v.uv.y) * _frequency) + _Time.z) * 0.5 + 0.5;
                
                o.disp = disp;
                
                v.vertex.xyz += v.normal * disp * _displacement;

                o.vertex = TransformObjectToHClip(v.vertex);
                o.uv.xy = v.uv;
                
                return o;
            }

            float4 frag (Interpolators i) : SV_Target {
                return float4(i.uv.x, i.disp * 0.5, i.uv.y, 1.0);
            }

ENDHLSL
        }
    }
}