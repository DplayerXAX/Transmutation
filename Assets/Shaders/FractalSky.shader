Shader "Capstone/Skybox/Morphing Fractal"
{
    Properties
    {
        _Scale ("Fractal Scale", Range(0.5, 8)) = 2.5
        _Speed ("Morph Speed", Range(0, 1)) = 0.12
        _Warp ("Domain Warp", Range(0, 3)) = 1.25
        _Twist ("Twist", Range(0, 6)) = 2
        _Detail ("Fine Detail", Range(0, 1)) = 0.65
        _Contours ("Contour Bands", Range(0, 1)) = 0.3
        _Contrast ("Contrast", Range(0.5, 5)) = 2.2
        _Brightness ("Brightness", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            float _Scale, _Speed, _Warp, _Twist, _Detail;
            float _Contours, _Contrast, _Brightness;
            struct Attributes { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position = UnityObjectToClipPos(v.vertex);
                o.direction = v.vertex.xyz;
                return o;
            }
            float Hash(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float Noise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f*f*f*(f*(f*6-15)+10);
                return lerp(
                    lerp(lerp(Hash(i), Hash(i+float3(1,0,0)), f.x),
                         lerp(Hash(i+float3(0,1,0)), Hash(i+float3(1,1,0)), f.x), f.y),
                    lerp(lerp(Hash(i+float3(0,0,1)), Hash(i+float3(1,0,1)), f.x),
                         lerp(Hash(i+float3(0,1,1)), Hash(i+float3(1,1,1)), f.x), f.y), f.z);
            }
            float Fbm(float3 p)
            {
                float value = 0, amplitude = 0.5;
                [unroll] for (int octave = 0; octave < 5; octave++)
                {
                    value += amplitude * Noise(p);
                    // Rotate octave axes to suppress aligned lattice patterns.
                    p = float3(dot(p,float3(0,0.8,0.6)),
                               dot(p,float3(-0.8,0.36,-0.48)),
                               dot(p,float3(-0.6,-0.48,0.64))) * 2.03 + 17.1;
                    amplitude *= 0.5;
                }
                return value / 0.96875;
            }
            float4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // Sample a 3D field on the sphere: no UV seam or pole singularity.
                float3 p = normalize(i.direction) * _Scale;
                float t = _Time.y * _Speed;
                float angle = _Twist * sin(p.y * 0.7 + t * 0.31) + t * 0.13;
                float s, c;
                sincos(angle, s, c);
                p.xz = float2(c*p.x-s*p.z, s*p.x+c*p.z);
                float3 drift = float3(t*0.23, -t*0.17, t*0.19);
                float3 warp = float3(Fbm(p+drift), Fbm(p.yzx-drift+19.7),
                                     Fbm(p.zxy+drift+43.1)) - 0.5;
                float3 q = p + warp * _Warp * 3 + drift;
                float field = Fbm(q);
                float fine = Fbm(q*2.7 + warp*2 - drift*0.4);
                float ridges = 1 - abs(2*fine-1);
                float density = lerp(field, field*0.65+ridges*0.35, _Detail);
                float phase = field*55 + fine*8;
                // Fade subpixel contours to avoid distant shimmer.
                float band = 0.5+0.5*cos(phase);
                band = lerp(band, 0.5, saturate(fwidth(phase)*0.5));
                density = lerp(density, density*0.55+band*0.45, _Contours);
                float gray = saturate((density-0.5)*_Contrast+0.5)*_Brightness;
                return float4(gray.xxx, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
