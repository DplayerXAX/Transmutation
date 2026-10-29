Shader "Capstone/ArtDirection/HabitatFX"
{
    // Additive, animated effects for the habitat block (they move in Play mode and in a Scene view with
    // "Always Refresh" on):
    // 0 heat column: bands of hot air rising up a soft open tube (uv.y = 0 bottom .. 1 top, uv.x = round),
    // 1 light shaft: a soft cone of light with dust drifting through it (uv.y = 0 top .. 1 bottom),
    // 2 time rings: dashed rings turning, each at its own speed (uv.x = round, uv.y = across, uv1.x = speed),
    // 3 sparks: camera-facing four-point flares (all four corners at the spark's centre, uv = corner,
    //   uv1 = random, life offset); with Rise > 0 they float up and fade like embers, with Rise 0 they twinkle,
    // 4 ripples: a pool of colour lying on the ground (uv.x = round, uv.y = 0 centre .. 1 rim) with wavy rings
    //   flowing outward; Fill = how strong the pool is, Size = how sharp the rings are. Neighbouring pools
    //   overlap, so the regions bleed into each other,
    // 5 quantum foam (heat): a ground pool of jittering sub-atomic cells whose edges split into red / green /
    //   blue copies, like light refracted through hot, unstable matter (uv as in Ripples),
    // 6 tide (time): a clear sheet hovering over the ground (uv = position across the sheet 0..1) that the
    //   vertex shader lifts into travelling waves; only the crests and the glancing edges show, so it reads as
    //   transparent water flowing past, with a second faster swell over it,
    // 7 diffraction (light): spectral rings round a source, each colour at its own spacing so the rings split
    //   into rainbows, crossed by thin diffraction spikes (uv as in Ripples),
    // 8 veil (time): a thin translucent sheet flowing in the wind (uv.x across 0..1, uv.y metres along). A thin
    //   film looks brighter the more edge-on it is seen (the light path through it grows as 1 / |N.V|), so the
    //   folds glow and the faces stay clear; fine striations run along the flow; colour runs across the sheet
    //   (uv1 = random, length in metres),
    // 9 starburst (light): a camera-facing burst (one quad per burst, corners at its centre as for sparks):
    //   radial streaks of random length, each colour scaled out by its own wavelength (blue inside, red
    //   outside) the way a diffraction pattern spreads, over a white core,
    // 10 caustics (light through crystals): a ground pool (uv as in Ripples) of bright filaments where refracted
    //   light focuses: the sharp folds of a slowly moving wave field; each channel sees the field slightly
    //   scaled, so the filaments split into rainbow edges,
    // 11 halo (fluorescence): the mesh pushed out along its normals by Size and drawn as a soft additive glow,
    //   brightest through the middle, so fluorescent minerals bleed light into the air round them,
    // 12 leaf (growth): a thin backlit membrane of chlorophyll, alpha blended (set the blend to SrcAlpha /
    //   OneMinusSrcAlpha). Light through it is absorbed along the path, so it is clear lime face-on and deep
    //   green edge-on (the dark curves), brighter when the sun is behind it, with a fine net of veins.
    Properties
    {
        [Enum(HeatColumn,0,LightShaft,1,TimeRings,2,Sparks,3,Ripples,4,QuantumFoam,5,Tide,6,Diffraction,7,Veil,8,Starburst,9,Caustics,10,Halo,11,Leaf,12)] _Mode("Mode", Float) = 0
        [HDR] _Color("Colour", Color) = (1, 0.5, 0.1, 1)
        [HDR] _Color2("Second Colour", Color) = (1, 0.9, 0.4, 1)
        _Intensity("Intensity", Range(0, 4)) = 1
        _Speed("Speed", Float) = 1
        _Count("Bands / Dashes", Float) = 6
        _Fill("Dash Fill", Range(0.05, 0.95)) = 0.5
        _Size("Spark Size (metres)", Float) = 0.08
        _Rise("Spark Rise (metres, 0 = twinkle)", Float) = 3
        _Seed("Seed", Float) = 0
        _Dust("Light Shaft Dust", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Effect"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Mode;
                half4 _Color, _Color2;
                float _Intensity, _Speed, _Count, _Fill, _Size, _Rise, _Seed, _Dust, _SrcBlend, _DstBlend;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 viewWS : TEXCOORD3;
                float life : TEXCOORD4;
            };

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7)) + _Seed * 13.7) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.uv = input.uv;
                o.uv1 = input.uv1;
                o.life = 0;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                if ((int)round(_Mode) == 11)
                {
                    positionWS += TransformObjectToWorldNormal(input.normalOS) * _Size;
                }
                else if ((int)round(_Mode) == 9)
                {
                    float3 right = UNITY_MATRIX_V[0].xyz, camUp = UNITY_MATRIX_V[1].xyz;
                    positionWS += (right * input.uv.x + camUp * input.uv.y) * _Size * input.uv1.x;
                }
                else if ((int)round(_Mode) == 8)
                {
                    // The veil billows: waves travel along it and its far edge flutters more than its root.
                    float t = _Time.y * _Speed;
                    float3 n = TransformObjectToWorldNormal(input.normalOS);
                    float along = input.uv.y;
                    float wave = sin(along * 1.3 - t * 1.6 + input.uv1.x * 6.0) * 0.6 + sin(along * 3.1 - t * 2.7 + input.uv.x * 2.0) * 0.25;
                    positionWS += n * wave * _Rise * (0.4 + 0.6 * input.uv.x);
                    positionWS.y += sin(along * 0.7 - t * 0.8 + input.uv1.x * 3.0) * 0.25 * _Rise;
                }
                else if ((int)round(_Mode) == 3)
                {
                    // Sparks: rise and sway over their life (or stay put and twinkle), then face the camera.
                    float t = _Time.y * _Speed * 0.25 + input.uv1.y;
                    float life = frac(t);
                    o.life = life;
                    float3 up = float3(0, 1, 0);
                    positionWS += up * life * _Rise + float3(sin(t * 6.3 + input.uv1.x * 40.0), 0, cos(t * 5.1 + input.uv1.x * 23.0)) * 0.15 * saturate(_Rise);
                    float3 right = UNITY_MATRIX_V[0].xyz, camUp = UNITY_MATRIX_V[1].xyz;
                    float size = _Size * (0.6 + 0.8 * input.uv1.x);
                    positionWS += (right * input.uv.x + camUp * input.uv.y) * size;
                }
                else if ((int)round(_Mode) == 6)
                {
                    // Tide: waves travelling along the sheet's v direction (towards the front), bent sideways.
                    float t = _Time.y * _Speed;
                    float2 q = input.uv * float2(_Count * 0.6, _Count);
                    float wave = sin(q.y * 6.2832 - t * 1.3 + sin(q.x * 2.1 + t * 0.3) * 0.8) * 0.5 + 0.5;
                    float swell = sin(q.y * 2.3 + q.x * 1.7 - t * 2.1) * 0.5 + 0.5;
                    float edge = sin(input.uv.x * 3.1416) * sin(input.uv.y * 3.1416);
                    o.life = wave;
                    positionWS.y += (pow(wave, 2.0) * 0.45 + swell * 0.15) * _Rise * edge;
                }
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.viewWS = GetWorldSpaceViewDir(positionWS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                int mode = (int)round(_Mode);
                float t = _Time.y * _Speed;
                float facing = abs(dot(normalize(i.normalWS), normalize(i.viewWS)));
                half3 colour;
                half alpha;
                if (mode == 0)
                {
                    // Hot air: wavy bands climbing the tube, strongest low down, soft at the silhouette.
                    float wobble = Noise(float2(i.uv.x * 8.0, i.uv.y * 3.0 - t * 0.7)) * 0.35;
                    float band = sin((i.uv.y * _Count - t + wobble) * 6.2832) * 0.5 + 0.5;
                    band = pow(band, 4.0);
                    alpha = band * (1.0 - i.uv.y) * smoothstep(0.0, 0.15, i.uv.y) * pow(facing, 1.5);
                    colour = lerp(_Color.rgb, _Color2.rgb, i.uv.y);
                }
                else if (mode == 1)
                {
                    // A soft cone of light, bright at its core, with dust specks drifting down through it.
                    float core = pow(facing, 3.0);
                    float fade = smoothstep(0.0, 0.2, i.uv.y) * (1.0 - smoothstep(0.75, 1.0, i.uv.y));
                    float2 dustUv = float2(i.uv.x * 40.0, i.uv.y * 25.0 + t * 0.6);
                    float dust = step(0.985, Hash(floor(dustUv))) * (0.5 + 0.5 * sin(t * 3.0 + Hash(floor(dustUv) + 3.0) * 20.0));
                    alpha = (core * 0.35 + dust * _Dust) * fade;
                    colour = lerp(_Color.rgb, _Color2.rgb, i.uv.y);
                }
                else if (mode == 2)
                {
                    // Dashed rings turning at their own speeds, with fine ticks like a clock face.
                    float turn = i.uv.x + t * i.uv1.x * 0.05;
                    float dash = step(frac(turn * _Count), _Fill);
                    float ticks = step(frac(turn * _Count * 6.0), 0.12) * step(0.6, i.uv.y);
                    float across = smoothstep(0.0, 0.2, i.uv.y) * smoothstep(1.0, 0.8, i.uv.y);
                    alpha = saturate(dash * 0.9 + ticks * 0.6) * across;
                    colour = lerp(_Color.rgb, _Color2.rgb, ticks);
                }
                else if (mode == 4)
                {
                    float r = i.uv.y, a = i.uv.x * 6.2832;
                    // Rings bent by slow noise round the circle, so they flow like disturbed water, not like targets.
                    float warp = Noise(float2(cos(a), sin(a)) * 2.5 + r * 3.0 + _Seed + t * 0.05) * 0.16 + sin(a * 3.0 + t * 0.4) * 0.025;
                    float phase = (r + warp) * _Count - t * 0.35;
                    float ring = pow(saturate(sin(phase * 6.2832) * 0.5 + 0.5), max(_Size, 1.0));
                    float edge = 1.0 - smoothstep(0.45, 1.0, r);
                    float pool = pow(1.0 - r, 2.0) * _Fill;
                    alpha = (ring * (0.3 + 0.7 * (1.0 - r)) + pool) * edge;
                    colour = lerp(_Color.rgb, _Color2.rgb, ring);
                }
                else if (mode == 5)
                {
                    float r = i.uv.y, a = i.uv.x * 6.2832;
                    float2 p = float2(cos(a), sin(a)) * r * _Count;
                    // Cells that boil: the seeds jump a little many times a second.
                    float tick = floor(t * 6.0);
                    half3 rgb = 0;
                    [unroll] for (int c = 0; c < 3; c++)
                    {
                        // Each colour sees the cells a little shifted, so edges split into fringes.
                        float2 q = p + float2(c - 1, (c - 1) * 0.6) * _Fill * 0.12;
                        float2 cell = floor(q), f = frac(q);
                        float best = 8.0, second = 8.0;
                        for (int y = -1; y <= 1; y++)
                            for (int x = -1; x <= 1; x++)
                            {
                                float2 o2 = float2(x, y);
                                float2 jitter = float2(Hash(cell + o2 + tick * 0.13), Hash(cell + o2 + 7.1 + tick * 0.07));
                                float d = length(f - o2 - 0.2 - 0.6 * jitter);
                                if (d < best) { second = best; best = d; } else if (d < second) second = d;
                            }
                        float wall = 1.0 - smoothstep(0.0, 0.07, second - best);
                        float core = exp(-best * best * 40.0) * step(0.8, Hash(cell + tick));
                        rgb[c] = wall + core * 2.0;
                    }
                    float edge = 1.0 - smoothstep(0.35, 1.0, r);
                    // Mostly red heat, the fringes keep a little of the other colours.
                    colour = rgb * lerp(_Color2.rgb, _Color.rgb, 0.5) + rgb.r * _Color.rgb;
                    alpha = edge * (0.6 + 0.4 * sin(t * 9.0 + r * 20.0));
                }
                else if (mode == 6)
                {
                    // Transparent tide: dark where you look through it, bright on crests and at glancing angles.
                    float fresnel = pow(1.0 - facing, 3.0);
                    float crest = pow(i.life, 6.0);
                    float2 q = i.uv * float2(_Count * 0.6, _Count);
                    float lace = pow(saturate(sin((q.y + sin(q.x * 3.7 + t) * 0.15) * 18.85 - t * 4.0) * 0.5 + 0.5), 12.0) * i.life;
                    float edge = smoothstep(0.0, 0.15, i.uv.x) * smoothstep(1.0, 0.85, i.uv.x) * smoothstep(0.0, 0.15, i.uv.y) * smoothstep(1.0, 0.85, i.uv.y);
                    alpha = (fresnel * 0.5 + crest * 0.8 + lace * 0.6 + 0.04) * edge;
                    colour = lerp(_Color.rgb, _Color2.rgb, saturate(crest + lace));
                }
                else if (mode == 7)
                {
                    float r = i.uv.y, a = i.uv.x * 6.2832;
                    // Airy-like rings, red spaced widest and blue tightest, so each ring fans into a spectrum.
                    float3 k = float3(0.86, 1.0, 1.18) * _Count;
                    float3 rings = pow(saturate(cos((r * k - t * 0.15) * 6.2832) * 0.5 + 0.5), max(_Size, 1.0));
                    // Diffraction spikes from a six-bladed aperture, and a faint grating shimmer across them.
                    float spikes = pow(abs(cos(a * 3.0 + _Seed)), 60.0) * (1.0 - r);
                    float grating = pow(saturate(sin((r * 40.0 + a * 2.0) - t) * 0.5 + 0.5), 8.0) * 0.25;
                    float edge = 1.0 - smoothstep(0.4, 1.0, r);
                    colour = rings * lerp(_Color.rgb, _Color2.rgb, r) + (spikes + grating) * _Color2.rgb;
                    alpha = edge * (0.55 + 0.45 * (1.0 - r));
                }
                else if (mode == 8)
                {
                    float nv = abs(dot(normalize(i.normalWS), normalize(i.viewWS)));
                    float path = saturate(0.06 / max(nv, 0.03));
                    float across = i.uv.x;
                    float edges = smoothstep(0.0, 0.08, across) * smoothstep(1.0, 0.92, across);
                    float ends = smoothstep(0.0, 2.0, i.uv.y) * smoothstep(i.uv1.y, i.uv1.y - 2.0, i.uv.y);
                    float striae = pow(saturate(sin((across * 70.0 + sin(i.uv.y * 0.9 - t * 0.6) * 4.0)) * 0.5 + 0.5), 6.0);
                    float rim = exp(-pow((across - 0.97) * 30.0, 2.0)) + exp(-pow((across - 0.03) * 30.0, 2.0)) * 0.5;
                    alpha = (path * 1.4 + striae * 0.12 + rim * 0.5 + 0.03) * edges * ends;
                    colour = lerp(_Color.rgb, _Color2.rgb, smoothstep(0.2, 1.0, across));
                }
                else if (mode == 10)
                {
                    float r = i.uv.y, a = i.uv.x * 6.2832;
                    float2 p = float2(cos(a), sin(a)) * r * _Count;
                    half3 rgb = 0;
                    [unroll] for (int c = 0; c < 3; c++)
                    {
                        float2 q = p * (1.0 + (c - 1) * 0.025 * _Fill * 10.0) + _Seed;
                        // Two drifting wave fields; light focuses where they fold (|sin| near zero).
                        float w1 = sin(q.x * 1.7 + sin(q.y * 1.3 + t * 0.4) * 1.5 + t * 0.3);
                        float w2 = sin(q.y * 1.9 + sin(q.x * 1.1 - t * 0.35) * 1.7 - t * 0.25);
                        float w3 = sin((q.x + q.y) * 1.2 + sin(q.x * 0.7 + t * 0.2) * 2.0);
                        float focus = pow(saturate(1.0 - abs(w1 + w2 * 0.8 + w3 * 0.5) * 0.45), max(_Size, 1.0));
                        rgb[c] = focus;
                    }
                    float edge = 1.0 - smoothstep(0.3, 1.0, r);
                    colour = rgb * lerp(_Color2.rgb, _Color.rgb, r);
                    alpha = edge;
                }
                else if (mode == 11)
                {
                    float nv = abs(dot(normalize(i.normalWS), normalize(i.viewWS)));
                    colour = lerp(_Color.rgb, _Color2.rgb, nv);
                    alpha = pow(nv, 2.0) * 0.6;
                }
                else if (mode == 12)
                {
                    float3 n = normalize(i.normalWS), v = normalize(i.viewWS);
                    float nv = max(abs(dot(n, v)), 0.04);
                    float path = 1.0 / nv;
                    float transmit = exp(-_Fill * (path - 1.0));
                    Light sun = GetMainLight();
                    float back = pow(saturate(dot(-v, sun.direction) * 0.5 + 0.5), 3.0);
                    float veins = pow(saturate(sin(i.uv.x * 90.0 + sin(i.uv.y * 4.0) * 3.0) * 0.5 + 0.5), 20.0)
                                + pow(saturate(sin(i.uv.y * 60.0 + i.uv.x * 10.0) * 0.5 + 0.5), 24.0) * 0.6;
                    float edges = smoothstep(0.0, 0.04, i.uv.x) * smoothstep(1.0, 0.96, i.uv.x);
                    half3 lit = _Color2.rgb * (0.6 + 1.4 * back) * _Intensity;
                    colour = lerp(_Color.rgb, lit, transmit) * (1.0 - veins * 0.35);
                    float outline = 1.0 - edges;
                    colour = lerp(colour, _Color.rgb * 0.3, outline);
                    return half4(colour, saturate((1.0 - transmit) * 0.85 + 0.35 + outline));
                }
                else if (mode == 9)
                {
                    float2 p = i.uv;
                    float r = length(p), a = atan2(p.y, p.x);
                    float spin = _Time.y * _Speed * 0.05 + i.uv1.y * 6.0;
                    half3 rgb = 0;
                    [unroll] for (int c = 0; c < 3; c++)
                    {
                        // Longer wavelengths diffract wider: red spreads furthest.
                        float rc = r / (0.82 + (2 - c) * 0.12);
                        float s1 = Noise(float2((a + spin) * 7.0, i.uv1.x * 9.0));
                        float s2 = Noise(float2((a - spin * 0.7) * 19.0, i.uv1.x * 5.0 + 3.0));
                        float len = 0.25 + 0.75 * pow(s1, 3.0) + 0.3 * pow(s2, 4.0);
                        float streak = exp(-rc / max(len, 0.05) * 3.0) * pow(s1 * 0.6 + s2 * 0.4, 2.0);
                        float core = exp(-rc * rc * 60.0);
                        rgb[c] = streak * 2.0 + core * 3.0;
                    }
                    float flicker = 0.85 + 0.15 * sin(_Time.y * _Speed * 3.0 + i.uv1.x * 40.0);
                    colour = rgb * lerp(_Color2.rgb, _Color.rgb, saturate(r)) * flicker;
                    alpha = saturate(1.0 - r);
                }
                else
                {
                    // A four-point flare: thin cross arms over a soft round glow.
                    float2 p = i.uv;
                    float arms = exp(-abs(p.x) * 14.0) * exp(-abs(p.y) * 1.8) + exp(-abs(p.y) * 14.0) * exp(-abs(p.x) * 1.8);
                    float glow = exp(-dot(p, p) * 5.0);
                    float twinkle = _Rise > 0.0 ? sin(i.life * 3.1416) : 0.4 + 0.6 * pow(sin(_Time.y * _Speed * 2.0 + i.uv1.x * 30.0) * 0.5 + 0.5, 3.0);
                    alpha = saturate(arms + glow * 0.6) * twinkle * saturate(1.0 - dot(p, p));
                    colour = lerp(_Color2.rgb, _Color.rgb, saturate(length(p)));
                }
                return half4(colour * alpha * _Intensity, 1);
            }
            ENDHLSL
        }
    }
}
