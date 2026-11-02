// Generated board-only copy of Assets/Shaders/Effects/Resources/HeatHaze.shader. Rebuild with ArtFormationSetup.
Shader "Capstone/ArtFormation/Effects/HeatHaze"
{
    // Expanding shells of dots around a heat source, drawn by HeatHaze.cs.
    // Each shell is an evenly dotted sphere that swells outward from the body and fades; its surface folds
    // with slow waves, and crests carry bigger, brighter dots. Each dot quad's vertices hold the dot's unit
    // direction and its shell phase; they are placed and pushed out to camera-facing corners here.
    // A few horizontal bands of dots glitch into cyan / magenta now and then.
    Properties
    {
        _AFPalette("Board Palette", Color) = (1,1,1,1)
        _HeatColor("Heat Color", Color) = (1, 0.32, 0.08, 1)
        _DeepColor("Trough Color", Color) = (0.55, 0.02, 0.05, 1)
        _CrestColor("Crest Color", Color) = (1, 0.85, 0.45, 1)
        _CyanColor("Cyan", Color) = (0, 0.95, 1, 1)
        _MagentaColor("Magenta", Color) = (1, 0.1, 0.75, 1)
        _Inner("Start Radius", Float) = 0.4
        _Radius("End Radius", Float) = 2
        _Intensity("Intensity", Range(0, 1)) = 1
        _Fold("Fold Height", Float) = 0.15
        _Cycle("Seconds Per Shell", Float) = 3
        _DotSize("Dot Size", Float) = 0.03
        _Opacity("Opacity", Range(0, 1)) = 0.8
        _Glitch("Glitch", Range(0, 1)) = 0.5
        _Seed("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment ArtFormationFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _AFPalette;
                half4 _HeatColor, _DeepColor, _CrestColor, _CyanColor, _MagentaColor;
                float _Inner, _Radius, _Intensity, _Fold, _Cycle, _DotSize, _Opacity, _Glitch, _Seed;
            CBUFFER_END

            // position = unit direction of the dot, uv0 = quad corner, uv1.x = shell phase offset 0..1.
            struct Attributes { float4 positionOS : POSITION; float2 corner : TEXCOORD0; float2 shell : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                half4 color : COLOR;
                float glitch : TEXCOORD1;
                float3 surfaceOS : TEXCOORD2;
            };

            float Hash(float2 p)
            {
                float3 h = frac(float3(p.xyx) * 0.1031);
                h += dot(h, h.yzx + 33.33);
                return frac((h.x + h.y) * h.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.corner = 0;
                o.surfaceOS = input.positionOS.xyz;
                float phase = sin(dot(input.positionOS.xyz, float3(2.7,3.1,1.9)) - _Time.y * 6.2832 / max(_Cycle,.1) + _Seed) * .5 + .5;
                half3 c = lerp(_DeepColor.rgb, _HeatColor.rgb, phase);
                c = lerp(c, _CrestColor.rgb, saturate(phase*2-1));
                o.color = half4(c, _Opacity * lerp(.35,1,phase) * _Intensity);
                o.glitch = sin(input.positionOS.y*11+floor(_Time.y*6)+_Seed) > .93 ? _Glitch : 0;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = abs(normalize(cross(ddx(input.surfaceOS),ddy(input.surfaceOS))));
                float2 st = n.z > n.x && n.z > n.y ? input.surfaceOS.xy : (n.y > n.x ? input.surfaceOS.xz : input.surfaceOS.yz);
                float2 corner = frac(st * max(4.0, _Radius / max(.015,_DotSize) * .12)) * 2 - 1;
                float dotShape = 1 - smoothstep(.55,1,length(corner));
                float3 color = input.color.rgb;
                if (input.glitch > 0.5) color = _CyanColor.rgb;
                else if (input.glitch < -0.5) color = _MagentaColor.rgb;
                return half4(color, input.color.a * dotShape);
            }
            
#include "Assets/ArtFormation/Shaders/ArtFormationPalette.hlsl"
half4 ArtFormationFrag(Varyings input) : SV_Target
{
    return ArtFormationColor(Frag(input), _AFPalette);
}
ENDHLSL
        }
    }
}
