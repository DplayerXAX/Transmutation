// Strange water for the dark caves, after Daniel's toon water (ShaderLab_1, week 8 "water toon"):
// back faces draw the inside (refracted background, banded depth colour, hard-edged caustics and banded light
// shafts marched through the volume), front faces draw the toon surface (two-tone light, fresnel opacity, hard
// highlight). Changes for the caves:
//   - the volume is a unit sphere (_Shape 1, radius 0.5) or a unit cube (_Shape 0) in object space,
//   - gravity is a property (_Down, object space): the "surface" the light comes through is the side facing
//     against it, so a pool hanging from a ceiling (_Down up) has its surface underneath and shafts rising,
//   - the caustics glow by themselves (the caves have no sun) and are not fogged, so water reads in the dark,
//   - no textures: surface ripples and the vertex wobble of floating blobs are procedural.
// Needs URP Opaque Texture (on in PC_RPAsset).
Shader "Capstone/Cave/StrangeWater"
{
    Properties
    {
        [Header(Color)]
        _shallowColor ("shallow color", Color) = (0.3, 0.85, 0.95, 1)
        _deepColor ("deep color", Color) = (0.02, 0.12, 0.3, 1)
        _surfaceColor ("surface color", Color) = (0.2, 0.6, 0.95, 1)
        [HDR] _causticsColor ("caustics glow color", Color) = (0.4, 1.6, 2, 1)
        _depthRange ("depth range", Float) = 1.5
        _depthBands ("depth bands", Range(1, 6)) = 3

        [Header(Shape)]
        [Enum(Cube, 0, Sphere, 1)] _Shape ("shape", Float) = 1
        _Down ("gravity (object space)", Vector) = (0, -1, 0, 0)
        _Wobble ("wobble", Range(0, 0.2)) = 0
        _LightDir ("light comes from (world)", Vector) = (0.3, 1, 0.2, 0)

        [Header(Surface)]
        _rippleScale ("ripple scale", Float) = 6
        _normalIntensity ("ripple strength", Range(0, 1)) = 0.35
        _refractionIntensity ("refraction intensity", Range(0, 0.2)) = 0.04
        _opacity ("surface opacity", Range(0, 1)) = 0.3
        _specularSize ("specular size", Range(0, 1)) = 0.02

        [Header(Caustics)]
        _causticsScale ("caustics scale", Float) = 2.5
        _causticsSpeed ("caustics speed", Range(0, 5)) = 0.8
        _causticsThreshold ("caustics threshold", Range(0, 1)) = 0.75
        _causticsIntensity ("caustics intensity", Range(0, 2)) = 0.9

        [Header(Light Shafts)]
        _shaftIntensity ("shaft intensity", Range(0, 2)) = 0.5
        _shaftFalloff ("shaft falloff", Range(0, 10)) = 1.2
        _shaftBands ("shaft bands", Range(1, 8)) = 3
        _Glow ("glow amount", Range(0, 3)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #define SHAFT_STEPS 12

        CBUFFER_START(UnityPerMaterial)
            float4 _shallowColor, _deepColor, _surfaceColor, _causticsColor;
            float _depthRange, _depthBands;
            float _Shape;
            float4 _Down, _LightDir;
            float _Wobble;
            float _rippleScale, _normalIntensity, _refractionIntensity, _opacity, _specularSize;
            float _causticsScale, _causticsSpeed, _causticsThreshold, _causticsIntensity;
            float _shaftIntensity, _shaftFalloff, _shaftBands, _Glow;
        CBUFFER_END

        TEXTURE2D(_CameraOpaqueTexture);
        SAMPLER(sampler_CameraOpaqueTexture);

        struct A { float4 vertex : POSITION; float3 normal : NORMAL; };
        struct V
        {
            float4 pos : SV_POSITION;
            float3 normal : TEXCOORD0;
            float3 posWorld : TEXCOORD1;
            float4 screenUV : TEXCOORD2;
            float3 posObject : TEXCOORD3;
        };

        float rand(float2 uv) { return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453123); }
        float noise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            float a = rand(i), b = rand(i + float2(1, 0)), c = rand(i + float2(0, 1)), d = rand(i + float2(1, 1));
            float2 u = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
        }
        float CausticsPattern(float2 p, float s)
        {
            float2 p1 = p + float2(sin(p.y * 1.2 + s), cos(p.x * 1.2 + s * 0.8));
            float2 p2 = p * 1.3 - float2(cos(p.y * 0.8 - s * 0.9), sin(p.x * 0.8 - s));
            return 1.0 - abs(noise(p1) - noise(p2));
        }
        // hard-edged caustics
        float Caustics(float2 xz)
        {
            float c = CausticsPattern(xz * _causticsScale, _Time.y * _causticsSpeed);
            return smoothstep(_causticsThreshold, _causticsThreshold + 0.02, c);
        }

        float3 DownWS() { return normalize(TransformObjectToWorldDir(_Down.xyz)); }
        // The surface: the point of the shape furthest against gravity.
        float3 SurfacePointWS() { return TransformObjectToWorld(-normalize(_Down.xyz) * 0.5); }

        // Caustic coordinates on the surface plane for a point below it (d = distance below the surface).
        float2 SurfaceCoords(float3 p, float3 down, float3 surface, out float d)
        {
            d = max(0, dot(p - surface, down));
            float3 onPlane = p - down * d;
            float3 u = normalize(abs(down.y) < 0.9 ? cross(down, float3(0, 1, 0)) : cross(down, float3(1, 0, 0)));
            float3 v = cross(down, u);
            return float2(dot(onPlane, u), dot(onPlane, v));
        }

        // entry/exit of the shape along a ray (object space)
        float2 RayShape(float3 ro, float3 rd)
        {
            if (_Shape > 0.5)
            {
                float b = dot(ro, rd), c = dot(ro, ro) - 0.25;
                float h = b * b - c;
                if (h < 0) return float2(1, 0);
                h = sqrt(h);
                return float2(-b - h, -b + h);
            }
            float3 inv = 1.0 / rd;
            float3 t0 = (-0.5 - ro) * inv, t1 = (0.5 - ro) * inv;
            float3 tmin = min(t0, t1), tmax = max(t0, t1);
            return float2(max(max(tmin.x, tmin.y), tmin.z), min(min(tmax.x, tmax.y), tmax.z));
        }

        float Shafts(float3 posWorld, out float thickness)
        {
            float3 down = DownWS(), surface = SurfacePointWS();
            float3 ro = TransformWorldToObject(GetCameraPositionWS());
            float3 rd = normalize(TransformWorldToObject(posWorld) - ro);
            float2 hit = RayShape(ro, rd);
            float tStart = max(hit.x, 0);
            float tEnd = max(hit.y, tStart);
            float stepLen = (tEnd - tStart) / SHAFT_STEPS;
            float t = tStart + stepLen * 0.5;
            float sum = 0;
            [loop]
            for (int k = 0; k < SHAFT_STEPS; k++)
            {
                float3 p = TransformObjectToWorld(ro + rd * t);
                float d;
                float2 s = SurfaceCoords(p, down, surface, d);
                sum += Caustics(s) * exp(-d * _shaftFalloff);
                t += stepLen;
            }
            thickness = distance(TransformObjectToWorld(ro + rd * tStart), TransformObjectToWorld(ro + rd * tEnd));
            return sum / SHAFT_STEPS * thickness;
        }

        V vert(A v)
        {
            V o;
            float3 p = v.vertex.xyz;
            // Floating blobs breathe and shiver.
            float w = sin(p.x * 9 + _Time.y * 2.1) * sin(p.y * 7 - _Time.y * 1.7) + sin(p.z * 8 + _Time.y * 2.6) * 0.5;
            p += v.normal * w * _Wobble;
            o.posObject = p;
            o.posWorld = TransformObjectToWorld(p);
            o.pos = TransformWorldToHClip(o.posWorld);
            o.normal = TransformObjectToWorldNormal(v.normal);
            o.screenUV = ComputeScreenPos(o.pos);
            return o;
        }

        // Procedural ripples bend the normal a little.
        float3 Ripple(float3 n, float3 posWorld)
        {
            float2 q = posWorld.xz * _rippleScale;
            float2 g = float2(noise(q + _Time.y * float2(0.9, 0.2)) - noise(q + float2(0.7, 0) + _Time.y * float2(0.9, 0.2)),
                              noise(q * 1.7 - _Time.y * float2(0.5, -0.2)) - noise(q * 1.7 + float2(0, 0.7) - _Time.y * float2(0.5, -0.2)));
            return normalize(n + float3(g.x, 0, g.y) * _normalIntensity * 3);
        }
        ENDHLSL

        // The inside: drawn first, from the back faces.
        Pass
        {
            Name "Volume"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragVolume
            float4 fragVolume(V i) : SV_Target
            {
                float2 screenUV = i.screenUV.xy / i.screenUV.w;
                float3 n = Ripple(normalize(-i.normal), i.posWorld);
                float3 background = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, screenUV + n.xz * _refractionIntensity).rgb;

                float thickness;
                float rays = Shafts(i.posWorld, thickness);

                // dark when deeper
                float depth01 = floor(saturate(thickness / _depthRange) * _depthBands) / _depthBands;
                float3 color = lerp(background * _shallowColor.rgb, _deepColor.rgb, depth01);

                // caustics on the far side, glowing by themselves
                float d;
                float2 s = SurfaceCoords(i.posWorld, DownWS(), SurfacePointWS(), d);
                color += _causticsColor.rgb * Caustics(s) * _causticsIntensity * exp(-d * _shaftFalloff * 0.5) * _Glow;

                // banded shafts
                rays = floor(rays * _shaftIntensity * _shaftBands) / _shaftBands;
                color += rays * _causticsColor.rgb * _shallowColor.rgb * _Glow;
                return float4(color, 1);
            }
            ENDHLSL
        }

        // The surface: toon lit, see-through in the middle, opaque at the rim, hard highlight.
        Pass
        {
            Name "Surface"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite Off
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragSurface
            float4 fragSurface(V i) : SV_Target
            {
                float3 normal = Ripple(normalize(i.normal), i.posWorld);
                float3 lightDir = normalize(_LightDir.xyz);
                float3 viewDir = normalize(GetCameraPositionWS() - i.posWorld);
                float3 halfDir = normalize(viewDir + lightDir);

                // two-colour on direction
                float lit = step(0.2, dot(normal, lightDir));
                float3 color = _surfaceColor.rgb * lerp(0.75, 1, lit);

                // some transparent
                float fresnel = 1 - saturate(dot(normal, viewDir));
                float alpha = lerp(_opacity, 0.85, step(0.7, fresnel));

                // some highlight, and a thin glowing rim so the shape reads in the dark
                float spec = step(1 - _specularSize, dot(normal, halfDir));
                float3 glow = _causticsColor.rgb * step(0.82, fresnel) * 0.35 * _Glow;
                return float4(color * alpha + spec + glow, alpha);
            }
            ENDHLSL
        }
    }
}
