Shader "Capstone/Wind/Streak"
{
    // Cartoony wind for WindArea particles. Shape 0 (stretched billboards) draws an ink-outlined ribbon with a
    // fat head, a tapering wobbling tail that curls into a hook and breaks into dots, and a faint colour
    // fringe so it reads as something alive passing through rather than plain air. Shape 1 (round billboards)
    // draws drifting hollow seeds/spores. The per-particle seed comes from the StableRandom.x vertex stream
    // (TEXCOORD0.z), which WindVisuals turns on.
    Properties
    {
        [Enum(Streak,0,Mote,1)] _Shape("Shape", Float) = 0
        _Color("Core", Color) = (0.93, 0.97, 1, 0.9)
        _InkColor("Ink", Color) = (0.08, 0.1, 0.16, 1)
        _InkWidth("Ink Width", Range(0, 0.6)) = 0.28
        _Thickness("Ribbon Thickness", Range(0.05, 1)) = 0.55
        _Wiggle("Wiggle", Range(0, 0.6)) = 0.25
        _Waves("Waves Along Length", Range(0, 4)) = 1.3
        _WobbleSpeed("Wobble Speed", Range(0, 12)) = 5
        _Curl("Tail Curl", Range(-1, 1)) = 0.45
        _Dashes("Tail Dots", Range(0, 12)) = 5
        _DashSpeed("Dot Crawl", Range(0, 6)) = 2.2
        _Fringe("Colour Fringe", Range(0, 0.6)) = 0.22
        [Toggle] _LengthAlongV("Length Along V", Float) = 0
        // Unity's stretched billboards run U from the leading end back, so the default flip puts the head forward.
        [Toggle] _FlipLength("Flip Head And Tail", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "WindStreak"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Shape;
                half4 _Color;
                half4 _InkColor;
                float _InkWidth;
                float _Thickness;
                float _Wiggle;
                float _Waves;
                float _WobbleSpeed;
                float _Curl;
                float _Dashes;
                float _DashSpeed;
                float _Fringe;
                float _LengthAlongV;
                float _FlipLength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float3 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; half4 color : COLOR; float3 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half3 Hue(float h)
            {
                h = frac(h) * 6.0;
                return saturate(half3(abs(h - 3.0) - 1.0, 2.0 - abs(h - 2.0), 2.0 - abs(h - 4.0)));
            }

            half4 Streak(float2 uv, float seed, half4 tint)
            {
                float t = _LengthAlongV > 0.5 ? uv.y : uv.x;      // 0 tail .. 1 head (after the flip)
                float s = (_LengthAlongV > 0.5 ? uv.x : uv.y) * 2.0 - 1.0;
                if (_FlipLength > 0.5) t = 1.0 - t;

                float time = _Time.y;
                // The centre line wobbles along the length and the tail hooks to one side.
                float wobble = sin(t * 6.2832 * _Waves + seed * 31.0 - time * _WobbleSpeed) * _Wiggle * saturate(1.2 - t);
                float hook = _Curl * pow(saturate(1.0 - t), 3.0) * (seed > 0.5 ? 1.0 : -1.0);
                float centre = wobble + hook;

                // Fat round head, thin tail.
                float width = _Thickness * pow(saturate(t), 0.7) * (1.0 - smoothstep(0.88, 1.0, t)) + 0.04;
                float d = abs(s - centre) / width;

                // The thin end breaks into crawling dots.
                float dash = frac(t * _Dashes - time * _DashSpeed + seed * 7.0);
                float broken = t < 0.55 && dash > 0.62 ? 1.0 : 0.0;

                float aa = fwidth(d) * 1.5 + 1e-4;
                float body = (1.0 - smoothstep(1.0 - aa, 1.0, d)) * (1.0 - broken);
                float core = 1.0 - smoothstep(1.0 - _InkWidth - aa, 1.0 - _InkWidth, d);

                half3 colour = lerp(_InkColor.rgb, _Color.rgb * tint.rgb, core);
                half alpha = body * lerp(_InkColor.a, _Color.a, core);

                // A thin rainbow fringe just outside the ink, shifting with the seed.
                float fringe = smoothstep(1.0, 1.0 + _Fringe * 0.5, d) * (1.0 - smoothstep(1.0 + _Fringe * 0.5, 1.0 + _Fringe, d));
                fringe *= (1.0 - broken) * saturate(t * 2.0);
                colour = lerp(colour, Hue(seed + t * 0.6 + time * 0.1), saturate(fringe * (1.0 - body)));
                alpha = max(alpha, fringe * 0.45 * (1.0 - body));
                return half4(colour, alpha * tint.a);
            }

            half4 Mote(float2 uv, float seed, half4 tint)
            {
                float2 p = uv * 2.0 - 1.0;
                float time = _Time.y;
                // A hollow seed with a notch that turns, so drifting motes seem to tumble.
                float angle = atan2(p.y, p.x) + time * (1.5 + seed * 3.0) * (seed > 0.5 ? 1.0 : -1.0);
                float r = length(p) * (1.0 + 0.12 * sin(angle * 3.0));
                float aa = fwidth(r) * 1.5 + 1e-4;
                float outer = 1.0 - smoothstep(0.8 - aa, 0.8, r);
                float hole = smoothstep(0.32 - aa, 0.32, r);
                float notch = step(0.25, frac(angle / 6.2832));
                float ring = outer * hole * notch;
                float inkBand = smoothstep(0.8 - _InkWidth * 0.5 - aa, 0.8 - _InkWidth * 0.5, r);
                half3 colour = lerp(_Color.rgb * tint.rgb, _InkColor.rgb, inkBand);
                return half4(colour, ring * _Color.a * tint.a);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float seed = input.uv.z;
                half4 result = _Shape > 0.5 ? Mote(input.uv.xy, seed, input.color) : Streak(input.uv.xy, seed, input.color);
                clip(result.a - 0.003);
                return result;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
