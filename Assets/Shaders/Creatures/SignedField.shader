Shader "Capstone/Creature/SignedField"
{
    Properties
    {
        _BaseColor("Gel Color", Color) = (0.08, 0.65, 0.48, 1)
        _BackgroundColor("Background Color", Color) = (0.95, 0.92, 0.85, 1)
        [HDR] _RimColor("Rim Color", Color) = (0.2, 1, 0.75, 1)
        _SphereRadius("Sphere Radius", Range(0.08, 0.5)) = 0.23
        _Movement("Movement Distance", Range(0, 0.5)) = 0.22
        _Speed("Movement Speed", Range(0, 4)) = 1
        _Blend("Gel Blend", Range(0.01, 0.5)) = 0.2
        _ViewSize("View Size (Object Units)", Range(0.1, 5)) = 1.15
        _Center("Object Center Offset", Vector) = (0, 0, 0, 0)
        _Gloss("Gloss", Range(8, 128)) = 64
        _RimStrength("Rim Strength", Range(0, 3)) = 0.7
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "SignedField"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor, _BackgroundColor, _RimColor, _Center;
                float _SphereRadius, _Movement, _Speed, _Blend;
                float _ViewSize, _Gloss, _RimStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPosition : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.screenPosition = ComputeScreenPos(output.positionCS);
                return output;
            }

            float SmoothUnion(float a, float b)
            {
                float k = max(_Blend, 0.0001);
                float h = saturate(0.5 + 0.5 * (b - a) / k);
                return lerp(b, a, h) - k * h * (1.0 - h);
            }
            float Field(float3 p, float3 a, float3 b, float3 c)
            {
                return SmoothUnion(SmoothUnion(length(p-a) - _SphereRadius,
                    length(p-b) - _SphereRadius), length(p-c) - _SphereRadius);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 centerCS = TransformObjectToHClip(_Center.xyz);
                clip(centerCS.w - 0.0001);
                float4 centerScreen = ComputeScreenPos(centerCS);
                float2 uv = input.screenPosition.xy / input.screenPosition.w;
                float2 centerUV = centerScreen.xy / centerScreen.w;
                float3 axisX = TransformObjectToWorldDir(float3(1,0,0), false);
                float3 axisY = TransformObjectToWorldDir(float3(0,1,0), false);
                float3 axisZ = TransformObjectToWorldDir(float3(0,0,1), false);
                float objectScale = max(length(axisX), max(length(axisY), length(axisZ)));
                // Object-centered screen projection: no mesh UVs or surface normals.
                // Clip W handles both perspective distance and orthographic cameras.
                float2 plane = (uv - centerUV) * 2.0 * centerCS.w;
                plane /= float2(UNITY_MATRIX_P._m00, UNITY_MATRIX_P._m11 * _ProjectionParams.x);
                plane /= max(objectScale * _ViewSize, 0.0001);

                float t = _Time.y * _Speed;
                float3 a = _Movement * float3(sin(t), cos(t * 0.83), 0.45 * sin(t * 0.71));
                float3 b = _Movement * float3(sin(t * 0.91 + 2.094), cos(t * 1.07 + 2.094), 0.45 * cos(t * 0.87));
                float3 c = _Movement * float3(sin(t * 1.13 + 4.189), cos(t * 0.79 + 4.189), 0.45 * sin(t * 0.93 + 3.0));
                float bound = _SphereRadius + _Movement * 1.5 + _Blend + 0.05;
                float3 origin = float3(plane, -bound);
                float travel = 0;
                float3 p = origin;
                bool hit = false;
                [loop] for (int step = 0; step < 80; step++)
                {
                    p = origin + float3(0, 0, travel);
                    float d = Field(p, a, b, c);
                    if (d < 0.001) { hit = true; break; }
                    travel += d;
                    if (travel > bound * 2.0) break;
                }
                if (!hit) return half4(_BackgroundColor.rgb, 1);

                float2 e = float2(0.001, 0);
                float3 n = normalize(float3(
                    Field(p + e.xyy,a,b,c) - Field(p - e.xyy,a,b,c),
                    Field(p + e.yxy,a,b,c) - Field(p - e.yxy,a,b,c),
                    Field(p + e.yyx,a,b,c) - Field(p - e.yyx,a,b,c)));
                float3 lightDir = normalize(float3(-0.5, 0.65, -1));
                float3 viewDir = float3(0,0,-1);
                float diffuse = saturate(dot(n, lightDir));
                float specular = pow(saturate(dot(n, normalize(lightDir + viewDir))), _Gloss);
                float rim = pow(1.0 - saturate(dot(n, viewDir)), 3);
                float3 color = _BaseColor.rgb * (0.25 + 0.75 * diffuse);
                color += specular * 0.9 + _RimColor.rgb * rim * _RimStrength;
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
