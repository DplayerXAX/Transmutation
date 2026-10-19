Shader "Transmutation/Goo Coating"
{
    Properties
    {
        _BaseColor ("Goo Color", Color) = (0.18, 0.85, 0.27, 1)
        _Coverage ("Coverage", Range(0, 1)) = 1
        _Thickness ("Thickness (metres)", Range(0, 0.01)) = 0.002
        [HideInInspector] _PathRange ("Anatomical Range", Vector) = (0, 1, -1, 0.5)
        [HideInInspector] _AxisOS ("Longitudinal Axis", Vector) = (0, 1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+1" }
        Pass
        {
            Name "GooCoating"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Coverage;
                float _Thickness;
                float4 _PathRange;
                float4 _AxisOS;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float armProgress : TEXCOORD2;
            };

            // Object-space longitudinal position becomes continuous shoulder-to-fingertip progress, 0..1.
            float CreateArmProgress(float3 positionOS)
            {
                float localProgress = saturate((dot(positionOS, _AxisOS.xyz) - _PathRange.z) * _PathRange.w);
                return lerp(_PathRange.x, _PathRange.y, localProgress);
            }

            // World-space lighting gives the thin shell a glossy, wet surface without transparency sorting.
            half3 CreateGooLighting(float3 positionWS, float3 normalWS)
            {
                Light mainLight = GetMainLight();
                float3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
                float3 halfDirectionWS = SafeNormalize(mainLight.direction + viewDirectionWS);
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half specular = pow(saturate(dot(normalWS, halfDirectionWS)), 64.0);
                half rim = pow(1.0 - saturate(dot(normalWS, viewDirectionWS)), 3.0);
                return _BaseColor.rgb * (0.3 + diffuse * mainLight.color * 0.7) +
                    specular * mainLight.color * 0.85 + rim * _BaseColor.rgb * 0.35;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz) + output.normalWS * _Thickness;
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.armProgress = CreateArmProgress(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                clip(_Coverage - 0.00001);
                clip(input.armProgress - (1.0 - _Coverage));
                return half4(CreateGooLighting(input.positionWS, normalize(input.normalWS)), 1.0);
            }
            ENDHLSL
        }
    }
}
