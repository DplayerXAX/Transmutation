Shader "Capstone/Creature/WaterDroplet"
{
    // A glyph creature's body: a bead of dark, oily ink. Its rim runs with thin-film rainbow colours that
    // drift over the surface, tiny sparks float inside it, and the sun leaves one sharp highlight.
    // With _Iridescence and _Motes at 0 it is a plain clear water bead. _Glow (per droplet, or vertex
    // colour red on a merged surface) brightens it while it is part of a written glyph.
    Properties
    {
        [HideInInspector] _CreatureHurt("Health hurt", Range(0,1)) = 0
        _DeepColor("Deep Colour", Color) = (0.015, 0.015, 0.03, 1)
        _RimColor("Rim Colour", Color) = (0.75, 0.95, 1, 1)
        _GlowColor("Glyph Glow Colour", Color) = (0.55, 0.95, 1, 1)
        _CoreAlpha("Core Opacity", Range(0, 1)) = 0.72
        _RimAlpha("Rim Opacity", Range(0, 1)) = 0.95
        _RimPower("Rim Sharpness", Range(0.5, 8)) = 2.6
        _Specular("Highlight", Range(0, 4)) = 1.6
        _Glow("Glyph Glow", Range(0, 1)) = 0
        _Iridescence("Thin-film Rainbow", Range(0, 2)) = 1.1
        _FilmScale("Film Bands", Range(0.2, 6)) = 2.2
        _FilmDrift("Film Drift Speed", Range(0, 1)) = 0.12
        _Motes("Inner Sparks", Range(0, 2)) = 0.9
        [Toggle] _VertexGlow("Glow From Vertex Colour (merged surface)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Droplet"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _CreatureHurt;
                half4 _DeepColor;
                half4 _RimColor;
                half4 _GlowColor;
                half _CoreAlpha;
                half _RimAlpha;
                half _RimPower;
                half _Specular;
                half _Glow;
                half _VertexGlow;
                half _Iridescence;
                half _FilmScale;
                half _FilmDrift;
                half _Motes;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float fog : TEXCOORD2;
                half glow : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fog = ComputeFogFactor(position.positionCS.z);
                output.glow = _VertexGlow > 0.5h ? input.color.r : _Glow;
                return output;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n = normalize(input.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                half facing = saturate(dot(n, v));
                half rim = pow(1.0h - facing, _RimPower);
                float t = _Time.y;

                Light sun = GetMainLight();
                float3 h = normalize(sun.direction + v);
                half highlight = pow(saturate(dot(n, h)), 90.0h) * _Specular;
                half caustic = pow(saturate(dot(-n, sun.direction)), 6.0h) * 0.35h;

                // Thin film: the colour depends on the film's thickness, which grows toward the rim and
                // wanders slowly over the surface, so bands of rainbow drift across the ink.
                float film = (1.0 - facing) * _FilmScale + Noise(input.positionWS * 2.3 + t * _FilmDrift) * 1.4 + t * _FilmDrift * 0.5;
                half3 rainbow = 0.5h + 0.5h * cos(6.2831853 * (film + float3(0.0, 0.33, 0.67)));
                half filmMask = saturate(rim * 1.6h + 0.12h);

                // Sparks floating inside the ink, seen through the front of the body.
                float3 sp = input.positionWS * 26.0 + float3(0.0, t * 0.35, t * 0.2);
                half sparks = pow(saturate(Noise(sp) * 1.15 - 0.15), 14.0) * 6.0 * facing * _Motes;

                half3 colour = lerp(_DeepColor.rgb, _RimColor.rgb, rim * (1.0h - saturate(_Iridescence)));
                colour += rainbow * filmMask * _Iridescence * 0.85h;
                colour += sparks * lerp(half3(0.7h, 0.9h, 1.0h), rainbow, 0.5h);
                colour += (highlight + caustic) * sun.color;
                half3 glowColour = lerp(_GlowColor.rgb, rainbow, 0.35h);
                colour = lerp(colour, colour + glowColour * 0.9h, input.glow);
                colour = lerp(colour, half3(1.0h, 0.25h, 0.2h), _CreatureHurt * 0.6h);
                half alpha = saturate(lerp(_CoreAlpha, _RimAlpha, rim) + highlight + sparks + input.glow * 0.25h);
                colour = MixFog(colour, input.fog);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
