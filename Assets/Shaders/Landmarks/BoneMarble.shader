Shader "Capstone/Landmarks/BoneMarble"
{
    // Bone as poured, marbled liquid: black and white bands swirl through the bone and drift slowly
    // (domain-warped noise in world space, so moving ribs swim through the pattern). Where bands meet,
    // the colour channels split apart into thin red / cyan / blue fringes. Heat (BoneKiln) glows in the seams.
    Properties
    {
        [Header(Marble)]
        _ColorA("Light Band", Color) = (0.95, 0.94, 0.92, 1)
        _ColorB("Dark Band", Color) = (0.03, 0.03, 0.035, 1)
        _ColorMid("Middle Band", Color) = (0.45, 0.46, 0.5, 1)
        _Scale("Swirl Scale (per metre)", Float) = 0.11
        _Warp("Warp Strength", Range(0, 8)) = 3.2
        _Bands("Bands", Range(1, 12)) = 4
        _Sharpness("Band Edge Sharpness", Range(1, 40)) = 14
        _Flow("Flow Speed", Float) = 0.035

        [Header(Flesh)]
        _FleshColor("Flesh Band", Color) = (0.5, 0.06, 0.08, 1)
        _FleshDark("Flesh Band (between beats)", Color) = (0.24, 0.02, 0.03, 1)
        _FleshShare("Flesh Bands (share)", Range(0, 1)) = 0
        _PulseRate("Heartbeat (beats per second)", Float) = 0.7

        [Header(Colour Fringes)]
        _Fringe("Fringe Width", Range(0, 0.2)) = 0.012
        _FringeStrength("Fringe Strength", Range(0, 2)) = 0.9
        _FringeA("Fringe Colour A", Color) = (1, 0.15, 0.1, 1)
        _FringeB("Fringe Colour B", Color) = (0.1, 0.9, 1, 1)
        _FringeC("Fringe Colour C", Color) = (0.15, 0.25, 1, 1)

        [Header(Light)]
        _ShadeAmount("Shading", Range(0, 1)) = 0.45
        _RimStrength("Rim", Range(0, 1)) = 0.35

        [Header(Heat)]
        _GlowColor("Seam Glow Color", Color) = (0.15, 0.75, 0.8, 1)
        _GlowStrength("Seam Glow Strength", Range(0, 4)) = 0.25
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _ColorA;
            half4 _ColorB;
            half4 _ColorMid;
            float _Scale;
            float _Warp;
            float _Bands;
            float _Sharpness;
            float _Flow;
            half4 _FleshColor;
            half4 _FleshDark;
            half _FleshShare;
            float _PulseRate;
            float _Fringe;
            half _FringeStrength;
            half4 _FringeA;
            half4 _FringeB;
            half4 _FringeC;
            half _ShadeAmount;
            half _RimStrength;
            half4 _GlowColor;
            half _GlowStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fog : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i + float3(0, 0, 0)), Hash(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            float Fbm(float3 p)
            {
                float sum = 0.0, amp = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    sum += Noise(p) * amp;
                    p = p * 2.03 + float3(1.7, 9.2, 3.1);
                    amp *= 0.5;
                }
                return sum;
            }

            // Marble value at a point: noise warped by noise warped by noise, drifting with time.
            float Marble(float3 p, float t)
            {
                float3 q = float3(Fbm(p + float3(0.0, t, 0.0)), Fbm(p + float3(5.2, 1.3, t)), Fbm(p + float3(t, 2.8, 7.1)));
                float3 r = float3(Fbm(p + _Warp * q + float3(1.7, 9.2, 0.0)), Fbm(p + _Warp * q + float3(8.3, 2.8, 4.4)), 0.0);
                return Fbm(p + _Warp * float3(r.xy, r.x * 0.5) + t * 0.5);
            }

            half3 BandColour(float index)
            {
                float which = fmod(index, 3.0);
                half3 colour = which < 0.5 ? _ColorA.rgb : (which < 1.5 ? _ColorB.rgb : _ColorMid.rgb);
                // Some bands are raw flesh, swelling darker and redder with a slow heartbeat.
                float flesh = step(frac(sin(index * 12.9898) * 43758.5453), _FleshShare);
                float beat = pow(0.5 + 0.5 * sin(_Time.y * _PulseRate * 6.2832), 6.0);
                return lerp(colour, lerp(_FleshDark.rgb, _FleshColor.rgb, beat), flesh);
            }

            // Banded colour: light, dark and grey bands in turn, each blending into the next at a sharp edge.
            half3 Bands(float v)
            {
                float x = v * _Bands + 10.0;
                float band = frac(x);
                float index = floor(x);
                float blend = smoothstep(1.0 - 1.0 / _Sharpness, 1.0, band);
                return lerp(BandColour(index), BandColour(index + 1.0), blend);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _Time.y * _Flow;
                float3 p = input.positionWS * _Scale;


                float v = Marble(p, t);
                // The channels read the marble a little apart: thin coloured fringes at the band edges.
                float vr = v + _Fringe;
                float vb = v - _Fringe;
                half3 colour = Bands(v);
                half3 split = half3(Bands(vr).r, colour.g, Bands(vb).b);
                half fringe = saturate(length(split - colour) * 2.0) * _FringeStrength;
                // Tint the fringe from red through cyan to blue along the band.
                float hue = frac(v * _Bands * 1.7);
                half3 fringeColour = hue < 0.33 ? _FringeA.rgb : (hue < 0.66 ? _FringeB.rgb : _FringeC.rgb);
                colour = lerp(colour, fringeColour, fringe * 0.85);
                colour = lerp(colour, split, 0.2);

                // Light: soft shading so the bones keep their shape, plus a pale rim.
                half3 normalWS = normalize(input.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half ndl = saturate(dot(normalWS, light.direction)) * light.shadowAttenuation;
                half shade = lerp(1.0 - _ShadeAmount, 1.0, ndl);
                colour *= shade;
                half3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = pow(1.0 - saturate(dot(normalWS, viewDir)), 3.0) * _RimStrength;
                colour += rim * lerp(_FringeB.rgb, _ColorA.rgb, 0.5);

                // Heat glows in the seams between bands.
                float bandPos = frac(v * _Bands + 10.0);
                float seam = 1.0 - saturate(min(bandPos, 1.0 - bandPos) * _Sharpness * 0.5);
                colour += _GlowColor.rgb * seam * _GlowStrength;

                colour = MixFog(colour, input.fog);
                return half4(colour, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOct = saturate(octNormalWS * 0.5 + 0.5);
                return half4(PackFloat2To888(remappedOct), 0.0);
            #else
                return half4(normalWS, 0.0);
            #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
