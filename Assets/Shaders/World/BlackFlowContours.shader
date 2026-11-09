Shader "Capstone/World/BlackFlowContours"
{
    Properties
    {
        [MainColor] _BackgroundColor("Background", Color) = (0.001,0.001,0.001,1)
        _LineColor("Flow Lines", Color) = (0.38,0.4,0.42,1)
        _DotColor("Moving Dots", Color) = (1,1,1,1)
        _PatternScale("Pattern Scale (per metre)", Float) = 0.22
        _WarpStrength("Flow Distortion", Range(0,2)) = 0.8
        _LineDensity("Contour Density", Range(4,40)) = 18
        _LineWidth("Line Half Width", Range(0.002,0.04)) = 0.008
        _DotRadius("Dot Radius", Range(0.005,0.1)) = 0.035
        _DotsPerLoop("Dots Per Loop", Range(1,16)) = 5
        _DotDensity("Populated Contours", Range(0,1)) = 0.65
        _FlowSpeed("Dot Flow Speed", Range(-0.2,0.2)) = 0.018
        _Seed("Random Seed", Float) = 37
        [HideInInspector] _BaseColor("Base Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "BlackFlowContours"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BackgroundColor, _LineColor, _DotColor, _BaseColor;
                float _PatternScale, _WarpStrength, _LineDensity, _LineWidth;
                float _DotRadius, _DotsPerLoop, _DotDensity, _FlowSpeed, _Seed;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fog : TEXCOORD1; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.fog=ComputeFogFactor(o.positionCS.z);
                return o;
            }
            float Hash(float2 p)
            {
                float3 q=frac(float3(p.xyx)*0.1031);
                q+=dot(q,q.yzx+33.33);
                return frac((q.x+q.y)*q.z);
            }
            half4 Frag(Varyings v) : SV_Target
            {
                // Static, continuous world-space mapping: neighbouring meshes share the flow.
                float2 p=(v.positionWS.xz+v.positionWS.y*float2(0.23,0.31))*max(_PatternScale,0.001);
                float2 q=float2(p.x*0.83+p.y*0.27,p.y*1.12-p.x*0.18);
                q.x+=_WarpStrength*(sin(q.y*0.73+1.7)+0.35*sin(q.y*1.61));
                q.y+=_WarpStrength*(sin(q.x*0.61-0.8)+0.28*cos(q.x*1.43));

                // Contours of a cellular stream function form nested vortices and connecting lanes.
                float field=cos(q.x)+cos(q.y);
                float density=max(_LineDensity,1);
                float lane=round(field*density);
                float laneError=field*density-lane;
                float gradient=max(length(sin(q)),0.08);
                float across=laneError/(density*gradient);
                float lineAA=max(fwidth(across),0.0001);
                float lineMask=1-smoothstep(_LineWidth,_LineWidth+lineAA,abs(across));
                // Average unresolved contours instead of letting them flicker in the distance.
                float lineFootprint=fwidth(field*density);
                float average=saturate(2*_LineWidth*density*gradient);
                lineMask=lerp(lineMask,average,smoothstep(0.5,1.5,lineFootprint));

                // On each closed contour the gradient angle winds exactly once.
                // Animating this angle moves dots ALONG the same static contour, including at wrap.
                float angle=atan2(sin(q.y),sin(q.x))/6.2831853;
                float count=max(round(_DotsPerLoop),1);
                float identity=Hash(float2(lane,_Seed));
                float phase=angle*count-_Time.y*_FlowSpeed*count*lerp(0.65,1.3,identity)+identity;
                float phaseError=frac(phase+0.5)-0.5;
                float2 s=sin(q);
                float2 c=cos(q);
                float2 angleGradient=float2(-s.y*c.x,s.x*c.y)/max(dot(s,s),0.0001);
                float2 tangent=float2(-s.y,s.x)/gradient;
                float alongScale=max(abs(dot(angleGradient,tangent))*count/6.2831853,0.02);
                float along=phaseError/alongScale;
                float distanceToDot=length(float2(across,along));
                float dotAA=max(fwidth(distanceToDot),0.0001);
                float dots=1-smoothstep(_DotRadius,_DotRadius+dotAA,distanceToDot);
                dots*=step(identity,_DotDensity);
                dots*=1-smoothstep(0.5,1.5,lineFootprint);
                // Suppress the undefined angle at vortex centres and saddle points.
                dots*=smoothstep(0.04,0.16,length(s));
                half3 color=lerp(_BackgroundColor.rgb,_LineColor.rgb,lineMask);
                color=lerp(color,_DotColor.rgb,dots);
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
