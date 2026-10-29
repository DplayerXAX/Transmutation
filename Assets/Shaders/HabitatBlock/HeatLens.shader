Shader "Capstone/ArtDirection/HeatLens"
{
    // Hot, unstable matter as a lens: the scene behind (the URP opaque texture) is refracted through the surface,
    // each colour channel with its own index of refraction so the edges split into orange / blue dispersion
    // fringes, like light through molten glass. The surface itself slowly melts (vertex noise), carries sharp
    // pink-white highlight bands and a red glow at grazing angles. Needs the opaque texture (on in PC_RPAsset;
    // the habitat camera also asks for it).
    Properties
    {
        _Strength("Refraction (screen fraction)", Range(0, 0.2)) = 0.06
        _Dispersion("Dispersion (spread between channels)", Range(0, 1)) = 0.45
        _Shimmer("Heat Shimmer", Range(0, 1)) = 0.35
        _Melt("Melt (metres)", Range(0, 0.5)) = 0.12
        _Speed("Speed", Float) = 0.5
        [HDR] _Glow("Edge Glow", Color) = (1, 0.15, 0.05, 1)
        [HDR] _Highlight("Highlight", Color) = (1, 0.85, 0.85, 1)
        _Tint("Through Tint", Color) = (1, 0.82, 0.78, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Lens"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            Cull Back
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Strength, _Dispersion, _Shimmer, _Melt, _Speed;
                half4 _Glow, _Highlight, _Tint;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 screen : TEXCOORD2;
            };

            float Hash3(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }
            float Noise3(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash3(i), n100 = Hash3(i + float3(1, 0, 0)), n010 = Hash3(i + float3(0, 1, 0)), n110 = Hash3(i + float3(1, 1, 0));
                float n001 = Hash3(i + float3(0, 0, 1)), n101 = Hash3(i + float3(1, 0, 1)), n011 = Hash3(i + float3(0, 1, 1)), n111 = Hash3(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y), lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float t = _Time.y * _Speed;
                // Melting: the surface swells and sags with slow rising noise.
                float melt = Noise3(positionWS * 1.3 + float3(0, -t, 0)) - 0.5;
                positionWS += normalWS * melt * _Melt * 2.0;
                o.positionWS = positionWS;
                o.normalWS = normalWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                // Hot air: the normal itself wavers, so the refraction shimmers.
                float3 shimmer = float3(Noise3(i.positionWS * 4.0 + float3(0, -t * 3.0, 0)), Noise3(i.positionWS * 4.0 + 17.0 + float3(0, -t * 3.0, 0)), 0) - 0.5;
                float3 bent = normalize(n + shimmer * _Shimmer);
                float facing = saturate(dot(bent, v));
                // Screen-space bend: the view-space normal, stronger towards the silhouette (thicker glass there).
                float2 dir = mul((float3x3)UNITY_MATRIX_V, bent).xy;
                float thickness = 1.0 - facing * 0.6;
                float2 uv = i.screen.xy / i.screen.w;
                float3 ior = 1.0 + float3(-1.0, 0.0, 1.0) * _Dispersion;
                half3 behind;
                behind.r = SampleSceneColor(uv - dir * _Strength * thickness * ior.r).r;
                behind.g = SampleSceneColor(uv - dir * _Strength * thickness * ior.g).g;
                behind.b = SampleSceneColor(uv - dir * _Strength * thickness * ior.b).b;
                half3 colour = behind * _Tint.rgb;
                // Sharp highlight bands (a soft key light reflected in the folds) and red glow at the rim.
                float3 r = reflect(-v, bent);
                float band = pow(saturate(dot(r, normalize(float3(-0.4, 0.8, -0.45)))), 24.0);
                float band2 = pow(saturate(sin(dot(r, float3(3.0, 5.0, 2.0)) * 3.0 + t) * 0.5 + 0.5), 30.0) * 0.6;
                float fresnel = pow(1.0 - facing, 3.0);
                colour += _Highlight.rgb * (band + band2) + _Glow.rgb * fresnel;
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
}
