Shader "Capstone/Realm/RealmBeam"
{
    // The light pillar through the spire: additive, a white-hot core fading to a coloured edge,
    // bright streaks racing up it, and a slow pulse. Ignores fog so it pierces the haze.
    // Mesh data: uv1.x = around (0..1), uv1.y = metres up.
    Properties
    {
        _Core("Core", Color) = (1, 1, 1, 1)
        _Edge("Edge", Color) = (0.45, 0.85, 1, 1)
        _Intensity("Intensity", Range(0, 4)) = 1.6
        _StreakSpeed("Streak Speed (m/s)", Float) = 18
        _StreakScale("Streaks Per Metre", Float) = 0.25
        _Pulse("Pulse", Range(0, 1)) = 0.25
        _Fade("Fade (0 = off, 1 = on)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Beam"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Core;
                half4 _Edge;
                half _Intensity;
                float _StreakSpeed;
                float _StreakScale;
                half _Pulse;
                half _Fade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv1 : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv1 : TEXCOORD2; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv1 = input.uv1;
                return output;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                // Bright in the middle of the column as seen, fading at its silhouette.
                half facing = abs(dot(normalize(input.normalWS), viewDirWS));
                half core = pow(facing, 2.0h);
                // Streaks running up: thin columns around the beam, each with its own speed and phase.
                float column = floor(input.uv1.x * 24.0);
                float speed = _StreakSpeed * (0.7 + Hash(float2(column, 1.0)) * 0.6);
                float run = frac((input.uv1.y - _Time.y * speed) * _StreakScale + Hash(float2(column, 7.0)));
                half streak = smoothstep(0.85h, 1.0h, run) * step(0.4, Hash(float2(column, 3.0)));
                half pulse = 1.0h + _Pulse * sin(_Time.y * 2.2 - input.uv1.y * 0.05);
                half3 color = lerp(_Edge.rgb * 0.5h, _Core.rgb, core) * (0.35h + core * 0.65h) + _Core.rgb * streak * 0.8h;
                return half4(color * _Intensity * pulse * _Fade * (0.3h + 0.7h * facing), 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
