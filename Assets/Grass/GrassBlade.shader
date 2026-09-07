Shader "Tutorial604/BezierBlade"
{
    Properties
    {
        [Header(Shape)]
        _TaperAmount ("Taper Amount", Float) = 0
        _BladeWorldXAngle ("Blade World X Angle", Range(-90, 90)) = 0
        [Toggle] _FaceCamera ("Face Camera", Float) = 1
        _CurvedNormalAmount("Curved Normal Amount", Range(0, 5)) = 1
        _p1Offset ("p1Offset", Float) = 1
        _p2Offset ("p2Offset", Float) = 1

        [Header(Shading)]
        _TopColor ("Top Color", Color) = (0.45, 0.72, 0.28, 1)
        _BottomColor ("Bottom Color", Color) = (0.28, 0.48, 0.16, 1)
        _GrassAlbedo("Grass albedo", 2D) = "white" {}
        _Brightness("Brightness", Range(0, 3)) = 1.3

        [Header(Wind Animation)]
        _WaveAmplitude("Wave Amplitude", Float) = 1
        _WaveSpeed("Wave Speed", Float) = 1
        _SinOffsetRange("Phase Variation", Range(0, 10)) = 0.3
        _PushTipForward("Push Tip Forward", Range(0, 2)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Unlit Grass Blade"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct GrassBlade
            {
                float3 position;
                float rotAngle;
                float hash;
                float height;
                float width;
                float tilt;
                float bend;
                float3 surfaceNorm;
                float windForce;
                float sideBend;
            };

            StructuredBuffer<GrassBlade> _GrassBlades;
            StructuredBuffer<int> Triangles;
            StructuredBuffer<float4> Colors;
            StructuredBuffer<float2> Uvs;

            TEXTURE2D(_GrassAlbedo);
            SAMPLER(sampler_GrassAlbedo);

            CBUFFER_START(UnityPerMaterial)
                float _TaperAmount;
                float _BladeWorldXAngle;
                float _FaceCamera;
                float _CurvedNormalAmount;
                float _p1Offset;
                float _p2Offset;
                half4 _TopColor;
                half4 _BottomColor;
                half _Brightness;
                float _WaveAmplitude;
                float _WaveSpeed;
                float _SinOffsetRange;
                float _PushTipForward;
            CBUFFER_END

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float t : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            float3x3 RotAxis3x3(float angle, float3 axis)
            {
                axis = normalize(axis);

                float s, c;
                sincos(angle, s, c);

                float t = 1.0 - c;

                float x = axis.x;
                float y = axis.y;
                float z = axis.z;

                float xy = x * y;
                float xz = x * z;
                float yz = y * z;
                float xs = x * s;
                float ys = y * s;
                float zs = z * s;

                return float3x3(
                    t * x * x + c, t * xy - zs, t * xz + ys,
                    t * xy + zs, t * y * y + c, t * yz - xs,
                    t * xz - ys, t * yz + xs, t * z * z + c
                );
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                GrassBlade blade = _GrassBlades[IN.instanceID];
                float height = blade.height;
                float hash = blade.hash;
                float windForce = blade.windForce;

                int positionIndex = Triangles[IN.vertexID];
                float4 vertColor = Colors[positionIndex];
                float2 uv = Uvs[positionIndex];

                float t = vertColor.r;
                float3 centerPos = float3(0, t * height, 0);

                float width = blade.width * (1 - _TaperAmount * t);
                float side = vertColor.g * 2 - 1;
                float3 position = centerPos + float3(0, 0, side * width);

                float windOsc = sin((_Time.y + hash * 2.0 * PI) * _WaveSpeed + _SinOffsetRange * 2.0 * PI);
                float leanAngle = (windOsc + _PushTipForward) * windForce * _WaveAmplitude;
                float3x3 windRot = RotAxis3x3(leanAngle, float3(0, 0, 1));
                position = mul(windRot, position);

                // Pitch first so the stem (local Y) is established, then spin only around that axis
                float3x3 worldXRot = RotAxis3x3(radians(_BladeWorldXAngle), float3(1, 0, 0));
                position = mul(worldXRot, position);

                float3 localY = normalize(mul(worldXRot, mul(windRot, float3(0, 1, 0))));
                float yawAngle = 0.0;

                if (_FaceCamera > 0.5)
                {
                    float3 face = mul(worldXRot, mul(windRot, float3(1, 0, 0)));
                    float3 toCam = GetCameraPositionWS() - blade.position;

                    float3 faceFlat = face - localY * dot(face, localY);
                    float3 toCamFlat = toCam - localY * dot(toCam, localY);
                    float faceLen = length(faceFlat);
                    float camLen = length(toCamFlat);

                    if (faceLen > 1e-5 && camLen > 1e-5)
                    {
                        faceFlat /= faceLen;
                        toCamFlat /= camLen;
                        yawAngle = atan2(dot(localY, cross(faceFlat, toCamFlat)), dot(faceFlat, toCamFlat));
                    }
                }
                else
                {
                    yawAngle = -blade.rotAngle;
                }

                position = mul(RotAxis3x3(yawAngle, localY), position);

                position += blade.position;

                OUT.positionCS = TransformWorldToHClip(position);
                OUT.uv = uv;
                OUT.t = t;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);

                return OUT;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 grassAlbedoSample = SAMPLE_TEXTURE2D(_GrassAlbedo, sampler_GrassAlbedo, i.uv);
                half3 grassAlbedo = saturate(grassAlbedoSample.rgb);

                half4 grassCol = lerp(_BottomColor, _TopColor, i.t);
                half3 color = grassCol.rgb * grassAlbedo * _Brightness;
                color = MixFog(color, i.fogFactor);

                return half4(color, grassCol.a);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/InternalErrorShader"
}
