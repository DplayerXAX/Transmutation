Shader "Capstone/Sketch Contours"
{
    Properties
    {
        _LineColor ("Line Color", Color) = (0.2, 0.2, 0.15, 1)
        _Opacity ("Opacity", Range(0,1)) = 1
        _Thickness ("Edge Sample Radius (pixels)", Range(0.5,4)) = 1
        _NormalSensitivity ("Normal Sensitivity", Range(0,3)) = 0.3
        _DepthSensitivity ("Depth Sensitivity", Range(0,5)) = 1.5
        _DepthRange ("Depth Comparison Range (world units)", Float) = 20
        _ErrorRange ("Stroke Distortion (UV)", Range(0,0.01)) = 0.003
        _ErrorPeriod ("Stroke Frequency", Range(1,100)) = 30
        _NoiseAmount ("Noise Height (UV)", Range(0,0.02)) = 0.01
        _NoiseScale ("Noise Scale", Range(1,500)) = 128
        _StrokeSpeed ("Stroke Animation Speed (0 freezes)", Range(0,2)) = 0.2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float4 _LineColor;
        float _Opacity, _Thickness, _NormalSensitivity, _DepthSensitivity, _DepthRange;
        float _ErrorRange, _ErrorPeriod, _NoiseAmount, _NoiseScale, _StrokeSpeed;
        CBUFFER_END

        float IsSky(float depth)
        {
            #if UNITY_REVERSED_Z
                return depth <= 0.000001;
            #else
                return depth >= 0.999999;
            #endif
        }

        float EyeDepth(float depth)
        {
            if (unity_OrthoParams.w > 0.5)
            {
                #if UNITY_REVERSED_Z
                    depth = 1.0 - depth;
                #endif
                return lerp(_ProjectionParams.y, _ProjectionParams.z, depth);
            }
            return LinearEyeDepth(depth, _ZBufferParams);
        }

        float CheckSame(float2 a, float2 b)
        {
            float da = SampleSceneDepth(saturate(a));
            float db = SampleSceneDepth(saturate(b));
            float skyA = IsSky(da), skyB = IsSky(db);
            if (skyA != skyB) return 0;
            if (skyA > 0.5) return 1;
            // Match the source's packed camera-space normal XY comparison.
            float2 na = TransformWorldToViewDir(SampleSceneNormals(saturate(a)), false).xy * 0.5;
            float2 nb = TransformWorldToViewDir(SampleSceneNormals(saturate(b)), false).xy * 0.5;
            float resolutionScale = _ScaledScreenParams.y / 400.0;
            float2 dn = abs(na - nb) * _NormalSensitivity * resolutionScale;
            float dd = abs(EyeDepth(da) - EyeDepth(db)) / max(_DepthRange, 0.001);
            dd *= _DepthSensitivity * resolutionScale;
            return (dn.x + dn.y < 0.1 && dd < 0.1) ? 1.0 : 0.0;
        }

        half4 DetectEdges(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            float2 pixel = _Thickness / _ScaledScreenParams.xy;
            float edge = CheckSame(uv + pixel, uv - pixel)
                       * CheckSame(uv + pixel * float2(-1,1), uv + pixel * float2(1,-1));
            return half4(edge, edge, edge, 1);
        }

        float Hash(float2 p)
        {
            float3 q = frac(float3(p.xyx) * 0.1031);
            q += dot(q, q.yzx + 33.33);
            return frac((q.x + q.y) * q.z);
        }
        float Noise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(Hash(i), Hash(i + float2(1,0)), f.x),
                        lerp(Hash(i + float2(0,1)), Hash(i + 1), f.x), f.y);
        }
        float EdgeAt(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv)).r;
        }
        half4 Composite(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float time = _Time.y * _StrokeSpeed;
            float2 uv = input.texcoord;
            float noise = (Noise(uv * _NoiseScale) - 0.5) * _NoiseAmount;
            // Original phase pairs with independent, smooth drift for each stroke.
            float2 uv0 = uv + _ErrorRange * sin(_ErrorPeriod * uv.yx + float2(0,0) + time * float2(1.0,0.73)) + noise;
            float2 uv1 = uv + _ErrorRange * sin(_ErrorPeriod * uv.yx + float2(1.047,3.142) + time * float2(-0.81,1.13)) + noise;
            float2 uv2 = uv + _ErrorRange * sin(_ErrorPeriod * uv.yx + float2(2.094,1.571) + time * float2(0.61,-0.93)) + noise;
            float edge = EdgeAt(uv0) * EdgeAt(uv1) * EdgeAt(uv2);
            return half4(_LineColor.rgb, saturate((1.0 - edge) * _Opacity * _LineColor.a));
        }
        ENDHLSL
        Pass
        {
            Name "Detect Edges"
            Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DetectEdges
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass
        {
            Name "Three Distorted Strokes"
            Blend SrcAlpha OneMinusSrcAlpha, Zero One
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Composite
            ENDHLSL
        }
    }
    Fallback Off
}
