Shader "Capstone/Skybox/Liquid Ink"
{
    // Poured, glossy grey ink across a sky ceiling. The smooth swirling field is treated as a liquid
    // surface built from a few overlapping layers joined by rounded lips. It is lit with soft diffuse
    // light and a glossy highlight, so lips facing the light catch white streaks and the far sides
    // fall into shadow; no drawn outlines. Dark bubbles float in it. The light is then cut into a
    // few flat grey blocks with crisp edges (highlights become flat white shapes), like a cel-shaded
    // ink painting. The ink deepens overhead and thins into a bright haze at the horizon.
    Properties
    {
        _InkColor ("Ink", Color) = (0.035, 0.035, 0.04, 1)
        _PaperColor ("Paper", Color) = (0.9, 0.9, 0.89, 1)
        _HorizonColor ("Horizon Haze", Color) = (0.82, 0.82, 0.82, 1)
        _GroundColor ("Below Horizon", Color) = (0.12, 0.12, 0.13, 1)

        [Header(Shapes)]
        _Scale ("Shape Scale", Range(0.1, 3)) = 0.55
        _Warp ("Swirl", Range(0, 3)) = 1.5
        _Macro ("Big Dark And Light Regions", Range(0, 3)) = 1.4
        _Darkness ("Overall Darkness", Range(-0.5, 0.5)) = 0.12
        _Levels ("Ink Layers", Range(1, 8)) = 4
        _StepSoftness ("Layer Lip Softness", Range(0.05, 0.5)) = 0.22

        [Header(Light)]
        _LightAngle ("Light Direction (degrees)", Range(0, 360)) = 126
        _LightHeight ("Light Height", Range(0.1, 2)) = 0.4
        _Relief ("Relief", Range(0, 4)) = 1.3
        _Gloss ("Gloss Sharpness", Range(4, 200)) = 35
        _GlossStrength ("Gloss Strength", Range(0, 1.5)) = 1

        [Header(Flat Blocks)]
        _Blocks ("Light Blocks", Range(2, 10)) = 5
        _BlockGradient ("Gradient Inside Blocks", Range(0, 1)) = 0.25
        _HighlightCut ("Highlight Threshold", Range(0.05, 1)) = 0.35
        _Lift ("Lift Dark Blocks", Range(0, 0.5)) = 0.12

        [Header(Sky)]
        _ZenithDarken ("Darker Overhead", Range(0, 1)) = 0.3
        _HazeHeight ("Horizon Haze Height", Range(0.5, 4)) = 1.8

        [Header(Bubbles)]
        _Bubbles ("Bubbles", Range(0, 0.6)) = 0.12
        _BubbleScale ("Bubble Spacing (bigger = smaller bubbles)", Range(1, 12)) = 4.5

        [Header(Motion)]
        _Speed ("Drift Speed", Range(0, 0.2)) = 0.02
        _DriftAngle ("Drift Direction (degrees)", Range(0, 360)) = 35
        _Morph ("Shape Change Speed", Range(0, 0.2)) = 0.03
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            float4 _InkColor, _PaperColor, _HorizonColor, _GroundColor;
            float _Scale, _Warp, _Macro, _Darkness, _Levels, _StepSoftness;
            float _LightAngle, _LightHeight, _Relief, _Gloss, _GlossStrength;
            float _ZenithDarken, _HazeHeight;
            float _Blocks, _BlockGradient, _HighlightCut, _Lift;
            float _Bubbles, _BubbleScale, _Speed, _DriftAngle, _Morph;

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

            float Hash(float2 p)
            {
                float3 q = frac(float3(p.x * 0.1031, p.y * 0.1030, p.x * 0.0973));
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            float GradientNoise(float2 p)
            {
                float2 i = floor(p), f = p - i;
                float2 u = f * f * f * (f * (f * 6 - 15) + 10);
                float a0 = Hash(i) * 6.2831853, a1 = Hash(i + float2(1, 0)) * 6.2831853;
                float a2 = Hash(i + float2(0, 1)) * 6.2831853, a3 = Hash(i + float2(1, 1)) * 6.2831853;
                float a = dot(float2(cos(a0), sin(a0)), f);
                float b = dot(float2(cos(a1), sin(a1)), f - float2(1, 0));
                float c = dot(float2(cos(a2), sin(a2)), f - float2(0, 1));
                float d = dot(float2(cos(a3), sin(a3)), f - float2(1, 1));
                return a + (b - a) * u.x + (c - a) * u.y + (a - b - c + d) * u.x * u.y;
            }

            float Fbm(float2 p, int octaves)
            {
                float value = 0, amplitude = 0.5, total = 0;
                for (int o = 0; o < octaves; o++)
                {
                    value += amplitude * GradientNoise(p);
                    total += amplitude;
                    p = float2(0.8 * p.x - 0.6 * p.y, 0.6 * p.x + 0.8 * p.y) * 2.03 + float2(17.1, 9.2);
                    amplitude *= 0.5;
                }
                return value / total;
            }

            // Smooth liquid field: warped twice, low octaves only so shapes stay round and glossy.
            float Field(float2 p, float morph)
            {
                float2 a = float2(Fbm(p + float2(1.7, 9.2) + morph, 2), Fbm(p + float2(8.3, 2.8) - morph, 2));
                float2 b = float2(Fbm(p + a * _Warp + float2(3.1, 5.4), 2), Fbm(p + a * _Warp + float2(-2.2, 7.7), 2));
                float macro = Fbm(p * 0.25 + float2(4.4, -6.1), 2) * _Macro;
                return saturate(Fbm(p + b * _Warp, 3) * 1.7 + macro + 0.5 - _Darkness);
            }

            // Overlapping ink layers: nearly flat terraces joined by soft rounded lips.
            float Layered(float f)
            {
                float s = f * _Levels;
                float i = floor(s), t = s - i;
                return (i + smoothstep(0.5 - _StepSoftness, 0.5 + _StepSoftness, t) * 0.85 + t * 0.15) / _Levels;
            }

            float Height(float2 p, float morph)
            {
                return Layered(Field(p, morph));
            }

            float4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 direction = normalize(i.direction);

                float lift = max(direction.y, 0) + 0.2;
                float drift = radians(_DriftAngle);
                float2 p = direction.xz / lift * _Scale + float2(cos(drift), sin(drift)) * _Time.y * _Speed;
                float morph = _Time.y * _Morph;
                float footprint = max(max(fwidth(p.x), fwidth(p.y)), 1e-4);
                float e = max(footprint * 1.5, 0.003);

                float h = Height(p, morph);
                float hx = Height(p + float2(e, 0), morph);
                float hy = Height(p + float2(0, e), morph);

                // Surface normal of the liquid (z points at the viewer below).
                float3 normal = normalize(float3(-(hx - h) / e * _Relief * 0.12, -(hy - h) / e * _Relief * 0.12, 1));
                float angle = radians(_LightAngle);
                float2 lightFlat = float2(cos(angle), sin(angle));
                float3 light = normalize(float3(lightFlat, _LightHeight));
                float diffuse = saturate(dot(normal, light));
                float3 halfway = normalize(light + float3(0, 0, 1));
                float gloss = pow(saturate(dot(normal, halfway)), _Gloss) * _GlossStrength;

                float tone = saturate(h * 0.95 + (diffuse - 0.6) * 0.7 - 0.02);

                // Bubbles: dark ovals in the light value.
                // No branch: fwidth below needs neighbouring pixels to run the same code.
                float2 cellP = p * _BubbleScale;
                float2 cell = floor(cellP);
                float keep = step(Hash(cell + float2(0.5, 3.5)), _Bubbles);
                float2 centre = 0.3 + 0.4 * float2(Hash(cell + float2(1.5, 0)), Hash(cell + float2(0, 1.5)));
                float radius = 0.08 + 0.17 * Hash(cell + float2(7.5, 2.5));
                float2 v = (cellP - cell - centre) * float2(0.8, 1) / radius;
                float q = length(v);
                float soft = max(footprint * _BubbleScale / radius * 1.5, 0.04);
                float inside = (1 - smoothstep(1 - soft, 1 + soft, q)) * keep;
                float facing = saturate(-dot(v, lightFlat) / max(q, 1e-3));
                float lip = (1 - smoothstep(0, soft * 2.5, abs(q - 0.92))) * facing * keep;

                // Light value: deeper overhead, then cut into flat blocks.
                float value = tone * (1 - _ZenithDarken * pow(saturate(direction.y), 0.8));
                value = (value * (1 - _Lift) + _Lift) * (1 - inside * 0.7);
                float blocks = value * _Blocks;
                float block = floor(blocks), within = blocks - block;
                float rate = max(fwidth(blocks), 1e-4);
                float stepped = min(block + smoothstep(1 - rate, 1, within), _Blocks - 1) / (_Blocks - 1);
                stepped = saturate(stepped + (within - 0.5) * _BlockGradient / _Blocks);
                float3 color = lerp(_InkColor.rgb, _PaperColor.rgb, pow(stepped, 1.15));

                // Highlights: flat white shapes where the gloss (or a bubble lip) is strong.
                float shine = max(gloss, lip * 0.85);
                float shineRate = max(fwidth(shine), 1e-4);
                color = lerp(color, _PaperColor.rgb, smoothstep(_HighlightCut - shineRate, _HighlightCut + shineRate, shine));

                // Sky cue: the shapes thin into a bright haze at the horizon; below it is ground colour.
                float haze = pow(1 - saturate(direction.y * _HazeHeight), 2.5);
                color = lerp(color, _HorizonColor.rgb, haze);
                color = lerp(color, _GroundColor.rgb, saturate(-direction.y * 8));
                return float4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
