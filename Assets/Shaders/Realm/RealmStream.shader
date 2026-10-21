Shader "Capstone/Realm/RealmStream"
{
    // A marbled stream, like poured fluid art: crisp bands of teal, cream, copper and black stretched along the flow and swirled by a slow warp, thin gold veins between the bands, a glossy wet highlight and the lamps reflected in it.
    // Mesh data: uv0.x = 0 at the base .. 1 at the tip, uv0.y = random per piece,
    // uv1.x = across (-1..1), uv1.y = continuous metres along the entire river, including render-section seams.
    Properties
    {
        _ColorA("Teal", Color) = (0.04, 0.42, 0.44, 1)
        _ColorB("Deep Teal", Color) = (0.01, 0.15, 0.18, 1)
        _ColorC("Cream", Color) = (0.96, 0.89, 0.76, 1)
        _ColorD("Copper", Color) = (0.78, 0.43, 0.14, 1)
        _ColorE("Black", Color) = (0.015, 0.015, 0.02, 1)
        _VeinColor("Gold Veins", Color) = (1, 0.8, 0.42, 1)
        _Bands("Bands Across", Float) = 5
        _Stretch("Stretch Along Flow", Float) = 0.18
        _Warp("Swirl", Range(0, 3)) = 1.4
        _FlowSpeed("Flow Speed (m/s)", Float) = 0.5
        _VeinWidth("Vein Width (px)", Range(0.3, 4)) = 1.3
        _Gloss("Gloss", Range(0, 2)) = 1
        _LampReflection("Lamp Reflections", Range(0, 3)) = 1
        [Header(Motion)]
        _Sway("Sway (metres at tip)", Float) = 0
        _SwaySpeed("Sway Speed", Float) = 0.6
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull [_Cull]
        Offset -1, -1

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _ColorA;
            half4 _ColorB;
            half4 _ColorC;
            half4 _ColorD;
            half4 _ColorE;
            half4 _VeinColor;
            float _Bands;
            float _Stretch;
            half _Warp;
            float _FlowSpeed;
            half _VeinWidth;
            half _Gloss;
            half _LampReflection;
            float _Sway;
            float _SwaySpeed;
            float _Cull;
        CBUFFER_END

        // Flow animates in the fragment shader. Keep the ground-fitted mesh fixed over the terrain.
        float3 RealmAnimate(float3 positionWS, float2 uv)
        {
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
                float along = input.uv1.y, across = input.uv1.x;
                float time = _Time.y;
                // Marble coordinates: stretched along the flow and carried downstream.
                float2 q = float2((along - time * _FlowSpeed) * _Stretch, across * 0.5);
                // Two rounds of domain warp give the poured, folded look.
                float2 w1 = float2(MarbleNoise(q * 1.7 + float2(3.1, 7.7)), MarbleNoise(q * 1.7 + float2(11.3, 1.9)));
                float2 w2 = float2(MarbleNoise(q * 3.1 + w1 * 1.5 + float2(5.5, 2.2) + time * 0.02), MarbleNoise(q * 3.1 + w1 * 1.5 + float2(1.3, 9.4)));
                float field = across * 0.5 + (w1.y - 0.5) * 0.6 * _Warp + (w2.x - 0.5) * 0.35 * _Warp;
                float bandCoord = field * _Bands;
                float band = floor(bandCoord);
                float within = frac(bandCoord);
                // Each band takes a colour from the palette; cream and copper are rarer, like veins of paint.
                float pick = RealmHash(float3(band, 3.7, 1.1));
                half3 color = pick < 0.34 ? _ColorA.rgb : pick < 0.58 ? _ColorB.rgb : pick < 0.72 ? _ColorE.rgb : pick < 0.88 ? _ColorC.rgb : _ColorD.rgb;
                // A soft gradient inside each band gives it body.
                color *= lerp(0.72h, 1.12h, smoothstep(0.0, 1.0, within));
                // Thin gold veins on the band edges, crisp at any distance.
                half vein = LineWorldStripe(bandCoord, _VeinWidth);
                color = lerp(color, _VeinColor.rgb, vein * 0.9h);
                // Wet gloss: the surface tilts with the bands, so highlights follow the marbling.
                half3 n = normalize(normalWS + half3((w2.x - 0.5h) * 0.25h, 0.0h, (w1.y - 0.5h) * 0.25h));
                half3 reflected = reflect(-viewDirWS, n);
                Light mainLight = GetMainLight();
                half3 shine = pow(saturate(dot(reflected, mainLight.direction)), 80.0h) * _Gloss;
                half fresnel = pow(1.0h - saturate(dot(n, viewDirWS)), 5.0h);
                shine += fresnel * 0.25h * _Gloss;
            #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                for (uint i = 0u; i < count; ++i)
                {
                    Light lamp = GetAdditionalLight(i, input.positionWS, half4(1, 1, 1, 1));
                    shine += lamp.color * pow(saturate(dot(reflected, lamp.direction)), 24.0h) * saturate(lamp.distanceAttenuation * 5.0h) * _LampReflection;
                }
            #endif
                color += shine;
                // A dark lip where the stream meets the ground.
                color = lerp(color, _ColorE.rgb, smoothstep(0.88, 1.0, abs(across)));
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
