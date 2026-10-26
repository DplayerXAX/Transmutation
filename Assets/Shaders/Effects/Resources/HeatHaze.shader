Shader "Capstone/Effects/HeatHaze"
{
    // Expanding shells of dots around a heat source, drawn by HeatHaze.cs.
    // Each shell is an evenly dotted sphere that swells outward from the body and fades; its surface folds
    // with slow waves, and crests carry bigger, brighter dots. Each dot quad's vertices hold the dot's unit
    // direction and its shell phase; they are placed and pushed out to camera-facing corners here.
    // A few horizontal bands of dots glitch into cyan / magenta now and then.
    Properties
    {
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
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
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
                float3 dir = normalize(input.positionOS.xyz);
                float t = _Time.y + _Seed;

                // Shell age: 0 when it leaves the body, 1 when it fades out at the end radius.
                float age = frac(input.shell.x + t / max(_Cycle, 0.1));
                float reach = lerp(_Inner, _Radius, lerp(0.45, 1, _Intensity));
                // Fast start, slow finish, like a pressure wave losing energy.
                float grow = 1 - pow(1 - age, 1.8);
                float radius = lerp(_Inner, reach, grow);

                // Slow folds over the shell surface: three wave trains along drifting axes.
                float3 a = normalize(float3(sin(t * 0.21 + _Seed), 0.6, cos(t * 0.17)));
                float3 b = normalize(float3(cos(t * 0.13 + 2), sin(t * 0.19), 0.7));
                float3 c = normalize(float3(0.5, cos(t * 0.11 + 4), sin(t * 0.23)));
                float wave = sin(dot(dir, a) * 7 - t * 1.4 + input.shell.x * 6.28) * 0.5
                           + sin(dot(dir, b) * 11 + t * 1.1) * 0.3
                           + sin(dot(dir, c) * 17 - t * 1.9) * 0.2;
                float crest = saturate(wave * 0.6 + 0.5);
                crest *= crest;
                radius += wave * _Fold * lerp(0.3, 1, grow) * lerp(0.4, 1, _Intensity);

                float3 centreWS = TransformObjectToWorld(dir * radius);
                // Dots grow a little with the shell so it stays a readable grid as it spreads.
                float size = _DotSize * lerp(0.4, 1.3, crest) * lerp(0.6, 1.2, grow);
                // Low heat shows only a sparse, small scatter of the dots; the full grid needs real heat.
                float heat = _Intensity * _Intensity;
                float density = lerp(0.12, 1, heat);
                float dotRandom = Hash(float2(dot(dir, float3(91.7, 13.3, 57.1)), input.shell.x * 31 + 7));
                size *= lerp(0.55, 1, _Intensity) * step(dotRandom, density);
                float3 view = TransformWorldToView(centreWS) + float3(input.corner * size, 0);
                o.positionCS = TransformWViewToHClip(view);
                o.corner = input.corner;

                // Glitch: a thin horizontal band of dots flashes to cyan / magenta and slides sideways.
                float tick = floor(_Time.y * 6 + _Seed);
                float band = floor((centreWS.y) / max(_Radius * 0.07, 1e-3));
                float roll = Hash(float2(band + _Seed * 13, tick));
                o.glitch = roll > 1 - 0.08 * _Glitch ? (Hash(float2(band, tick + 9)) > 0.5 ? 1 : -1) : 0;
                if (o.glitch != 0) o.positionCS.x += o.glitch * 0.01 * o.positionCS.w;

                // The far side of a shell is dimmer, so it reads as a sphere and not a flat disc.
                float3 toCamera = normalize(GetCameraPositionWS() - TransformObjectToWorld(float3(0, 0, 0)));
                float facing = dot(TransformObjectToWorldDir(dir), toCamera) * 0.5 + 0.5;

                float3 color = lerp(_DeepColor.rgb, _HeatColor.rgb, saturate(crest * 1.6));
                color = lerp(color, _CrestColor.rgb, saturate(crest * 2 - 1));
                float fade = smoothstep(0, 0.12, age) * pow(1 - age, 1.2) * lerp(0.25, 1, crest) * lerp(0.35, 1, facing);
                o.color = half4(color, fade * _Opacity * lerp(0.15, 1, heat));
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float dotShape = 1 - smoothstep(0.55, 1, length(input.corner));
                float3 color = input.color.rgb;
                if (input.glitch > 0.5) color = _CyanColor.rgb;
                else if (input.glitch < -0.5) color = _MagentaColor.rgb;
                return half4(color, input.color.a * dotShape);
            }
            ENDHLSL
        }
    }
}
