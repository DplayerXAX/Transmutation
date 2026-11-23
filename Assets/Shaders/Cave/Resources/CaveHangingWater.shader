// Clear water hanging under the cave rock (World/HangingLake) and its drops. It is laid over the real picture with very
// little coverage, so the rock stays sharp through it and the water fades out with no edge of its own: towards its rim,
// and wherever it comes close to rock (scene depth). The floor below lies mirrored in its surface (MirrorPool renders _MirrorTex for the nearest
// lake). The mesh carries how thick the water is (uv2.x): the view ray is bent in and walked through it for a light
// absorption and thin moving webs of light inside. uv.x = metres to the nearest pendant, uv.y = 0 at the rim .. 1 inside.
// _Tips: world x, z, seconds since a drop left, for rings across the surface. Not fogged. Needs the URP Depth Texture.
Shader "Capstone/Cave/HangingWater"
{
    Properties
    {
        _Tint("Clear Tint", Color) = (0.78, 0.95, 1, 1)
        _Deep("Depth Colour", Color) = (0.02, 0.1, 0.14, 1)
        [HDR] _Caustic("Light", Color) = (0.42, 1.0, 1.2, 1)
        _Absorb("Absorption", Range(0, 3)) = 0.3
        _WebScale("Webs Per Metre", Float) = 0.2
        _Speed("Speed", Float) = 0.3
        _Mirror("Mirror Strength", Range(0, 2)) = 1.3
        _MirrorOn("Has Mirror Image", Float) = 0
        _Solid("Whole Body (drops)", Float) = 0
        _SolidThickness("Whole Body Thickness", Float) = 0.14
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #define STEPS 3
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_MirrorTex); SAMPLER(sampler_MirrorTex);
            float4 _Tips[6];
            int _TipCount;
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint, _Deep, _Caustic;
                float _Absorb, _WebScale, _Speed, _Mirror, _MirrorOn, _Solid, _SolidThickness;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float4 uv : TEXCOORD2; float4 screen : TEXCOORD3; };

            V vert(A i)
            {
                V o;
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                float t = _Time.y * 0.5;
                p.y += sin(p.x * 0.6 + t) * sin(p.z * 0.7 - t * 0.8) * 0.025 * i.uv.y * (1 - _Solid);
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = float4(i.uv, i.uv2);
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            float Web(float2 uv, float time)
            {
                const float TAU = 6.28318;
                float2 p = frac(uv) * TAU - 250.0;
                float2 q = p;
                float c = 1.0;
                for (int n = 0; n < 3; n++)
                {
                    float s = time * (1.0 - 3.5 / (n + 1));
                    q = p + float2(cos(s - q.x) + sin(s + q.y), sin(s - q.y) + cos(s + q.x));
                    c += 1.0 / length(float2(p.x / (sin(q.x + s) / 0.005), p.y / (cos(q.y + s) / 0.005)));
                }
                c = 1.17 - pow(c / 3.0, 1.4);
                return saturate(pow(abs(c), 10.0));
            }

            half4 frag(V i) : SV_Target
            {
                float3 view = normalize(GetCameraPositionWS() - i.positionWS);
                float3 n = normalize(i.normalWS);
                if (dot(n, view) < 0) n = -n;
                float t = _Time.y;
                float inner = lerp(i.uv.y, 1, _Solid);
                float3 p = i.positionWS;
                n = normalize(n + float3(sin(p.x * 2.3 + t * 0.7) + sin(p.z * 3.1 - t * 0.6), 0,
                                         cos(p.z * 2.7 + t * 0.6) + sin(p.x * 1.9 + t * 0.5)) * 0.012 * inner);

                // Rings from the tips drops have just left.
                float2 ring = 0;
                for (int k = 0; k < _TipCount; k++)
                {
                    float2 to = p.xz - _Tips[k].xy;
                    float d = length(to) + 0.001, front = _Tips[k].z * 1.3;
                    ring += to / d * sin((d - front) * 13.0) * exp(-abs(d - front) * 3.0) * exp(-_Tips[k].z * 1.4) * 0.012;
                }

                float thickness = lerp(i.uv.z, _SolidThickness, _Solid);
                float3 ray = refract(-view, n, 0.75);
                if (dot(ray, ray) < 0.01) ray = -view;
                float path = min(thickness / max(abs(ray.y), 0.22), thickness * 3.5 + 0.2);

                float webs = 0;
                for (int k = 0; k < STEPS; k++)
                {
                    float s = (k + 0.5) / STEPS * path;
                    float3 q = p + ray * s;
                    float2 w = q.xz + float2(0.35, 0.2) * q.y;
                    w = float2(w.x * 0.8 - w.y * 0.6, w.x * 0.6 + w.y * 0.8);
                    w += float2(sin(w.y * 0.31 + 1.3) + sin(w.y * 0.13), cos(w.x * 0.27) + sin(w.x * 0.11 + 2.1)) * 2.4;
                    webs += Web(w * _WebScale, t * _Speed + 23.0);
                }
                webs = webs / STEPS * (1 - exp(-path * 2.5));

                float3 absorbed = exp(-_Absorb * path * float3(1.2, 0.35, 0.2));
                float2 screen = i.screen.xy / i.screen.w;
                half glow = lerp(lerp(0.3, 1.0, exp(-i.uv.x * 0.3)), 1.0, _Solid);
                half facing = saturate(dot(n, view));
                // The curled rim would catch the most: it stays calm so the edge does not draw itself.
                half fresnel = pow(1 - facing, 3) * lerp(smoothstep(0.25, 0.9, i.uv.y), 1, _Solid);

                half3 color = _Deep.rgb * (1 - absorbed.g) + _Caustic.rgb * webs * glow * 0.4;
                half cover = 1 - absorbed.g * (_Tint.r + _Tint.g + _Tint.b) / 3;
                if (_MirrorOn > 0.5)
                {
                    half3 mirror = SAMPLE_TEXTURE2D(_MirrorTex, sampler_MirrorTex, screen + n.xz * 0.02 + ring * 1.5).rgb;
                    half amount = 0.45 + 0.55 * fresnel;
                    color += mirror * _Mirror * amount;
                    cover = 1 - (1 - cover) * (1 - amount * 0.6);
                }
                color += _Caustic.rgb * fresnel * lerp(0.12, 0.7, _Solid) * glow;
                float3 halfway = normalize(view + normalize(float3(0.3, -0.8, 0.2)));
                color += pow(saturate(dot(n, halfway)), 320) * 0.35 * glow;
                cover = lerp(cover, 0.15 + fresnel * 0.5, _Solid);

                // No edge: it thins away towards the rim and as it nears the rock behind it.
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screen), _ZBufferParams);
                float eye = -TransformWorldToView(p).z;
                half nearRock = saturate((sceneEye - eye) / 0.6);
                half fade = lerp(smoothstep(0.02, 0.8, i.uv.y) * (1 - exp(-thickness * 6.0)) * nearRock * nearRock * (3 - 2 * nearRock), 1, _Solid);
                // Seen edge-on, the body's outline dissolves too.
                fade *= lerp(smoothstep(0.04, 0.42, facing), 1, _Solid);
                color *= fade;
                half alpha = saturate(cover) * fade;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
