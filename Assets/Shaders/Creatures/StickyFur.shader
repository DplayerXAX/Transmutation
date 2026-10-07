Shader "Capstone/Creatures/StickyFur"
{
    // Shell fur. The same blob mesh is drawn once per shell; each shell is pushed out along the normal
    // and keeps only the pixels that belong to a strand. Strands thin towards the tip, droop under their
    // own weight and drag behind the creature's motion, so the fur reads as damp and clumped.
    // Driven per renderer: _Shell (0 = skin, 1 = outermost shell) and _Velocity (world, already negated).
    Properties
    {
        _RootColor("Root Color", Color) = (0.03, 0.03, 0.035, 1)
        _TipColor("Tip Color", Color) = (0.9, 0.9, 0.88, 1)
        _ShadeLevels("Shade Levels", Range(1, 6)) = 3
        _ShadeStrength("Shade Strength", Range(0, 1)) = 0.35

        [Header(Fur)]
        _FurLength("Fur Length", Range(0, 0.4)) = 0.14
        _Density("Strand Density", Float) = 26
        _Thickness("Strand Thickness", Range(0.05, 1)) = 0.75
        _Clumping("Clumping", Range(0, 1)) = 0.6
        _Droop("Droop (gravity)", Range(0, 0.3)) = 0.09
        _Drag("Motion Drag", Range(0, 0.1)) = 0.025

        [Header(Wet Sheen)]
        _HighlightSize("Highlight Size", Range(0, 0.3)) = 0.06

        [Header(Driven By Script)]
        _Shell("Shell (0..1)", Range(0, 1)) = 0
        _Velocity("Drag Velocity (WS)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _RootColor;
            half4 _TipColor;
            half _ShadeLevels;
            half _ShadeStrength;
            float _FurLength;
            float _Density;
            half _Thickness;
            half _Clumping;
            float _Droop;
            float _Drag;
            half _HighlightSize;
            float _Shell;
            float4 _Velocity;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float3 surface : TEXCOORD0;
        };

        float Hash31(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return frac((p.x + p.y) * p.z);
        }

        float3 ShellPositionWS(Attributes input)
        {
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            float3 normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
            float h = _Shell;
            // Outer shells bend more: gravity plus drag behind the motion.
            float bend = h * h;
            positionWS += normalWS * (_FurLength * h);
            positionWS += float3(0, -_Droop, 0) * bend;
            positionWS += _Velocity.xyz * (_Drag * bend);
            return positionWS;
        }

        // Keeps a pixel only if it lies on a strand at this shell's height.
        void ClipFur(float3 surface)
        {
            if (_Shell <= 0.0) return;
            float3 p = normalize(surface) * _Density;
            float3 cell = floor(p);
            float3 local = frac(p) - 0.5;
            float strand = Hash31(cell);
            // Clumps: neighbouring strands share length, so the fur sticks together in tufts.
            float clump = Hash31(floor(p / 3.0) + 7.0);
            float strandLength = lerp(strand, clump, _Clumping) * 0.6 + 0.4;
            clip(strandLength - _Shell);
            float radius = _Thickness * 0.5 * (1.0 - _Shell / max(strandLength, 1e-3));
            clip(radius - length(local));
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
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

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 surface : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = ShellPositionWS(input);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.surface = input.surface;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                ClipFur(input.surface);

                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));

                half ndotl = saturate(dot(normalWS, mainLight.direction) * 0.5 + 0.5) * mainLight.shadowAttenuation;
                half levels = max(_ShadeLevels, 1.0);
                half stepped = floor(ndotl * levels + 0.5) / levels;
                half shade = lerp(1.0 - _ShadeStrength, 1.0 + _ShadeStrength, stepped);

                // Dark roots, pale tips, with a hard step so it matches the posterized world.
                half tip = step(0.55, _Shell);
                half3 color = lerp(_RootColor.rgb, _TipColor.rgb, lerp(_Shell * 0.5, 1.0, tip)) * shade;

                // A flat wet glint on the skin and lower fur.
                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                half glint = step(1.0 - _HighlightSize, saturate(dot(normalWS, halfDir))) * step(_Shell, 0.3);
                color = lerp(color, _TipColor.rgb, glint);

                return half4(MixFog(color, input.fogFactor), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 surface : TEXCOORD0;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                float3 positionWS = ShellPositionWS(input);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
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
                output.positionCS = positionCS;
                output.surface = input.surface;
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                ClipFur(input.surface);
                return 0;
            }
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

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 surface : TEXCOORD0;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(ShellPositionWS(input));
                output.surface = input.surface;
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                ClipFur(input.surface);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
