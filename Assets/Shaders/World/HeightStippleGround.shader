Shader "Capstone/World/HeightStippleGround"
{
    Properties
    {
        [MainColor] _GroundColor("Middle Elevation Grey", Color) = (0.38,0.38,0.38,1)
        _HighColor("High Elevation Color", Color) = (1,1,1,1)
        _LowColor("Low Elevation Color", Color) = (0.008,0.008,0.008,1)
        _HeightLow("Low Height (world Y)", Float) = -45
        _HeightHigh("High Height (world Y)", Float) = 5
        _Midpoint("Neutral Height In Range", Range(0.05,0.95)) = 0.5
        _BandCount("Elevation Bands", Range(2,32)) = 10
        _ContourWidth("Contour Width (band fraction)", Range(0.005,0.15)) = 0.025
        _ContourStrength("Contour Strength", Range(0,1)) = 0.45
        _ShadowStrength("Received Shadow Strength", Range(0,1)) = 0.3
        _FacetStrength("Directional Facet Shading", Range(0,1)) = 0
        [HideInInspector] _BaseColor("Base Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "HeightBands"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _GroundColor, _HighColor, _LowColor, _BaseColor;
                float _HeightLow, _HeightHigh, _Midpoint, _BandCount;
                float _ContourWidth, _ContourStrength, _ShadowStrength;
                float _FacetStrength;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fog : TEXCOORD2;
            };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                o.fog=ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 Frag(Varyings v) : SV_Target
            {
                float height=saturate((v.positionWS.y-_HeightLow)/max(_HeightHigh-_HeightLow,0.001));
                // Shared world-height thresholds keep bands aligned across all chunks.
                float bands=max(round(_BandCount),2);
                float elevation=height*bands;
                float band=min(floor(elevation),bands-1);
                float level=band/(bands-1);
                half3 color=level<=_Midpoint
                    ? lerp(_LowColor.rgb,_GroundColor.rgb,level/max(_Midpoint,0.001))
                    : lerp(_GroundColor.rgb,_HighColor.rgb,(level-_Midpoint)/max(1-_Midpoint,0.001));

                // Draw only internal contours; fade subpixel lines to avoid distant shimmer.
                float boundary=clamp(floor(elevation+0.5),1,bands-1);
                float distanceToContour=abs(elevation-boundary);
                float footprint=max(fwidth(elevation),0.0001);
                float contour=1-smoothstep(_ContourWidth,_ContourWidth+footprint,distanceToContour);
                contour*=1-smoothstep(0.25,1,footprint);
                color=lerp(color,_LowColor.rgb,contour*_ContourStrength);
                Light sun=GetMainLight(TransformWorldToShadowCoord(v.positionWS));
                color*=lerp(1-_ShadowStrength,1,sun.shadowAttenuation);
                color*=lerp(1,0.42+0.58*saturate(dot(normalize(v.normalWS),sun.direction)),_FacetStrength);
                return half4(MixFog(color,v.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack Off
}
