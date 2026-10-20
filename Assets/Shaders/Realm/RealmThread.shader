Shader "Capstone/Realm/RealmThread"
{
    // Light threads strung between lamps and the tower: a pale line with bright beads of light running along it.
    // Mesh data: uv0.x = 0 at the base .. 1 at the tip, uv0.y = random per piece,
    // uv1.x = around (0..1) or across (-1..1), uv1.y = metres along the piece.
    Properties
    {
        _Color("Thread", Color) = (0.72, 0.74, 0.78, 1)
        _PulseColor("Bead", Color) = (1, 1, 1, 1)
        _PulseSpacing("Bead Spacing (m)", Float) = 7
        _PulseLength("Bead Tail (m)", Float) = 0.8
        _PulseSpeed("Bead Speed (m/s)", Float) = 2.2
        _Prism("Prism Colour", Range(0, 1)) = 0.6
        [Header(Motion)]
        _Sway("Sway (metres at tip)", Float) = 0
        _SwaySpeed("Sway Speed", Float) = 0.6
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull [_Cull]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half4 _PulseColor;
            float _PulseSpacing;
            float _PulseLength;
            float _PulseSpeed;
            half _Prism;
            float _Sway;
            float _SwaySpeed;
            float _Cull;
        CBUFFER_END

        // Gentle sway that grows towards the tip (uv.x).
        float3 RealmAnimate(float3 positionWS, float2 uv)
        {
            float t = uv.x;
            float phase = uv.y * 6.2831853 + dot(positionWS.xz, float2(0.13, 0.07));
            positionWS.xz += float2(sin(_Time.y * _SwaySpeed + phase), cos(_Time.y * _SwaySpeed * 0.8 + phase * 1.3)) * _Sway * t * t;
            return positionWS;
        }

        float RealmHash(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.yzx + 33.33);
            return frac((p.x + p.y) * p.z);
        }

        // Smooth value noise 0..1 in 2D.
        float MarbleNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = RealmHash(float3(i, 0.0)), b = RealmHash(float3(i + float2(1, 0), 0.0));
            float c = RealmHash(float3(i + float2(0, 1), 0.0)), d = RealmHash(float3(i + float2(1, 1), 0.0));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }
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
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Shaders/World/LineWorldLighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float2 uv1 : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = RealmAnimate(TransformObjectToWorld(input.positionOS.xyz), input.uv);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.uv1 = input.uv1;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half3 Shade(Varyings input, half3 normalWS, half3 viewDirWS, float2 screenUV)
            {
                // uv1.y = metres along the thread; each thread is offset by its random value.
                float s = (input.uv1.y - _Time.y * _PulseSpeed + input.uv.y * 17.0) / max(_PulseSpacing, 0.1);
                float behind = frac(s) * _PulseSpacing;
                half bead = exp(-behind / max(_PulseLength, 0.05));
                // Prismatic filament: pastel colours drift along it; a brighter sleeve at the edges.
                half facing = saturate(dot(normalWS, viewDirWS));
                float hue = input.uv1.y * 0.09 + input.uv.y + _Time.y * 0.04;
                half3 prism = 0.5h + 0.5h * cos(6.2831853 * (hue + half3(0.0h, 0.33h, 0.67h)));
                half3 color = lerp(_Color.rgb, prism, _Prism);
                color = lerp(color * 0.75h, half3(1, 1, 1), pow(1.0h - facing, 2.0h) * 0.6h);
                return lerp(color, _PulseColor.rgb * 1.4h, bead);
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS) * IS_FRONT_VFACE(isFront, 1.0, -1.0);
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                half3 color = Shade(input, normalWS, viewDirWS, screenUV);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0);
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

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS = RealmAnimate(TransformObjectToWorld(input.positionOS.xyz), input.uv);
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
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };

            float4 DepthVert(Attributes input) : SV_POSITION
            {
                return TransformWorldToHClip(RealmAnimate(TransformObjectToWorld(input.positionOS.xyz), input.uv));
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
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(RealmAnimate(TransformObjectToWorld(input.positionOS.xyz), input.uv));
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                return half4(PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5)), 0.0);
            #else
                return half4(normalWS, 0.0);
            #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
