Shader "Capstone/World/WindBrushGround"
{
    Properties
    {
        [MainColor] _BackgroundColor("Ground Color", Color) = (0.004,0.005,0.004,1)
        _BrushColor("Fiber Color", Color) = (0.78,0.75,0.65,1)
        _PatternScale("Pattern Scale (per metre)", Range(0.05,3)) = 0.5
        _Angle("Brush Direction (degrees)", Range(-180,180)) = -38
        _Spacing("Brush Spacing", Range(2,8)) = 3.6
        _Length("Brush Length", Range(0.5,4)) = 3
        _Width("Brush Width", Range(0.1,1.5)) = 0.85
        _Curve("S Curve", Range(0,1)) = 0.4
        _FiberCount("Fibers Per Brush", Range(6,50)) = 18
        _Variation("Brush Shape Variation", Range(0,1)) = 0.85
        _Scatter("Position Scatter", Range(0,1)) = 0.95
        _RotationVariation("Rotation Variation", Range(0,180)) = 155
        _Separation("Brush Separation", Range(0.02,0.5)) = 0.12
        _Grain("Dry Brush Grain", Range(0,1)) = 0.55
        _WindDirection("Wind Direction (degrees)", Range(-180,180)) = 25
        _WindSpeed("Wind Speed", Range(0,4)) = 1
        _WindStrength("Sway Strength", Range(0,1)) = 0.8
        _WaveScale("Wind Wave Scale", Range(0.05,2)) = 0.45
        _GustStrength("Gust Strength", Range(0,1)) = 0.6
        _Seed("Random Seed", Float) = 31
        _ShadowStrength("Received Shadow Strength", Range(0,1)) = 0.5
        [HideInInspector] _BaseColor("Base Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "WindBrush"
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
                half4 _BackgroundColor, _BrushColor, _BaseColor;
                float _PatternScale, _Angle, _Spacing, _Length, _Width, _Curve;
                float _FiberCount, _Grain, _WindDirection, _WindSpeed, _WindStrength;
                float _WaveScale, _GustStrength, _Seed, _ShadowStrength;
                float _Variation, _Scatter, _RotationVariation, _Separation;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fog : TEXCOORD1; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            float Hash(float2 p)
            {
                float3 q = frac(float3(p.xyx)*0.1031);
                q += dot(q,q.yzx+33.33);
                return frac((q.x+q.y)*q.z);
            }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p);
                f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),
                    lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            float2 BrushCenter(float2 id, float spacing)
            {
                float2 random=float2(Hash(id+_Seed+12.3),Hash(id+_Seed+87.6));
                return (id+0.5+(random-0.5)*0.9*_Scatter)*spacing;
            }
            half4 Frag(Varyings v) : SV_Target
            {
                float s,c;
                sincos(radians(_Angle),s,c);
                float2 world=v.positionWS.xz*max(_PatternScale,0.001);
                float2 p=mul(float2x2(c,-s,s,c),world);
                float spacing=max(_Spacing,2);
                float2 cellSize=spacing.xx;
                float2 cell=floor(p/cellSize);
                float ws,wc;
                sincos(radians(_WindDirection),ws,wc);
                float2 wind=float2(wc,ws);
                float time=_Time.y*_WindSpeed;
                float ink=0;
                // Neighbour cells keep bent tips continuous across cell boundaries.
                [unroll] for(int y=-1;y<=1;y++)
                [unroll] for(int x=-1;x<=1;x++)
                {
                    float2 id=cell+float2(x,y);
                    float r=Hash(id+_Seed);
                    float r2=Hash(id+_Seed+43.2);
                    float r3=Hash(id+_Seed+113.7);
                    float r4=Hash(id+_Seed+209.1);
                    float2 center=BrushCenter(id,spacing);
                    // Disjoint circular motion envelopes around scattered seeds.
                    // A two-cell neighbour is at least 1.1*spacing away, so the
                    // 0.54*spacing cap makes the immediate eight sufficient.
                    float nearest=spacing*1.08;
                    [unroll] for(int ny=-1;ny<=1;ny++)
                    [unroll] for(int nx=-1;nx<=1;nx++)
                    {
                        if(nx!=0 || ny!=0)
                            nearest=min(nearest,distance(center,BrushCenter(id+float2(nx,ny),spacing)));
                    }
                    float safeRadius=max(0.005,nearest*0.5-min(_Separation,nearest*0.2));
                    float2 local=p-center;
                    float tilt=radians((r3-0.5)*2*_RotationVariation);
                    float st,ct;
                    sincos(tilt,st,ct);
                    local=mul(float2x2(ct,-st,st,ct),local);
                    float length=_Length*lerp(1,lerp(0.45,1.4,r),_Variation);
                    float brushWidth=_Width*lerp(1,lerp(0.4,1.65,r2),_Variation);
                    float curve=_Curve*lerp(1,lerp(0.25,1.8,r3),_Variation);
                    float windAmplitude=_WindStrength*(1+_GustStrength)*1.2;
                    float extentX=curve*1.5+brushWidth*0.57+windAmplitude;
                    float extentY=length*0.5+0.1;
                    float fit=min(1,safeRadius/sqrt(extentX*extentX+extentY*extentY));
                    local/=max(fit,0.001);
                    float u=local.y/length+0.5;
                    float along=saturate(u);
                    // Shared travelling wind fronts plus a small per-brush delay.
                    // The root stays planted while the tip follows the gust.
                    float phase=dot(center,wind)*_WaveScale-time*1.9;
                    float gust=0.5+0.5*sin(phase*0.63-time*0.2);
                    float sway=sin(phase-along*lerp(1.2,3,r4)+r*1.7);
                    float curvePhase=lerp(-1.4,1.4,r4)*_Variation;
                    float curveFrequency=lerp(3,7,r3);
                    float bend=curve*(sin((along-0.5)*curveFrequency+curvePhase)
                        +0.5*_Variation*sin(along*3.14159+r2*6.28318));
                    bend+=_WindStrength*(0.12+0.88*along*along)*
                        (sway+0.2*sin(phase*1.71-along*4+r3*6))*(1+gust*_GustStrength);
                    float across=local.x-bend;
                    float width=brushWidth*(0.82+0.18*sin(along*3.14159));
                    float fiberCount=max(4,floor(_FiberCount*lerp(1,lerp(0.4,1.7,r4),_Variation)));
                    float flutter=sin(along*9-phase*2.1+r2*12)*0.15*along*_WindStrength;
                    float fiberCoord=(across/width+0.5)*fiberCount+flutter;
                    float fiberID=floor(fiberCoord);
                    float jitter=Noise(float2(fiberID+r*79,along*19))*0.24;
                    float fiberDistance=abs(frac(fiberCoord+jitter)-0.5);
                    float aa=max(fwidth(fiberCoord),0.015);
                    float fibers=1-smoothstep(0.19-aa,0.36+aa,fiberDistance);
                    fibers=lerp(fibers,0.55,smoothstep(0.6,1.8,aa));
                    float grain=Noise(float2(across*125+r*31,local.y*85-time*2.5));
                    float bristle=Hash(float2(fiberID,r*97));
                    float tipRagged=(bristle-0.5)*0.075+(grain-0.5)*0.025;
                    float edgeAA=clamp(fwidth(local.y),0.003,0.03);
                    float ends=1-smoothstep(length*0.5-0.025-edgeAA,length*0.5+edgeAA,abs(local.y)+tipRagged);
                    float edge=1-smoothstep(width*0.43,width*0.52,abs(across)+(grain-0.5)*0.035);
                    float dry=lerp(1,smoothstep(0.18,0.8,grain),_Grain);
                    float lighting=lerp(0.42,1,0.5+0.5*cos(along*3.5+phase*0.6));
                    float stroke=1.6*fibers*ends*edge*dry*lighting*lerp(0.7,1,bristle);
                    // Bound the complete animated mark, including antialiasing.
                    float envelope=1-smoothstep(safeRadius*0.98,safeRadius,distance(p,center));
                    ink=max(ink,stroke*envelope);
                }
                half3 color=lerp(_BackgroundColor.rgb,_BrushColor.rgb,saturate(ink));
                Light sun=GetMainLight(TransformWorldToShadowCoord(v.positionWS));
                color*=lerp(1-_ShadowStrength,1,sun.shadowAttenuation);
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
