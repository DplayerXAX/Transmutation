// A perfectly still cave pool that mirrors what is above it (MirrorPool renders the mirrored view into _MirrorTex),
// so the lit rock and stalactites seem to hang upside down in it. A faint slow ripple, a thin dark-teal tint, more
// mirror at grazing angles, and a soft fade at its edge into the floor. Without a mirror image (another pool is the
// one being mirrored) it shows a dark glossy tint only. On a ceiling it faces down; rings run out from the tips where
// drops let go (_Tips: world x, z, seconds since the drop).
Shader "Capstone/Cave/MirrorPool"
{
    Properties
    {
        _Tint("Water Tint", Color) = (0.05, 0.16, 0.2, 1)
        _Mirror("Mirror Strength", Range(0, 2)) = 1.4
        _MirrorOn("Has Mirror Image", Float) = 0
        _Ripple("Ripple", Range(0, 0.03)) = 0.004
        _EdgeFade("Edge Fade", Range(0.01, 0.5)) = 0.18
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MirrorTex); SAMPLER(sampler_MirrorTex);
            float4 _Tips[6];
            int _TipCount;
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Mirror, _MirrorOn, _Ripple, _EdgeFade;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float4 screen : TEXCOORD0; float3 positionWS : TEXCOORD1; float2 uv : TEXCOORD2; };
            V vert(A i)
            {
                V o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screen = ComputeScreenPos(o.positionCS);
                o.uv = i.uv;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;
                float t = _Time.y * 0.35;
                float2 wobble = float2(sin(i.positionWS.x * 1.7 + t) + sin(i.positionWS.z * 2.3 - t * 1.3),
                                       cos(i.positionWS.z * 1.9 + t * 0.8) + sin(i.positionWS.x * 2.1 + t * 1.1)) * _Ripple;
                for (int k = 0; k < _TipCount; k++)
                {
                    float2 to = i.positionWS.xz - _Tips[k].xy;
                    float d = length(to) + 0.001, front = _Tips[k].z * 1.3;
                    wobble += to / d * sin((d - front) * 13.0) * exp(-abs(d - front) * 3.0) * exp(-_Tips[k].z * 1.4) * 0.012;
                }
                half3 mirror = SAMPLE_TEXTURE2D(_MirrorTex, sampler_MirrorTex, uv + wobble).rgb * _MirrorOn;
                float3 view = normalize(GetCameraPositionWS() - i.positionWS);
                half fresnel = 0.55 + 0.45 * pow(1 - abs(view.y), 3);
                half3 color = _Tint.rgb * 0.4 + mirror * _Mirror * fresnel;
                // uv is centred on the pool (0..1 across): fade out at the rim.
                half r = length(i.uv - 0.5) * 2;
                half alpha = 1 - smoothstep(1 - _EdgeFade, 1, r);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
