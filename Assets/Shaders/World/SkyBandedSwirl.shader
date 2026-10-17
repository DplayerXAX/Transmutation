Shader "Capstone/Skybox/Banded Swirl"
{
    // An ordered version of the white kneaded sky: stacked ribbon layers that wind around the sky
    // in one direction and curl in steady waves, like layered cut paper. Each ribbon is shaded
    // from its lit edge to its tucked-under edge, with a thin ink line where one layer overlaps the next.
    Properties
    {
        _LightTone ("Ribbon Light Tone", Color) = (0.95, 0.95, 0.93, 1)
        _ShadowTone ("Ribbon Tucked Tone", Color) = (0.58, 0.58, 0.6, 1)
        _InkColor ("Edge Ink", Color) = (0.12, 0.12, 0.14, 1)
        _GroundColor ("Below Horizon", Color) = (0.4, 0.4, 0.42, 1)

        [Header(Layout)]
        _Bands ("Ribbon Layers (horizon to zenith)", Range(2, 30)) = 11
        _Wind ("Spiral Turns (whole numbers)", Range(-4, 4)) = 1
        _WaveCount ("Waves Around The Sky (whole numbers)", Range(1, 12)) = 5
        _WaveAmount ("Wave Height", Range(0, 1.5)) = 0.55
        _CurlAmount ("Curl Height", Range(0, 1)) = 0.25
        _Knead ("Soft Kneaded Noise", Range(0, 0.5)) = 0.12

        [Header(Look)]
        _Falloff ("Shading Falloff", Range(0.2, 4)) = 1.4
        _EdgeWidth ("Edge Line Width (pixels)", Range(0, 4)) = 1.4
        _Variation ("Tone Difference Between Layers", Range(0, 0.4)) = 0.12
        _Speed ("Drift Speed", Range(0, 0.2)) = 0.02
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

            float4 _LightTone, _ShadowTone, _InkColor, _GroundColor;
            float _Bands, _Wind, _WaveCount, _WaveAmount, _CurlAmount, _Knead;
            float _Falloff, _EdgeWidth, _Variation, _Speed;

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

            float Fbm(float3 p)
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
                float elevation = asin(clamp(direction.y, -1, 1)) / 1.5707963; // -1..1
                float azimuth = atan2(direction.z, direction.x);                 // -pi..pi
                float t = _Time.y * _Speed;

                // Whole-number spiral turns and wave counts keep the pattern seamless all the way round.
                float wind = round(_Wind);
                float waves = round(_WaveCount);
                float height = saturate(elevation);

                // Ribbon coordinate: stacked layers swept by regular waves...
                float flow = height * _Bands - t * 3.0;
                flow += _WaveAmount * sin(azimuth * waves + height * 4.0 + t * 6.2831853);
                // ...smaller curls riding on the waves, leaning the same way so they read as wind...
                flow += _CurlAmount * sin(azimuth * waves * 2.0 - height * _Bands * 1.7 + t * 9.0) * (1.0 - height * 0.5);
                // ...a little soft noise so it still feels kneaded rather than printed...
                flow += (Fbm(direction * 2.2 + float3(t, 0, -t)) - 0.5) * _Knead * 4.0;
                // ...all wound into a slow spiral around the zenith.
                float coordinate = flow + wind * azimuth / 6.2831853;

                float layer = floor(coordinate);
                float across = frac(coordinate);

                // Lit at the upper edge of each ribbon, tucked into shadow where the next layer covers it.
                float shade = pow(across, _Falloff);
                float3 color = lerp(_ShadowTone.rgb, _LightTone.rgb, shade);
                color *= 1.0 - _Variation * Hash(float3(layer, 3.1, 7.7));

                // Thin ink line where one ribbon overlaps the next, faded where layers bunch up (the zenith).
                // Screen-space rate of change, measured without the jump where the azimuth wraps around.
                float azimuthRate = min(fwidth(azimuth), fwidth(atan2(-direction.z, -direction.x)));
                float fw = max(fwidth(flow) + abs(wind) * azimuthRate / 6.2831853, 1e-5);
                float distanceToEdge = min(across, 1.0 - across);
                float edge = 1.0 - smoothstep(fw * _EdgeWidth * 0.5, fw * (_EdgeWidth * 0.5 + 1.0), distanceToEdge);
                edge *= saturate(1.5 - fw * 4.0);
                color = lerp(color, _InkColor.rgb, edge * step(0.001, _EdgeWidth));

                // Dense layers near the zenith blur into their average tone instead of shimmering.
                float3 average = lerp(_ShadowTone.rgb, _LightTone.rgb, 1.0 / (_Falloff + 1.0));
                color = lerp(color, average, saturate(fw * 3.0 - 0.6));

                color = lerp(color, _GroundColor.rgb, saturate(-elevation * 8.0));
                return float4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
