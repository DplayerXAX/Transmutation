Shader "Capstone/Realm/RealmCrystal"
{
    // Floating crystals and roof films: dark glassy facets that each catch the light differently, bright edges, faint lines that seem to sit inside, and a thin-film fringe at grazing angles.
    // Mesh data: uv0.x = 0 at the base .. 1 at the tip, uv0.y = random per piece,
    // uv1.x = around (0..1) or across (-1..1), uv1.y = metres along the piece.
    Properties
    {
        _DeepColor("Deep", Color) = (0.07, 0.08, 0.1, 1)
        _MidColor("Facet", Color) = (0.45, 0.48, 0.55, 1)
        _EdgeColor("Edge", Color) = (1, 1, 1, 1)
        _FringeA("Film A", Color) = (0.3, 0.75, 0.8, 1)
        _FringeB("Film B", Color) = (0.78, 0.4, 0.7, 1)
        _FringeAmount("Film Amount", Range(0, 1)) = 0.35
        _Glint("Glint", Range(0, 2)) = 1
        _InnerLines("Inner Lines Per Metre", Float) = 5
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
            half4 _DeepColor;
            half4 _MidColor;
            half4 _EdgeColor;
            half4 _FringeA;
            half4 _FringeB;
            half _FringeAmount;
            half _Glint;
            float _InnerLines;
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
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDirWS)), 2.5h);
                // Every facet has its own brightness.
                float facet = RealmHash(round(normalWS * 6.0) + input.uv.y * 13.0);
                Light mainLight = GetMainLight();
                half glint = pow(saturate(dot(reflect(-viewDirWS, normalWS), mainLight.direction)), 40.0h) * _Glint;
                // Lines that shift with the view, so they read as being inside the glass.
                float3 inside = input.positionWS - viewDirWS * (0.4 + facet * 0.8);
                half lines = LineWorldStripe(dot(inside, float3(0.3, 1.0, 0.2)) * _InnerLines, 1.0h) * LineWorldDetail(input.positionWS);
                half3 color = lerp(_DeepColor.rgb, _MidColor.rgb, facet * 0.7);
                color += lines * 0.3h * _EdgeColor.rgb;
                color = lerp(color, _EdgeColor.rgb, saturate(fresnel * 0.8h + glint));
                half3 film = lerp(_FringeA.rgb, _FringeB.rgb, 0.5h + 0.5h * sin(fresnel * 12.0h + _Time.y * 0.5));
                color = lerp(color, film, smoothstep(0.3h, 0.8h, fresnel) * _FringeAmount);
                return color;
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
