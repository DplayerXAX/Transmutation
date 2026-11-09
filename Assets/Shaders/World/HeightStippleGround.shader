Shader "Capstone/World/HeightStippleGround"
{
    Properties
    {
        [MainColor] _GroundColor("Grey Ground", Color) = (0.38,0.38,0.38,1)
        _HighColor("High Ground Dots", Color) = (1,1,1,1)
        _LowColor("Low Ground Dots", Color) = (0.008,0.008,0.008,1)
        _HeightLow("Low Height (world Y)", Float) = -45
        _HeightHigh("High Height (world Y)", Float) = 5
        _Midpoint("Neutral Height In Range", Range(0.05,0.95)) = 0.5
        _DotSpacing("Dot Spacing (metres)", Range(0.03,1)) = 0.18
        _DotRadius("Dot Radius (metres)", Range(0.002,0.1)) = 0.025
        _MaxDensity("Maximum Dot Density", Range(0,1)) = 0.95
        _DensityPower("Density Falloff", Range(0.2,4)) = 0.8
        _Seed("Random Seed", Float) = 71
        _ShadowStrength("Received Shadow Strength", Range(0,1)) = 0.3
        [HideInInspector] _BaseColor("Base Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "HeightStipple"
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
                float _HeightLow, _HeightHigh, _Midpoint, _DotSpacing, _DotRadius;
                float _MaxDensity, _DensityPower, _Seed, _ShadowStrength;
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
            float Hash(float2 p)
            {
                float3 q=frac(float3(p.xyx)*0.1031);
                q+=dot(q,q.yzx+33.33);
                return frac((q.x+q.y)*q.z);
            }
            float Dots(float2 world, float density, float seed)
            {
                float spacing=max(_DotSpacing,0.01);
                float2 p=world/spacing;
                float2 cell=floor(p);
                float coverage=0;
                float radius=min(_DotRadius/spacing,0.22);
                float footprint=max(length(ddx(p)),length(ddy(p)));
                [unroll] for(int y=-1;y<=1;y++)
                [unroll] for(int x=-1;x<=1;x++)
                {
                    float2 id=cell+float2(x,y);
                    float2 random=float2(Hash(id+seed),Hash(id+seed+27.9));
                    float2 center=id+0.5+(random-0.5)*0.8;
                    float distanceToDot=length(p-center);
                    float dotRadius=radius*lerp(0.75,1.15,Hash(id+seed+59.3));
                    float aa=clamp(fwidth(distanceToDot),0.001,0.3);
                    float dotMask=1-smoothstep(max(0,dotRadius-aa*0.5),dotRadius+aa*0.5,distanceToDot);
                    float present=step(Hash(id+seed+113.1),density)*step(0.00001,density);
                    coverage=max(coverage,dotMask*present);
                }
                // Resolve tiny distant dots to their average coverage, avoiding shimmer.
                float average=density*3.14159*radius*radius*0.92;
                return lerp(coverage,average,smoothstep(0.6,1.5,footprint));
            }
            half4 Frag(Varyings v) : SV_Target
            {
                float height=saturate((v.positionWS.y-_HeightLow)/max(_HeightHigh-_HeightLow,0.001));
                float high=saturate((height-_Midpoint)/max(1-_Midpoint,0.001));
                float low=saturate((_Midpoint-height)/max(_Midpoint,0.001));
                float density=pow(max(high,low),_DensityPower)*_MaxDensity;
                float3 weights=pow(abs(normalize(v.normalWS)),4);
                weights/=max(dot(weights,1),0.001);
                float dots=Dots(v.positionWS.yz,density,_Seed+19)*weights.x
                    +Dots(v.positionWS.xz,density,_Seed)*weights.y
                    +Dots(v.positionWS.xy,density,_Seed+53)*weights.z;
                half3 ink=lerp(_LowColor.rgb,_HighColor.rgb,step(_Midpoint,height));
                half3 color=lerp(_GroundColor.rgb,ink,dots);
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
