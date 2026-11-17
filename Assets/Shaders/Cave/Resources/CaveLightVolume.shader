// The light round a glowing cave creature, as a crisp volume and a mask instead of a soft sprite:
//   volume - the air inside the light's sphere glows in hard-edged nested shells (banded, like the water shafts),
//            clipped by the scene depth, so rock in front hides it and rock inside cuts it;
//   mask   - where the light's sphere meets a surface, that surface is lit in hard bands with a thin bright rim at the
//            edge of the light, so the light reads as a pool cut out of the dark.
// Drawn from the sphere's back faces, so it works with the camera inside it. When the camera is close to the light's
// middle (a carried Glim) the volume fades away and only the mask stays, so it never fogs the view.
// Not fogged. Needs the URP depth texture (on in PC_RPAsset).
Shader "Capstone/Cave/LightVolume"
{
    Properties
    {
        [HDR] _Color("Light Color", Color) = (0.2, 1, 1, 1)
        _Glow("Glow Amount", Range(0, 3)) = 1
        _Bands("Bands", Range(1, 8)) = 4
        _Density("Volume Density", Range(0, 2)) = 0.35
        _Mask("Surface Mask Strength", Range(0, 2)) = 0.6
        _Rim("Mask Rim", Range(0, 3)) = 1.2
        _NearFade("Fade Volume Near Camera (m)", Float) = 2.5
        _Rays("Shafts Instead Of Shells", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest Always
        Cull Front
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Glow, _Bands, _Density, _Mask, _Rim, _Rays;
                float _NearFade;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float4 screen : TEXCOORD1; };

            V vert(A i)
            {
                V o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            float Banded(float x) { return floor(saturate(x) * _Bands) / _Bands; }

            half4 frag(V i) : SV_Target
            {
                float3 centre = TransformObjectToWorld(float3(0, 0, 0));
                float radius = length(TransformObjectToWorld(float3(0.5, 0, 0)) - centre);
                float3 eye = GetCameraPositionWS();
                float3 rd = normalize(i.positionWS - eye);

                // Ray into the sphere.
                float3 oc = eye - centre;
                float b = dot(oc, rd), c = dot(oc, oc) - radius * radius;
                float h = b * b - c;
                if (h <= 0) return 0;
                h = sqrt(h);
                float t0 = max(-b - h, 0), t1 = -b + h;

                // Where the scene surface is along this ray.
                float2 uv = i.screen.xy / i.screen.w;
                float eyeDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float3 camForward = -UNITY_MATRIX_V[2].xyz;
                float sceneT = eyeDepth / max(1e-4, dot(rd, camForward));
                float tEnd = min(t1, sceneT);

                float3 color = 0;
                // Volume: hard shells of glowing air.
                if (tEnd > t0)
                {
                    float sum = 0;
                    float stepLen = (tEnd - t0) / 8;
                    [unroll] for (int k = 0; k < 8; k++)
                    {
                        float3 p = eye + rd * (t0 + stepLen * (k + 0.5));
                        float3 q = p - centre;
                        float3 dir = q / max(length(q), 1e-4);
                        // Streaks radiating from the light (shafts), or hard shells when Rays is 0.
                        float streak = smoothstep(0.82, 0.9, abs(sin(dir.x * 13 + dir.y * 7) * sin(dir.z * 11 - dir.y * 5 + dir.x * 3)));
                        float fall = 1 - length(q) / radius;
                        sum += lerp(Banded(fall), saturate(fall) * (0.08 + streak), _Rays);
                    }
                    float nearFade = smoothstep(_NearFade * 0.3, _NearFade, length(oc));
                    color += _Color.rgb * (sum * stepLen / radius * _Density) * nearFade;
                }
                // Mask: the surface inside the light, lit in hard bands, with a thin rim where the light ends.
                if (sceneT > t0 && sceneT < t1)
                {
                    float3 s = eye + rd * sceneT;
                    float r = length(s - centre) / radius;
                    float lit = Banded(1 - r);
                    float rim = step(0.94, r) * step(r, 0.985);
                    color += _Color.rgb * (lit * _Mask + rim * _Rim * 0.25);
                }
                return half4(color * _Glow, 0);
            }
            ENDHLSL
        }
    }
}
