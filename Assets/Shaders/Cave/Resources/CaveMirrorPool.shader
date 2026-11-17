// A perfectly still cave pool that mirrors what is above it (MirrorPool renders the mirrored view into _MirrorTex),
// so the lit rock and stalactites seem to hang upside down in it. A faint slow ripple, a thin dark-teal tint, more
// mirror at grazing angles, and a soft fade at its edge into the floor. Without a mirror image (another pool is the
// one being mirrored) it shows a dark glossy tint only.
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
                half3 mirror = SAMPLE_TEXTURE2D(_MirrorTex, sampler_MirrorTex, uv + wobble).rgb * _MirrorOn;
                float3 view = normalize(GetCameraPositionWS() - i.positionWS);
                half fresnel = 0.55 + 0.45 * pow(1 - saturate(view.y), 3);
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
