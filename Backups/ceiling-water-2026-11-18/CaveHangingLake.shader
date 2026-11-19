// An upside-down lake: a sheet of water draped under cave rock (mesh from World/HangingLake). Water creeps across it
// towards the hanging drops: hard-edged toon ripple bands travel in to each pendant, over a dark see-through body,
// with thin bright crests. uv.x = metres to the nearest pendant, uv.y = 0 at the rim .. 1 inside. Not fogged, so a
// hanging lake shows from far away in the dark. Needs the URP Opaque Texture.
Shader "Capstone/Cave/HangingLake"
{
    Properties
    {
        _Deep("Deep Colour", Color) = (0.02, 0.1, 0.2, 1)
        _Shallow("Band Colour", Color) = (0.08, 0.42, 0.62, 1)
        [HDR] _Crest("Crest Colour", Color) = (0.55, 1.5, 1.8, 1)
        _Scale("Ripples Per Metre", Float) = 1.5
        _Speed("Flow Speed", Float) = 1.5
        _Bands("Bands", Range(1, 6)) = 3
        _Refraction("Refraction", Range(0, 0.1)) = 0.025
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_CameraOpaqueTexture); SAMPLER(sampler_CameraOpaqueTexture);
            CBUFFER_START(UnityPerMaterial)
                half4 _Deep, _Shallow, _Crest;
                float _Scale, _Speed, _Bands, _Refraction;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; float4 screen : TEXCOORD3; };

            float Wander(float3 p) { return sin(p.x * 0.9 + p.z * 0.4) * 0.7 + sin(p.z * 1.3 - p.x * 0.5) * 0.5; }

            V vert(A i)
            {
                V o;
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                // The skin of the water swells as the ripples pass, more in the open than at the rim.
                p.y += sin(i.uv.x * _Scale * 6.283 + _Time.y * _Speed * 2 + Wander(p)) * 0.035 * i.uv.y;
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv;
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float wave = frac(i.uv.x * _Scale + _Time.y * _Speed * 0.32 + Wander(i.positionWS) * 0.18);
                float band = floor(abs(wave * 2 - 1) * _Bands) / _Bands;
                float aa = max(fwidth(wave) * 1.5, 0.01);
                float crest = 1 - smoothstep(0.035, 0.035 + aa, abs(wave - 0.5));

                float2 uv = i.screen.xy / i.screen.w + float2(cos(wave * 6.283), sin(wave * 6.283)) * _Refraction;
                half3 behind = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv).rgb;
                half3 color = lerp(_Deep.rgb + behind * 0.35, _Shallow.rgb, band);
                float3 view = normalize(GetCameraPositionWS() - i.positionWS);
                half rim = pow(1 - saturate(abs(dot(normalize(i.normalWS), view))), 3);
                color += _Crest.rgb * (crest * 0.8 + rim * 0.25);
                // A dim glow: strongest at the points it drips from, sinking into the dark away from them.
                color *= lerp(0.1, 0.8, exp(-i.uv.x * 0.42));
                return half4(color, smoothstep(0, 0.35, i.uv.y) * 0.94);
            }
            ENDHLSL
        }
    }
}
