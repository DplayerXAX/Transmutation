Shader "Capstone/ArtDirection/PosterPattern"
{
    // Flat graphic-poster look for the habitat block: two-step flat shading, a black ink outline, and one
    // bold pattern laid over the surface in object space (no UVs needed):
    // 0 stripes, 1 polka dots, 2 mosaic tiles, 3 wavy bands, 4 leopard / terrazzo spots, 5 starfield.
    Properties
    {
        [Enum(Stripes,0,Dots,1,Mosaic,2,Waves,3,Spots,4,Stars,5)] _Pattern("Pattern", Float) = 0
        _ColorA("Base", Color) = (0.93, 0.88, 0.78, 1)
        _ColorB("Pattern", Color) = (0.05, 0.05, 0.06, 1)
        _ColorC("Accent", Color) = (0.85, 0.2, 0.22, 1)
        _InkColor("Ink", Color) = (0.03, 0.03, 0.04, 1)
        _Scale("Pattern Repeats Per Metre", Float) = 3
        _Fill("Pattern Fill (stripe width, dot size)", Range(0.05, 0.95)) = 0.45
        _Accent("Accent Share", Range(0, 1)) = 0.2
        _InkLine("Ink Line Width (pixels)", Range(0, 4)) = 1.2
        _Outline("Outline Width (metres)", Range(0, 0.1)) = 0.025
        _ShadowTint("Shadow Brightness", Range(0, 1)) = 0.62
        _Glow("Self Light", Range(0, 1)) = 0.25
        _Seed("Seed", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _Pattern;
            half4 _ColorA, _ColorB, _ColorC, _InkColor;
            float _Scale, _Fill, _Accent, _InkLine, _Outline, _ShadowTint, _Glow, _Seed;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float3 normalOS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs v = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = v.positionCS;
                output.positionWS = v.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                // Pattern space: object space scaled to metres, so big and small pieces share one pattern size.
                float3 scale = float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), length(UNITY_MATRIX_M._m02_m12_m22));
                output.positionOS = input.positionOS.xyz * scale;
                output.normalOS = input.normalOS;
                output.fogFactor = ComputeFogFactor(v.positionCS.z);
                return output;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7)) + _Seed * 17.13) * 43758.5453); }
            float2 Hash2(float2 p) { return float2(Hash(p), Hash(p + 19.19)); }

            // An ink line where dist (in pattern units) reaches 0, about _InkLine pixels wide; rate is the
            // screen-space change of the continuous pattern coordinate.
            half Ink(float dist, float rate)
            {
                // Far away the pattern gets finer than a few pixels: fade the ink so it does not turn to black.
                half fade = saturate((0.14 - rate) / 0.07);
                return (1.0 - smoothstep(0.0, max(rate, 1e-5) * _InkLine, abs(dist))) * fade;
            }
            float Edge(float f, float fill) { return min(min(f, 1.0 - f), abs(f - fill)); }

            half3 Pattern(float2 p, out half ink)
            {
                int mode = (int)round(_Pattern);
                ink = 0;
                if (mode == 0)
                {
                    // Bold diagonal stripes, every few an accent stripe.
                    float s = p.x + p.y * 0.6;
                    float f = frac(s), band = floor(s);
                    ink = Ink(Edge(f, _Fill), fwidth(s));
                    half3 b = Hash(float2(band, 3)) < _Accent ? _ColorC.rgb : _ColorB.rgb;
                    return f < _Fill ? b : _ColorA.rgb;
                }
                if (mode == 1)
                {
                    // Staggered polka dots, a few in the accent colour.
                    float2 q = p;
                    q.x += step(0.5, frac(q.y * 0.5)) * 0.5;
                    float2 cell = floor(q), f = frac(q) - 0.5;
                    float r = length(f), radius = _Fill * 0.5;
                    ink = Ink(r - radius, fwidth(r));
                    half3 dotColor = Hash(cell) < _Accent ? _ColorC.rgb : _ColorB.rgb;
                    return r < radius ? dotColor : _ColorA.rgb;
                }
                if (mode == 2)
                {
                    // Mosaic tiles of three colours (and a darker shade), with ink joints.
                    float2 cell = floor(p), f = frac(p);
                    float h = Hash(cell);
                    ink = max(Ink(min(f.x, 1.0 - f.x), fwidth(p.x)), Ink(min(f.y, 1.0 - f.y), fwidth(p.y)));
                    half3 c = h < 0.33 ? _ColorA.rgb : h < 0.66 ? _ColorB.rgb : _ColorC.rgb;
                    return Hash(cell + 7.7) < 0.25 ? c * 0.72 : c;
                }
                if (mode == 3)
                {
                    // Wavy bands like poured paint, ink between them, every third an accent.
                    float v = p.y + sin(p.x * 1.7 + _Seed) * 0.35 + sin(p.x * 3.9 + p.y * 0.5) * 0.12;
                    float f = frac(v), band = floor(v);
                    ink = Ink(Edge(f, _Fill), fwidth(v));
                    half3 c = fmod(abs(band), 3.0) < 1.0 ? _ColorC.rgb : _ColorB.rgb;
                    return f < _Fill ? c : _ColorA.rgb;
                }
                if (mode == 4)
                {
                    // Leopard / terrazzo: irregular blobs (cellular), inked rims, small accent cores.
                    float2 cell = floor(p), f = frac(p);
                    float best = 8.0, second = 8.0;
                    float2 bestCell = 0;
                    for (int y = -1; y <= 1; y++)
                        for (int x = -1; x <= 1; x++)
                        {
                            float2 o = float2(x, y), c = o + 0.15 + 0.7 * Hash2(cell + o);
                            float d = length(f - c);
                            if (d < best) { second = best; best = d; bestCell = cell + o; }
                            else if (d < second) second = d;
                        }
                    float radius = _Fill * 0.5 * (0.7 + 0.6 * Hash(bestCell + 3.1));
                    ink = Ink(best - radius, fwidth(p.x) + fwidth(p.y));
                    if (best < radius * 0.35 && Hash(bestCell + 9.3) < _Accent) return _ColorC.rgb;
                    return best < radius ? _ColorB.rgb : _ColorA.rgb;
                }
                // Starfield: dark field, white specks, rare accent stars.
                float2 cell = floor(p * 3.0), f = frac(p * 3.0) - 0.5;
                float h = Hash(cell);
                float2 at = (Hash2(cell + 5.5) - 0.5) * 0.6;
                float size = h < 0.08 ? 0.16 : h < 0.45 ? 0.06 : 0.0;
                float d = length(f - at);
                if (d < size) return h < 0.03 ? _ColorC.rgb : _ColorB.rgb;
                return _ColorA.rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalOS);
                float3 a = abs(n);
                float3 p = input.positionOS * _Scale + _Seed * 3.7;
                // Pick the plane facing the normal most (no blending: flat graphic patterns stay crisp).
                float2 uv = a.y > a.x && a.y > a.z ? p.xz : a.x > a.z ? p.zy : p.xy;
                half ink;
                half3 color = Pattern(uv, ink);

                float3 normalWS = normalize(input.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half lit = step(0.2, saturate(dot(normalWS, light.direction)) * light.shadowAttenuation);
                color *= lerp(_ShadowTint, 1.0, lit) * lerp(1.0, 1.15, _Glow);
                color = lerp(color, _InkColor.rgb, saturate(ink));
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        // Ink outline: the back faces pushed out along the normal.
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                output.positionCS = TransformWorldToHClip(positionWS + normalWS * _Outline);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return half4(_InkColor.rgb, 1); }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input) { Varyings o; o.positionCS = TransformObjectToHClip(input.positionOS.xyz); return o; }
            half4 Frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
