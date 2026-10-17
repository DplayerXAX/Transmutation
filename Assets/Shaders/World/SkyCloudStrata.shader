Shader "Capstone/Skybox/Cloud Strata"
{
    // A heavy cloud ceiling seen from below, like eroded rock: big cloud masses with dark navy
    // voids between them, puffy lobes with crinkled edges, relief light from one side and soft
    // shadows thrown by higher cloud onto lower cloud. The ceiling recedes in perspective and
    // fades into haze at the horizon.
    Properties
    {
        _LitColor ("Lit Cloud", Color) = (0.92, 0.93, 0.96, 1)
        _MidColor ("Shaded Cloud", Color) = (0.34, 0.36, 0.42, 1)
        _DeepColor ("Deep Voids", Color) = (0.03, 0.06, 0.12, 1)
        _GlintColor ("Glint", Color) = (0.5, 0.85, 1.0, 1)
        _HorizonColor ("Horizon Haze", Color) = (0.6, 0.61, 0.66, 1)
        _GroundColor ("Below Horizon", Color) = (0.12, 0.13, 0.16, 1)

        [Header(Shape)]
        _Scale ("Cloud Scale", Range(0.2, 4)) = 0.9
        _MacroScale ("Size Of Masses (smaller = bigger)", Range(0.05, 1)) = 0.28
        _Masses ("Mass Contrast", Range(0.5, 5)) = 3.2
        _Warp ("Swirl", Range(0, 2)) = 0.45
        _Detail ("Lobes And Erosion", Range(0, 2)) = 1
        _Steps ("Shelf Layers", Range(1, 12)) = 5
        _Terrace ("Shelf Strength", Range(0, 1)) = 0.35

        [Header(Light)]
        _LightAngle ("Light Direction (degrees)", Range(0, 360)) = 132
        _LightHeight ("Flat Light", Range(0, 1)) = 0.3
        _Relief ("Relief Strength", Range(0, 6)) = 3
        _ShadowReach ("Shadow Length", Range(0, 0.3)) = 0.08
        _ShadowStrength ("Shadow Strength", Range(0, 2)) = 1
        _Voids ("Darkness Of Voids", Range(0, 1)) = 1
        _GlintAmount ("Glint Amount", Range(0, 1)) = 0.18

        [Header(Line Art Filter)]
        _Stylize ("Line Art Amount", Range(0, 1)) = 1
        _Levels ("Tone Levels", Range(2, 8)) = 4
        _InkColor ("Ink", Color) = (0.03, 0.03, 0.04, 1)
        _PaperColor ("Paper", Color) = (0.95, 0.95, 0.93, 1)
        _CoarseDetail ("Outline Simplicity (lower = simpler shapes)", Range(1, 6)) = 3
        _OutlineWidth ("Outline Width (pixels)", Range(0, 4)) = 1.4
        _Pencil ("Fine Pencil Texture", Range(0, 1)) = 0.35
        _CloudTint ("Keep Cloud Colour", Range(0, 1)) = 0.12
        _HatchScale ("Hatch Lines", Float) = 40
        _HatchWidth ("Hatch Width (pixels)", Range(0.3, 3)) = 0.9

        [Header(Motion)]
        _Speed ("Drift Speed", Range(0, 0.2)) = 0.015
        _DriftAngle ("Drift Direction (degrees)", Range(0, 360)) = 35
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

            float4 _LitColor, _MidColor, _DeepColor, _GlintColor, _HorizonColor, _GroundColor;
            float _Scale, _MacroScale, _Masses, _Warp, _Detail, _Steps, _Terrace;
            float _LightAngle, _LightHeight, _Relief, _ShadowReach, _ShadowStrength, _Voids, _GlintAmount;
            float _Speed, _DriftAngle;
            float4 _InkColor, _PaperColor;
            float _Stylize, _Levels, _CoarseDetail, _OutlineWidth, _Pencil, _CloudTint, _HatchScale, _HatchWidth;

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

            // Random gradient angle per lattice point.
            float HashAngle(float2 p)
            {
                float3 q = frac(float3(p.x * 0.1031, p.y * 0.1030, p.x * 0.0973));
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z) * 6.2831853;
            }

            // Gradient noise, roughly -0.7..0.7.
            float GradientNoise(float2 p)
            {
                float2 i = floor(p), f = p - i;
                float2 u = f * f * f * (f * (f * 6 - 15) + 10);
                float a0 = HashAngle(i), a1 = HashAngle(i + float2(1, 0)), a2 = HashAngle(i + float2(0, 1)), a3 = HashAngle(i + float2(1, 1));
                float a = dot(float2(cos(a0), sin(a0)), f);
                float b = dot(float2(cos(a1), sin(a1)), f - float2(1, 0));
                float c = dot(float2(cos(a2), sin(a2)), f - float2(0, 1));
                float d = dot(float2(cos(a3), sin(a3)), f - float2(1, 1));
                return a + (b - a) * u.x + (c - a) * u.y + (a - b - c + d) * u.x * u.y;
            }

            // Octaves finer than a pixel (fade) are faded out so distant cloud does not shimmer.
            float Fbm(float2 p, int octaves, float fade, float gain)
            {
                float value = 0, amplitude = 0.5;
                // Normalise by all octaves, so skipping faded ones does not change the overall level.
                float total = 0.5 * (1 - pow(gain, octaves)) / (1 - gain);
                for (int o = 0; o < octaves; o++)
                {
                    if (o > fade) break;
                    value += amplitude * GradientNoise(p) * saturate(fade - o);
                    p = float2(0.8 * p.x - 0.6 * p.y, 0.6 * p.x + 0.8 * p.y) * 2.03 + float2(17.1, 9.2);
                    amplitude *= gain;
                }
                return value / total;
            }

            float CloudHeight(float2 p, float fade)
            {
                // Large masses and voids.
                float macro = Fbm(p * _MacroScale + float2(7.7, -3.3), 4, 9, 0.5) * _Masses + 0.5;
                float2 warp = float2(Fbm(p * 0.5 + float2(3.1, 1.7), 3, 9, 0.5), Fbm(p * 0.5 + float2(-5.3, 8.9), 3, 9, 0.5));
                float2 q = p + warp * _Warp * 2;
                // Puffy lobes: creases where the noise crosses zero, crinkled by the fine octaves.
                float lobes = 1 - abs(Fbm(q, 7, fade, 0.55)) * 2.2;
                float h = macro * 0.75 + (lobes - 0.4) * 0.3 * _Detail;
                // Soft shelves where the masses step down.
                float s = h * _Steps;
                float stepped = (floor(s) + smoothstep(0, 0.35, frac(s))) / _Steps;
                return lerp(h, stepped, _Terrace);
            }

            // Painted cloud colour at a point of the cloud ceiling, using octaves up to fade.
            float3 CloudColor(float3 direction, float2 p, float fade, float e)
            {
                float h = CloudHeight(p, fade);
                float hx = CloudHeight(p + float2(e, 0), fade);
                float hy = CloudHeight(p + float2(0, e), fade);

                // Relief: slopes facing the light are bright, slopes facing away go grey.
                float angle = radians(_LightAngle);
                float2 light = float2(cos(angle), sin(angle));
                float2 tilt = -float2(hx - h, hy - h) / e * _Relief * 0.1;
                float tiltLength = sqrt(dot(tilt, tilt) + 1);
                float lambert = saturate(dot(tilt, light) / tiltLength * 0.9 + _LightHeight / tiltLength);

                // Higher cloud towards the light throws a soft shadow.
                float s1 = CloudHeight(p + light * _ShadowReach, fade - 1.5);
                float s2 = CloudHeight(p + light * _ShadowReach * 2.5, fade - 2.5);
                float shadow = saturate(max(s1 - h, (s2 - h) * 0.6) * 10 * _ShadowStrength);

                float3 color = lerp(_MidColor.rgb, _LitColor.rgb, lambert);
                color = lerp(color, lerp(_MidColor.rgb, _DeepColor.rgb, 0.65), shadow * 0.85);
                color = lerp(color, _DeepColor.rgb, smoothstep(0.55, 0.25, h) * _Voids);
                color = lerp(color, _GlintColor.rgb, smoothstep(0.92, 1, lambert) * smoothstep(0.6, 0.8, h) * _GlintAmount);

                // Far cloud near the horizon flattens into haze.
                float haze = pow(1 - saturate(direction.y * 3.2), 3);
                return lerp(color, _HorizonColor.rgb, haze);
            }

            // Anti-aliased thin line on whole values of a coordinate; fades out where lines crowd.
            float Stripe(float coordinate, float widthPixels)
            {
                float fw = max(fwidth(coordinate), 1e-5);
                float d = abs(frac(coordinate + 0.5) - 0.5);
                return (1 - smoothstep(fw * widthPixels * 0.5, fw * (widthPixels * 0.5 + 1), d)) * saturate(1.5 - fw * 4);
            }

            float Tone(float3 color)
            {
                return sqrt(saturate(dot(color, float3(0.2126, 0.7152, 0.0722))));
            }

            float4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 direction = normalize(i.direction);

                // Project onto a flat cloud ceiling, so clouds recede towards the horizon.
                float lift = max(direction.y, 0) + 0.2;
                float drift = radians(_DriftAngle);
                float2 p = direction.xz / lift * _Scale + float2(cos(drift), sin(drift)) * _Time.y * _Speed;
                float footprint = max(max(fwidth(p.x), fwidth(p.y)), 1e-4);
                float fade = log2(1 / footprint) - 0.5;
                float e = max(footprint, 0.004);

                float3 painted = CloudColor(direction, p, fade, e);

                // Line art filter, so the sky matches the ink-and-paper world:
                // flat tone bands that follow only the big cloud masses, ink outlines between bands,
                // hatching in the dark bands, and the fine cloud detail kept as faint pencil texture.
                float3 coarse = CloudColor(direction, p, min(fade, _CoarseDetail), e);
                float tone = Tone(coarse);
                float bands = tone * _Levels;
                float flat = min(floor(bands), _Levels - 1) / (_Levels - 1);
                float pencil = (Tone(painted) - tone) * _Pencil;
                float3 drawn = lerp(_InkColor.rgb, _PaperColor.rgb, pow(saturate(flat + pencil), 1.6));
                drawn = lerp(drawn, painted, _CloudTint);

                float2 plane = direction.xz / lift;
                float hatchA = Stripe(dot(plane, float2(0.71, 0.71)) * _HatchScale + 0.15 * sin(plane.x * 2.3 - plane.y * 1.7), _HatchWidth);
                float hatchB = Stripe(dot(plane, float2(-0.6, 0.8)) * _HatchScale * 1.13 + 0.15 * sin(plane.x * 1.9 + plane.y * 2.1), _HatchWidth);
                float dark = 1 - flat;
                float hatch = max(hatchA * step(0.4, dark), hatchB * step(0.75, dark));
                float3 hatchInk = flat < 0.5 ? lerp(_InkColor.rgb, _PaperColor.rgb, 0.55) : _InkColor.rgb;
                drawn = lerp(drawn, hatchInk, hatch * 0.8);
                drawn = lerp(drawn, _InkColor.rgb, Stripe(bands, _OutlineWidth) * 0.9 * step(0.001, _OutlineWidth));

                float3 color = lerp(painted, drawn, _Stylize);
                // Below the horizon is plain ground colour.
                color = lerp(color, _GroundColor.rgb, saturate(-direction.y * 8));
                return float4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
