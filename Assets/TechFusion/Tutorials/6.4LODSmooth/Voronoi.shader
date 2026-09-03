Shader "Tutorial604/Voronoi"
{
    Properties
    {
        _NumClumpTypes("Clump Types", Range(1, 40)) = 4
        _NumClumps("Clump Count", Range(1, 100)) = 40
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _NumClumpTypes;
            float _NumClumps;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = v.uv;
                return o;
            }

            float2 Hash22(float2 p)
            {
                float3 a = frac(float3(p.x, p.y, p.x) * float3(123.34, 234.34, 345.65));
                a += dot(a, a + 34.45);
                return frac(float2(a.x * a.y, a.y * a.z));
            }

            float4 frag (v2f i) : SV_Target
            {
                float minDist = 100000.0;
                float id = 0.0;
                float2 clumpCentre = float2(0.0, 0.0);

                int clumpLimit = min(100, (int)_NumClumps);

                for (int j = 1; j < clumpLimit; j++)
                {
                    float2 jj = float2(float(j), float(j));
                    float2 p = Hash22(jj);
                    float d = distance(p, i.uv);

                    if (d < minDist)
                    {
                        minDist = d;
                        id = fmod(float(j), _NumClumpTypes);
                        clumpCentre = p;
                    }
                }

                float3 col = float3(id, clumpCentre.x, clumpCentre.y);
                return float4(col.xyz, 0.0);
            }
            ENDHLSL
        }
    }
}