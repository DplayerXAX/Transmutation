Shader "Capstone/ArtDirection/Crystal"
{
    // Physically motivated crystal for the habitat block, two kinds:
    // Specular: a clear, high-index faceted gem. Flat facet normals (from screen derivatives), Schlick Fresnel
    //   with a high F0, a tight GGX highlight from the sun, the sky reflected in the facets, and the scene behind
    //   refracted through each facet with per-channel dispersion; the body absorbs a little with thickness
    //   (Beer-Lambert), so thick parts are tinted and edges stay clear.
    // Fluorescent: a mineral that absorbs light and re-emits it in its own colour (uranium glass, fluorite):
    //   the glow follows the light it receives plus a constant share, is strongest through thick parts, and
    //   shows through a faint refraction of what is behind; a thin specular skin stays on top.
    // Needs the URP opaque texture for refraction (on in PC_RPAsset; the habitat camera also asks for it).
    Properties
    {
        [Enum(Specular,0,Fluorescent,1)] _Kind("Kind", Float) = 0
        _Tint("Body Tint (absorption)", Color) = (0.85, 0.92, 1, 1)
        [HDR] _Glow("Fluorescent Colour", Color) = (0.3, 1, 0.4, 1)
        _GlowAmount("Fluorescence", Range(0, 4)) = 1.5
        _F0("Reflectance At Normal Incidence", Range(0.02, 0.3)) = 0.12
        _Roughness("Roughness", Range(0.02, 0.5)) = 0.06
        _Refraction("Refraction (screen fraction)", Range(0, 0.2)) = 0.08
        _Dispersion("Dispersion", Range(0, 1)) = 0.5
        _Absorb("Absorption", Range(0, 4)) = 1
        _Facets("Flat Facets", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-20" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Crystal"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Kind;
                half4 _Tint, _Glow;
                float _GlowAmount, _F0, _Roughness, _Refraction, _Dispersion, _Absorb, _Facets;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 screen : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            float GGX(float nh, float roughness)
            {
                float a = roughness * roughness, a2 = a * a;
                float d = nh * nh * (a2 - 1.0) + 1.0;
                return a2 / (PI * d * d + 1e-5);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 smooth = normalize(i.normalWS);
                float3 flat = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                flat *= sign(dot(flat, smooth) + 1e-5);
                float3 n = normalize(lerp(smooth, flat, _Facets));
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float nv = saturate(dot(n, v));
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                float3 l = sun.direction, h = normalize(l + v);
                float nl = saturate(dot(n, l)), nh = saturate(dot(n, h));
                float shadow = sun.shadowAttenuation;

                // Fresnel (Schlick): how much of the light the surface reflects at this angle.
                float fresnel = _F0 + (1.0 - _F0) * pow(1.0 - nv, 5.0);
                // Thickness seen through: thin at grazing edges, thick face-on.
                float thickness = 0.3 + 0.7 * nv;

                // Refraction through the facet, each channel bent by its own index.
                float2 bend = mul((float3x3)UNITY_MATRIX_V, n).xy * _Refraction;
                float2 uv = i.screen.xy / i.screen.w;
                float3 ior = 1.0 + float3(-1.0, 0.0, 1.0) * _Dispersion;
                half3 through;
                through.r = SampleSceneColor(uv - bend * ior.r).r;
                through.g = SampleSceneColor(uv - bend * ior.g).g;
                through.b = SampleSceneColor(uv - bend * ior.b).b;
                half3 absorbed = pow(max(_Tint.rgb, 1e-3), _Absorb * thickness);

                // Reflection: the sky in the facet and the sun's sharp highlight.
                float3 r = reflect(-v, n);
                half3 sky = GlossyEnvironmentReflection(r, i.positionWS, _Roughness, 1.0);
                half3 highlight = sun.color * GGX(nh, _Roughness) * nl * shadow * 0.25;

                // Inside a high-index gem most light bounces back out (total internal reflection): the ray enters,
                // bends, mirrors off the back facets and leaves towards the sky. Each channel bends by its own index,
                // so the sky comes back split into colours: the gem's "fire".
                float3 back = -reflect(-n, flat);
                half3 inner;
                inner.r = GlossyEnvironmentReflection(reflect(refract(-v, n, 1.0 / (2.3 - _Dispersion * 0.08)), back), i.positionWS, _Roughness, 1.0).r;
                inner.g = GlossyEnvironmentReflection(reflect(refract(-v, n, 1.0 / 2.3), back), i.positionWS, _Roughness, 1.0).g;
                inner.b = GlossyEnvironmentReflection(reflect(refract(-v, n, 1.0 / (2.3 + _Dispersion * 0.08)), back), i.positionWS, _Roughness, 1.0).b;
                // The sun caught inside: a sharp highlight off the back facets.
                float3 innerDir = reflect(refract(-v, n, 1.0 / 2.3), back);
                half3 innerSun = sun.color * pow(saturate(dot(innerDir, l)), 200.0) * shadow * 4.0;

                half3 colour;
                if (_Kind < 0.5)
                {
                    half3 body = through * 0.45 + inner * 0.9 + innerSun;
                    colour = body * absorbed * (1.0 - fresnel) + (sky + highlight) * fresnel + highlight * 0.5;
                }
                else
                {
                    // Fluorescence: re-emitted light, fed by what the crystal receives (sun + a constant share
                    // standing in for ambient / UV), strongest through thick parts; behind shows faintly through.
                    float received = 0.35 + 0.65 * saturate(dot(smooth, l) * 0.5 + 0.5) * lerp(0.4, 1.0, shadow);
                    half3 emitted = _Glow.rgb * _GlowAmount * received * (0.35 + 0.65 * thickness);
                    colour = through * absorbed * 0.25 + emitted * (1.0 - fresnel * 0.5) + (sky * 0.5 + highlight) * fresnel + highlight * 0.3;
                }
                return half4(colour, 1);
            }
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input) { Varyings o; o.positionCS = TransformObjectToHClip(input.positionOS.xyz); return o; }
            half4 Frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
