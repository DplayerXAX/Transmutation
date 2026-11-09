Shader "Capstone/World/FlowingInkFloor"
{
    Properties
    {
        [MainColor] _PaperColor("Paper Color", Color) = (0.96,0.955,0.94,1)
        _InkColor("Ink Color", Color) = (0.025,0.028,0.032,1)
        _PatternScale("Pattern Scale (per metre)", Float) = 0.65
        _Angle("Direction (degrees)", Range(-180,180)) = 32
        _LaneWidth("Column Spacing", Range(0.5,5)) = 1.8
        _DashWidth("Dash Width", Range(0.02,0.4)) = 0.12
        _DashLength("Dash Length", Range(0.1,3)) = 0.8
        _DashGap("Dash Gap", Range(0.1,4)) = 1.3
        _DashSpeedMin("Column Speed Min", Float) = 0.08
        _DashSpeedMax("Column Speed Max", Float) = 0.32
        _DotRadius("Dot Radius", Range(0.02,0.4)) = 0.16
        _DotSpacing("Average Dot Spacing", Range(1,12)) = 4.5
        _DotSpeedMin("Dot Speed Min", Float) = 0.05
        _DotSpeedMax("Dot Speed Max", Float) = 0.45
        _Clearance("Dot To Line Clearance", Range(0.01,0.5)) = 0.12
        _GrainScale("Grain Density", Range(20,300)) = 90
        _GrainStrength("Grain Strength", Range(0,1)) = 0.98
        _Seed("Random Seed", Float) = 17
        _ShadowStrength("Received Shadow Strength", Range(0,1)) = 0.55
        [HideInInspector] _BaseColor("Base Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "FlowingInk"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _PaperColor, _InkColor, _BaseColor;
                float _PatternScale, _Angle, _LaneWidth, _DashWidth, _DashLength, _DashGap;
                float _DashSpeedMin, _DashSpeedMax, _DotRadius, _DotSpacing;
                float _DotSpeedMin, _DotSpeedMax, _Clearance, _GrainScale, _GrainStrength, _Seed, _ShadowStrength;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fog : TEXCOORD1; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            float Hash(float2 p)
            {
                float3 q = frac(float3(p.xyx) * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }
            float Ink(float distanceToEdge, float2 local, float identity)
            {
                float aa = max(fwidth(distanceToEdge), 0.001);
                float density = min(0.92, saturate(-distanceToEdge / 0.16 + 0.38));
                float grain = Hash(floor(local * _GrainScale) + identity);
                float rough = smoothstep(grain - 0.1, grain + 0.1, density);
                // Average unresolved grains in the distance to avoid sparkling.
                float footprint = max(length(ddx(local)), length(ddy(local))) * _GrainScale;
                rough = lerp(rough, density, smoothstep(1.5,4,footprint));
                return (1-smoothstep(-0.035-aa, 0.02+aa, distanceToEdge)) * lerp(1, rough, _GrainStrength);
            }
            half4 Frag(Varyings v) : SV_Target
            {
                float s, c;
                sincos(radians(_Angle), s, c);
                float2 p = mul(float2x2(c,-s,s,c), v.positionWS.xz) * max(_PatternScale,0.001);
                float width = max(_LaneWidth,0.5);
                float column = floor(p.x / width + 0.5);
                float columnID = Hash(float2(column, _Seed));
                float dashSpeed = lerp(_DashSpeedMin, _DashSpeedMax, columnID);
                float period = max(_DashLength + _DashGap,0.2);
                float2 dashP = float2(p.x - column * width,
                    (frac((p.y - _Time.y * dashSpeed) / period + columnID) - 0.5) * period);
                float2 box = abs(dashP) - float2(_DashWidth * 0.5, _DashLength * 0.5);
                float dashD = length(max(box,0)) + min(max(box.x,box.y),0) - 0.018;
                float ink = Ink(dashD, dashP, columnID * 137);

                float lane = floor(p.x / width);
                // Six persistent dots per long wrapping strip. Each dot retains its
                // own seed, lateral offset and speed, including across wrap seams.
                float stripLength = max(_DotSpacing,1) * 6;
                float radius = min(_DotRadius, max(0.01,(width - _DashWidth)*0.5 - _Clearance - 0.025));
                float margin = min(width*0.5, _DashWidth*0.5 + 0.025 + _Clearance + radius);
                [unroll] for (int i=0; i<6; i++)
                {
                    float2 id = float2(lane * 7 + i, _Seed + 91);
                    float r = Hash(id);
                    float x = lane*width + lerp(margin,width-margin,r);
                    float speed = lerp(_DotSpeedMin,_DotSpeedMax,Hash(id+23.7));
                    float y = (i + Hash(id+49.1)) * stripLength/6 + _Time.y*speed;
                    float dy = (frac((p.y-y)/stripLength+0.5)-0.5)*stripLength;
                    float2 dotP = float2(p.x-x,dy);
                    float dotD = length(dotP) - radius*lerp(0.8,1,r);
                    ink = max(ink,Ink(dotD,dotP,r*317));
                }
                half3 color = lerp(_PaperColor.rgb,_InkColor.rgb,ink);
                Light sun = GetMainLight(TransformWorldToShadowCoord(v.positionWS));
                color *= lerp(1-_ShadowStrength,1,sun.shadowAttenuation);
                return half4(MixFog(color,v.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack Off
}
