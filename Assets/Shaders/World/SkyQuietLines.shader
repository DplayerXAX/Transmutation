Shader "Capstone/Skybox/Quiet Lines"
{
    // A calm sky for the line world: a dark gradient with a few thin contour lines of a slowly
    // drifting field. Lines fade out towards the horizon so hills keep a clean silhouette.
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.03, 0.03, 0.035, 1)
        _HorizonColor ("Horizon", Color) = (0.11, 0.11, 0.12, 1)
        _GroundColor ("Below Horizon", Color) = (0.02, 0.02, 0.025, 1)
        _LineColor ("Line Colour", Color) = (0.55, 0.55, 0.58, 1)
        _LineStrength ("Line Strength", Range(0, 1)) = 0.45
        _LineWidth ("Line Width (pixels)", Range(0.3, 3)) = 0.9
        _Levels ("Contour Levels", Range(2, 24)) = 9
        _Scale ("Field Scale", Range(0.3, 6)) = 1.6
        _Speed ("Drift Speed", Range(0, 0.2)) = 0.015
        _Keep ("Unbroken Share Of Lines", Range(0, 1)) = 0.6
        _HorizonFade ("Line Fade Above Horizon", Range(0.01, 1)) = 0.35
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

            float4 _ZenithColor, _HorizonColor, _GroundColor, _LineColor;
            float _LineStrength, _LineWidth, _Levels, _Scale, _Speed, _Keep, _HorizonFade;

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
                f = f * f * (3 - 2 * f);
                return lerp(
                    lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x),
                         lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                    lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                         lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            // Only three octaves: broad shapes, no fine noise.
            float Field(float3 p)
            {
                float value = 0, amplitude = 0.5;
                [unroll] for (int octave = 0; octave < 3; octave++)
                {
                    value += amplitude * Noise(p);
                    p = p * 2.07 + 11.3;
                    amplitude *= 0.5;
                }
                return value / 0.875;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 direction = normalize(i.direction);
                float height = direction.y;

                float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(saturate(height), 0.6));
                sky = lerp(sky, _GroundColor.rgb, saturate(-height * 6));

                float t = _Time.y * _Speed;
                float field = Field(direction * _Scale + float3(t, t * 0.3, -t * 0.7));
                float coordinate = field * _Levels;
                float fw = max(fwidth(coordinate), 1e-5);
                float distanceToLine = abs(frac(coordinate + 0.5) - 0.5);
                float stroke = 1 - smoothstep(fw * _LineWidth * 0.5, fw * (_LineWidth * 0.5 + 1), distanceToLine);

                // Break some lines into arcs, and drop lines where they would crowd together.
                float level = floor(coordinate + 0.5);
                float arc = floor(atan2(direction.z, direction.x) * 3 + Field(direction * 3.1) * 4);
                stroke *= step(1 - _Keep, Hash(float3(level, arc, 7.1)));
                stroke *= saturate(1.5 - fw * 3);

                stroke *= smoothstep(0, _HorizonFade, height) * _LineStrength;
                return float4(lerp(sky, _LineColor.rgb, stroke), 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
