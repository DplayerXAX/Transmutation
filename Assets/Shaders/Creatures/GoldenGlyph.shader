Shader "Capstone/Creature/GoldenGlyph"
{
    // Glyph creatures as liquid gold glass. The body bends the scene behind it (refraction through the
    // URP opaque texture) and tints it amber, threads of light drift inside like sunlight in honey,
    // a hot gold rim and a sharp sun glint sit on the surface, and fine glitter twinkles as the view moves.
    // HDR values let the project's bloom catch the rim and glints. _Glow (per bead) or vertex colour red
    // (merged surface, _VertexGlow on) brightens it while it is part of a written glyph.
    Properties
    {
        [HideInInspector] _CreatureHurt("Health hurt", Range(0,1)) = 0
        [HDR] _GoldColor("Gold Rim", Color) = (2.6, 1.7, 0.6, 1)
        _AmberColor("Amber Body Tint", Color) = (1, 0.62, 0.22, 1)
        [HDR] _LightColor("Inner Light", Color) = (2.2, 1.5, 0.7, 1)
        _Refraction("Refraction", Range(0, 0.2)) = 0.06
        _BodyDensity("Body Density", Range(0, 1)) = 0.55
        _RimPower("Rim Sharpness", Range(0.5, 8)) = 2.8
        _Threads("Inner Light Threads", Range(0, 3)) = 1.2
        _ThreadScale("Thread Scale", Range(1, 40)) = 9
        _Glitter("Glitter", Range(0, 3)) = 1
        _Specular("Sun Glint", Range(0, 12)) = 6
        _Glow("Glyph Glow", Range(0, 1)) = 0
        [Toggle] _VertexGlow("Glow From Vertex Colour (merged surface)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "GoldenGlyph"
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _CreatureHurt;
                half4 _GoldColor;
                half4 _AmberColor;
                half4 _LightColor;
                half _Refraction;
                half _BodyDensity;
                half _RimPower;
                half _Threads;
                half _ThreadScale;
                half _Glitter;
                half _Specular;
                half _Glow;
                half _VertexGlow;
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
                float4 screen : TEXCOORD2;
                float fog : TEXCOORD3;
                half glow : TEXCOORD4;
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
                output.screen = ComputeScreenPos(position.positionCS);
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

                // The scene behind, bent by the curved surface and stained amber; thicker toward the middle.
                float2 uv = input.screen.xy / input.screen.w;
                float3 nView = TransformWorldToViewDir(n);
                float2 bent = uv - nView.xy * _Refraction * (0.4 + facing);
                half3 behind = SampleSceneColor(bent);
                half thickness = facing * facing;
                half3 body = behind * lerp(half3(1, 1, 1), _AmberColor.rgb, saturate(_BodyDensity + thickness * 0.4h));

                // Threads of light drifting inside: ridges of two slow noise layers.
                float3 p = input.positionWS * _ThreadScale;
                float a = Noise(p + float3(0, t * 0.25, t * 0.1));
                float b = Noise(p * 1.7 - float3(t * 0.15, 0, t * 0.2));
                half threads = pow(saturate(1.0 - abs(a - b) * 2.0), 6.0) * _Threads * (0.3h + thickness);
                // Molten core: the body itself gives off a warm light, strongest where it is thickest.
                half core = 0.18h + 0.55h * thickness;

                // Glitter: tiny cells that flash as the view direction sweeps across them.
                float3 cell = floor(input.positionWS * 140.0);
                float spark = Hash(cell + floor(v * 6.0));
                half glitter = step(0.994, spark) * _Glitter * (0.5h + 0.5h * sin(t * 6.0 + spark * 40.0));

                Light sun = GetMainLight();
                float3 h = normalize(sun.direction + v);
                half glint = pow(saturate(dot(n, h)), 220.0h) * _Specular;
                half sheen = pow(saturate(dot(n, h)), 18.0h) * 0.6h;

                half glow = input.glow;
                half3 colour = body;
                colour += _LightColor.rgb * (core * (0.7h + glow * 0.6h) + threads * (0.6h + glow));
                colour += _GoldColor.rgb * (rim * (0.8h + glow * 0.6h) + sheen);
                colour += (glint + glitter) * _GoldColor.rgb * sun.color;
                colour += _LightColor.rgb * glow * 0.25h;
                colour = lerp(colour, half3(1.5h, 0.3h, 0.2h), _CreatureHurt * 0.6h);
                colour = MixFog(colour, input.fog);
                half alpha = saturate(0.82h + rim * 0.18h);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
