Shader "Capstone/Creature/FireEater"
{
    Properties
    {
        [Header(Cells)]
        _CellDensity ("Cell Density (per UV axis)", Range(0,100)) = 8
        _DotSize ("Dot Radius", Range(0.01,0.48)) = 0.16
        _DotAmount ("Dot Amount (zero = none)", Range(0,1)) = 1
        _PlateBlend ("Dots to Connected Plates", Range(0,1)) = 0
        _SeamWidth ("Plate Seam Width", Range(0.001,0.25)) = 0.045
        _EdgeSoftness ("Edge Softness", Range(0,0.15)) = 0.025
        _Warp ("Pattern Warp", Range(0,1)) = 0.18
        _Seed ("Pattern Seed", Float) = 0
        [Header(Motion)]
        _Speed ("Animation Speed (zero = static)", Range(0,3)) = 0.28
        _Motion ("Animation Amount (zero = static)", Range(0,1)) = 1
        [Header(Grayscale)]
        _GroundGray ("Surface Gray", Range(0,1)) = 0.85
        _InkGray ("Dots and Seams Gray", Range(0,1)) = 0.015
        _Variation ("Cell Gray Variation", Range(0,0.5)) = 0.12
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float _CellDensity, _DotSize, _DotAmount, _PlateBlend, _SeamWidth;
        float _EdgeSoftness, _Warp, _Seed, _Speed, _Motion;
        float _GroundGray, _InkGray, _Variation;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings Vert(Attributes i)
        {
            Varyings o;
            UNITY_SETUP_INSTANCE_ID(i);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionCS=TransformObjectToHClip(i.positionOS.xyz);
            o.normalWS=TransformObjectToWorldNormal(i.normalOS);
            o.uv=i.uv;
            return o;
        }
        float2 Hash2(float2 p)
        {
            float3 q=frac(float3(p.x,p.y,p.x)*float3(0.1031,0.1030,0.0973));
            q+=dot(q,q.yzx+33.33);
            return frac((q.xx+q.yz)*q.zy);
        }
        half4 Frag(Varyings i):SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            if (_CellDensity<=0 || _DotAmount<=0) return half4(_GroundGray.xxx,1);
            float t=_Time.y*_Speed;
            float2 p=i.uv*_CellDensity+_Seed;
            p+=_Warp*float2(sin(p.y*1.7+_Motion*sin(t*0.31)),
                           sin(p.x*1.3+_Motion*sin(t*0.27+2)));
            float2 cell=floor(p), local=frac(p);
            float first=100,second=100, closestDot=100;
            float2 nearestID=0;
            // A 5x5 neighborhood keeps the second-nearest site reliable at cell corners.
            [unroll] for(int y=-2;y<=2;y++)
            [unroll] for(int x=-2;x<=2;x++)
            {
                float2 g=float2(x,y),id=cell+g;
                float2 random=Hash2(id);
                float2 rest=0.5+0.28*sin(6.2831853*random);
                float2 moving=0.5+0.28*sin(6.2831853*random+t);
                float2 sitePosition=lerp(rest,moving,_Motion);
                float distanceToSite=length(g+sitePosition-local);
                if (distanceToSite<first)
                {
                    second=first;first=distanceToSite;nearestID=id;
                }
                else second=min(second,distanceToSite);
                // Deterministic site selection: amount changes density without flickering.
                if (Hash2(id+73.17).x<_DotAmount)
                    closestDot=min(closestDot,distanceToSite);
            }
            float aa=max(fwidth(first),0.0001);
            float softness=max(_EdgeSoftness,aa);
            float dots=1-smoothstep(_DotSize-softness,_DotSize+softness,closestDot);
            float seamDistance=second-first;
            float seamAA=max(_EdgeSoftness,max(fwidth(seamDistance),0.0001));
            float seams=1-smoothstep(_SeamWidth-seamAA,_SeamWidth+seamAA,seamDistance);
            float selected=Hash2(nearestID+73.17).x<_DotAmount ? 1 : 0;
            float ink=lerp(dots,seams*selected,_PlateBlend);
            float surface=saturate(_GroundGray-_Variation*Hash2(nearestID+19).x);
            // Fade cell-to-cell shading in near the sites to avoid sharp background grid edges.
            float variationMask=1-smoothstep(0.2,0.6,first);
            surface=lerp(_GroundGray,surface,variationMask);
            float gray=lerp(surface,_InkGray,ink);
            return half4(gray.xxx,1);
        }
        half4 Depth(Varyings i):SV_Target { return 0; }
        half4 Normals(Varyings i):SV_Target
        {
            float3 n=normalize(i.normalWS);
            #ifdef _GBUFFER_NORMALS_OCT
                return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(n)*0.5+0.5)),0);
            #else
                return half4(n,0);
            #endif
        }
        ENDHLSL
        Pass
        {
            Name "Cellular"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Depth
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Normals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    Fallback Off
}
