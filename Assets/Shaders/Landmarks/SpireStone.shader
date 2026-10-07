Shader "Capstone/Landmarks/SpireStone"
{
    // Built stone for the landmark spires: dark masonry courses with irregular block joints,
    // some courses carved with rows of small glyphs. A slow pulse of light climbs the tower and
    // lights the glyphs as it passes, so the spire reads from far away.
    // Pattern is wrapped around the object's Y axis (object space), so it follows the tower.
    // Mesh data: uv0.x = 0 at the base .. 1 at the top, uv0.y = random value per part.
    Properties
    {
        [MainColor] _Color("Lit Base Color", Color) = (0.13, 0.13, 0.14, 1)
        _ShadowColor("Shadow Base Color", Color) = (0.01, 0.01, 0.015, 1)
        _LineColor("Joint Line Color", Color) = (0.8, 0.8, 0.78, 1)
        _ShadowLineDim("Line Brightness In Shadow", Range(0, 1)) = 0.35
        _LineWidth("Line Width (pixels)", Range(0.3, 4)) = 1.1

        [Header(Lighting)]
        _MainLightAmount("Sun Amount", Range(0, 2)) = 1.2
        _FakeLightDir("Fill Light Direction", Vector) = (0.3, 1, 0.2, 0)
        _FakeLightAmount("Fill Light Amount", Range(0, 2)) = 0.15
        _ShadeLevels("Shade Levels", Range(1, 6)) = 2
        _RimPower("Rim Power", Range(0.5, 8)) = 4
        _RimStrength("Rim Strength", Range(0, 1)) = 0.3

        [Header(Hatching)]
        _HatchColor("Hatch Color", Color) = (0.4, 0.4, 0.42, 1)
        _HatchStrength("Hatch Strength", Range(0, 1)) = 0.8
        _HatchScale("Hatch Lines Per Metre", Float) = 5
        _HatchWidth("Hatch Width (pixels)", Range(0.3, 4)) = 1

        [Header(Masonry)]
        _ArcRadius("Pattern Radius (metres per radian)", Float) = 9
        _CourseHeight("Course Height (metres)", Float) = 1.6
        _BlockWidth("Block Width Range (metres)", Vector) = (1.2, 3.6, 0, 0)
        _CourseWave("Course Waviness", Range(0, 1)) = 0.25

        [Header(Script)]
        _ScriptChance("Carved Courses (outside)", Range(0, 1)) = 0.22
        _ScriptChanceInside("Carved Courses (inside)", Range(0, 1)) = 0.7
        _GlyphWidth("Glyph Width (metres)", Float) = 0.45
        _ScriptColor("Script Color", Color) = (0.62, 0.62, 0.6, 1)
        _GlowColor("Pulse Glow Color", Color) = (0.55, 0.9, 0.92, 1)
        _GlowStrength("Pulse Glow Strength", Range(0, 4)) = 1.8
        _PulseSpeed("Pulse Climb Speed (metres/s)", Float) = 3
        _PulseSpacing("Pulse Spacing (metres)", Float) = 60
        _PulseWidth("Pulse Width (metres)", Float) = 5
        _ScriptFlicker("Glyph Flicker", Range(0, 1)) = 0.25

        [Header(Glitch)]
        _GlitchAmount("Glitch Amount", Range(0, 1)) = 0.4
        _GlitchColorA("Glitch Colour A", Color) = (0.22, 0.68, 0.74, 1)
        _GlitchColorB("Glitch Colour B", Color) = (0.78, 0.24, 0.55, 1)
        _GlitchBands("Tear Bands Per Metre", Float) = 0.5
        _GlitchRate("Tear Changes Per Second", Float) = 3

        [Header(Haze)]
        _HazeColor("Haze Color (alpha = strength)", Color) = (0, 0, 0, 0)
        _HazeStart("Haze Start", Float) = 10
        _HazeEnd("Haze End", Float) = 80
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half4 _ShadowColor;
            half4 _LineColor;
            half _ShadowLineDim;
            half _LineWidth;
            half _MainLightAmount;
            float4 _FakeLightDir;
            half _FakeLightAmount;
            half _ShadeLevels;
            half _RimPower;
            half _RimStrength;
            half4 _HatchColor;
            half _HatchStrength;
            float _HatchScale;
            half _HatchWidth;
            float _ArcRadius;
            float _CourseHeight;
            float4 _BlockWidth;
            half _CourseWave;
            half _ScriptChance;
            half _ScriptChanceInside;
            float _GlyphWidth;
            half4 _ScriptColor;
            half4 _GlowColor;
            half _GlowStrength;
            float _PulseSpeed;
            float _PulseSpacing;
            float _PulseWidth;
            half _ScriptFlicker;
            half _GlitchAmount;
            half4 _GlitchColorA;
            half4 _GlitchColorB;
            float _GlitchBands;
            float _GlitchRate;
            half4 _HazeColor;
            float _HazeStart;
            float _HazeEnd;
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Shaders/World/LineWorldLighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float3 normalOS : TEXCOORD3;
                float2 uv : TEXCOORD4;
                half fogFactor : TEXCOORD5;
            };

            float Hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // Anti-aliased distance line: 1 on the line, fading over about one pixel.
            half Line(float d, float fw, half widthPixels)
            {
                return 1.0h - smoothstep(fw * widthPixels * 0.5, fw * (widthPixels * 0.5 + 1.0), abs(d));
            }

            // One small carved glyph per cell: a few ticks, bars, slashes and dots picked by hash.
            // cell = 0..1 inside the glyph box.
            half Glyph(float2 cell, float id, float2 fw)
            {
                float h1 = Hash11(id * 1.17 + 0.3);
                float h2 = Hash11(id * 2.31 + 1.7);
                float h3 = Hash11(id * 3.73 + 4.1);
                float h4 = Hash11(id * 5.19 + 7.9);
                float2 p = (cell - 0.5) * float2(1.0, 1.3); // glyphs are a little taller than wide
                half inside = step(abs(p.x), 0.38) * step(abs(p.y), 0.5);
                float fwMax = max(fw.x, fw.y);

                half marks = 0.0h;
                // Spine, often off centre.
                marks = max(marks, Line(p.x - (h1 - 0.5) * 0.4, fw.x, 1.4h) * step(0.25, h1) * step(abs(p.y), 0.45 - h2 * 0.2));
                // Bars at one or two heights.
                marks = max(marks, Line(p.y - (h2 - 0.5) * 0.6, fw.y, 1.4h) * step(abs(p.x), 0.15 + h3 * 0.2));
                marks = max(marks, Line(p.y + (h4 - 0.5) * 0.6, fw.y, 1.4h) * step(0.55, h4) * step(abs(p.x - 0.1), 0.18));
                // Slash.
                marks = max(marks, Line((p.x - p.y * (h3 > 0.5 ? 1.0 : -1.0)) * 0.7, fwMax, 1.4h) * step(0.6, h3) * step(abs(p.y), 0.3));
                // Dot.
                marks = max(marks, (1.0h - smoothstep(0.05, 0.05 + fwMax * 1.5, length(p - float2(h4 - 0.5, h1 - 0.5) * 0.5))) * step(h2, 0.45));
                return marks * inside;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);

                half ao;
                half light = LineWorldLight(input.positionWS, normalWS, screenUV,
                    _MainLightAmount, _FakeLightDir.xyz, _FakeLightAmount, _ShadeLevels, ao);
                half3 color = lerp(_ShadowColor.rgb, _Color.rgb, light) * lerp(0.6h, 1.0h, ao);
                half hatch = LineWorldHatch(input.positionWS, light, _HatchScale, _HatchWidth) * _HatchStrength;
                color = lerp(color, _HatchColor.rgb, hatch);

                // Unwrap around the tower: u = metres around, v = metres up.
                float3 p = input.positionOS;
                float angle = atan2(p.z, p.x);
                float u = angle * _ArcRadius;
                float v = p.y;
                // Wrap seam: atan2 jumps at -pi..pi, so derivatives would spike; use a continuous version for widths.
                float uAlt = atan2(-p.z, -p.x) * _ArcRadius;
                float fwU = min(fwidth(u), fwidth(uAlt));
                float fwV = fwidth(v);

                // Courses: horizontal joints, gently wavy so they never look ruled.
                float courseCoord = v / _CourseHeight + _CourseWave * 0.5 * sin(u * 0.21 + input.uv.y * 6.0);
                float course = floor(courseCoord);
                float courseFrac = frac(courseCoord);
                float fwCourse = fwV / _CourseHeight;
                half joints = Line(min(courseFrac, 1.0 - courseFrac), fwCourse, _LineWidth);

                // Blocks: each course has its own block width and offset.
                float width = lerp(_BlockWidth.x, _BlockWidth.y, Hash11(course * 3.17 + 0.5));
                float blockCoord = (u + Hash11(course * 7.31) * 13.0) / width;
                float blockFrac = frac(blockCoord);
                half vertical = Line(min(blockFrac, 1.0 - blockFrac), fwU / width, _LineWidth);
                // Not every vertical joint is cut, so long blocks appear.
                vertical *= step(0.3, Hash11(floor(blockCoord) * 1.93 + course * 5.7));
                joints = max(joints, vertical);

                // Carved script on some courses; the inside of the tower is mostly written on.
                half facingIn = step(dot(input.normalOS.xz, p.xz), 0.0);
                half chance = lerp(_ScriptChance, _ScriptChanceInside, facingIn);
                half carved = step(Hash11(course * 11.7 + 3.1), chance);
                float glyphCoord = u / _GlyphWidth;
                float glyphId = floor(glyphCoord) + course * 131.0;
                float2 cell = float2(frac(glyphCoord), saturate((courseFrac - 0.15) / 0.7));
                float2 fwCell = float2(fwU / _GlyphWidth, fwCourse / 0.7);
                half script = Glyph(cell, glyphId, fwCell) * carved * step(0.12, Hash11(glyphId * 0.37));
                // Fade glyphs out when too small on screen, so far towers do not shimmer.
                script *= saturate(1.5h - fwCell.x * 6.0h);

                // A pulse of light climbing the tower; glyphs light up as it passes.
                float pulsePos = frac((v - _Time.y * _PulseSpeed) / _PulseSpacing) * _PulseSpacing;
                half pulse = exp(-pow(min(pulsePos, _PulseSpacing - pulsePos) / max(_PulseWidth, 0.01), 2.0));
                half flicker = lerp(1.0h, step(0.5, Hash11(glyphId + floor(_Time.y * 6.0))), _ScriptFlicker * pulse);

                half rim = pow(1.0h - saturate(dot(normalWS, viewDirWS)), _RimPower) * _RimStrength;
                half lineLight = lerp(_ShadowLineDim, 1.0h, light);
                color = lerp(color, _LineColor.rgb * lineLight, max(joints * (1.0h - script), saturate(rim)));
                color = lerp(color, _ScriptColor.rgb * lineLight, script);
                // Glow ignores lighting so it reads in shadow and at night.
                color += _GlowColor.rgb * script * pulse * flicker * _GlowStrength;
                color += _GlowColor.rgb * joints * pulse * 0.25h * _GlowStrength;

                // Glitch: thin bands where the pulse light tears into the palette colours.
                float band = floor(v * _GlitchBands);
                float tick = floor(_Time.y * _GlitchRate);
                float h = Hash11(band * 1.37 + tick * 7.91);
                half tear = step(1.0 - _GlitchAmount * 0.08, h);
                half3 tearColor = Hash11(h * 91.7) < 0.5 ? _GlitchColorA.rgb : _GlitchColorB.rgb;
                half scan = step(0.5, frac(input.positionCS.y * 0.25));
                color = lerp(color, tearColor * lerp(0.3h, 0.75h, scan), tear * 0.6h);

                color = LineWorldHaze(color, input.positionWS, _HazeColor, _HazeStart, _HazeEnd);
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
