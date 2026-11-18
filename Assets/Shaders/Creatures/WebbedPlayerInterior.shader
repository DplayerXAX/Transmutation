Shader "Transmutation/Webbed Player Interior"
{
    Properties
    {
        _BaseColor("Anatomy colour", Color) = (0.12,0.55,0.95,1)
        [HDR] _EmissionColor("Player fluorescence", Color) = (0,0,0,0)
        _Opacity("Anatomy opacity", Range(0,1)) = 1
        [HideInInspector] _SrcBlend("Source blend", Float) = 1
        [HideInInspector] _DstBlend("Destination blend", Float) = 0
        [HideInInspector] _ZWrite("Depth write", Float) = 1
        _Effect("0 bone, 1 flowing vessel, 2 floating flecks", Float) = 0
        _Drift("Local motion amplitude", Vector) = (0.0008,0.0006,0.0012,0)
        _Phase("Flow phase", Float) = 0
        [HideInInspector] _AnimationTime("Preview time", Float) = 0
        [HideInInspector] _UseAnimationTime("Use preview time", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor, _EmissionColor;
            float4 _Drift;
            float _Opacity, _Effect, _AnimationTime, _UseAnimationTime, _Phase;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; float4 motion:TEXCOORD1; };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; float phase:TEXCOORD3; float3 anatomyOS:TEXCOORD4; float boneDetail:TEXCOORD5; };
            float BoneHash(float3 p) { p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z); }
            float BoneNoise(float3 p)
            {
                float3 a=floor(p),f=frac(p);f=f*f*(3-2*f);
                return lerp(lerp(lerp(BoneHash(a),BoneHash(a+float3(1,0,0)),f.x),lerp(BoneHash(a+float3(0,1,0)),BoneHash(a+float3(1,1,0)),f.x),f.y),
                    lerp(lerp(BoneHash(a+float3(0,0,1)),BoneHash(a+float3(1,0,1)),f.x),lerp(BoneHash(a+float3(0,1,1)),BoneHash(a+float3(1,1,1)),f.x),f.y),f.z);
            }
            float Clock() { return lerp(_Time.y,_AnimationTime,_UseAnimationTime); }
            V Vert(A v)
            {
                float time=Clock();
                if (_Effect > 1.5)
                {
                    // All six vertices of a fleck have the same phase: each fleck drifts as a whole.
                    float phase=v.motion.w+_Phase;
                    float3 drift=float3(sin(time*0.29+phase),sin(time*0.21+phase*1.73),sin(time*0.25+phase*2.13));
                    v.positionOS.xyz += drift * _Drift.xyz;
                }
                else if (_Effect > 0.5)
                {
                    // A travelling contraction changes actual diameter; tapered tips stay closed.
                    float phase=v.motion.w+_Phase;
                    float wave=sin(time*1.35-v.uv.y*5+phase);
                    v.positionOS.xyz += v.normalOS * (v.motion.x*0.30*wave);
                    // Slow sideways migration follows each strand's envelope instead of separating its tips.
                    float3 shift=float3(sin(time*0.43+phase),sin(time*0.31+phase*1.7),cos(time*0.37+phase));
                    v.positionOS.xyz += shift * _Drift.xyz * v.motion.y;
                }
                V o; o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS);o.uv=v.uv;o.phase=v.motion.w;o.anatomyOS=v.positionOS.xyz;o.boneDetail=v.motion.z;
                return o;
            }
            half4 Frag(V i):SV_Target
            {
                Light light=GetMainLight(); float3 n=normalize(i.normalWS);
                float boneTone=1;
                if (_Effect < .5 && i.boneDetail > .5)
                {
                    float3 p=i.anatomyOS;
                    float grain=BoneNoise(p*float3(230,22,180));
                    float pores=BoneNoise(p*420);
                    float seamField=BoneNoise(p*31+BoneNoise(p*11)*.75);
                    float seams=(1-smoothstep(.006,.028,abs(seamField-.52)))*smoothstep(.43,.68,BoneNoise(p*13+7));
                    boneTone=.92+.12*grain+.06*pores-.14*seams;
                    float relief=.00028*grain+.00010*pores-.00022*seams;
                    float3 dx=ddx(i.positionWS),dy=ddy(i.positionWS);
                    float3 r1=cross(dy,n),r2=cross(n,dx);float determinant=dot(dx,r1);
                    if(abs(determinant)>1e-10)
                    {
                        float3 gradient=sign(determinant)*(ddx(relief)*r1+ddy(relief)*r2)/abs(determinant);
                        n=normalize(n-clamp(gradient,-.35,.35));
                    }

                }
                half3 colour=_BaseColor.rgb*(0.3+SampleSH(n)+light.color*0.6*saturate(dot(n,light.direction)));
                colour*=boneTone;
                float flow=1;
                if (_Effect > 0.5 && _Effect < 1.5)
                {
                    // Bright packets travel along each interrupted pink vessel rather than flashing in unison.
                    float wave=0.5+0.5*sin(i.uv.y*10+i.uv.x*0.7-Clock()*1.7+_Phase+i.phase);
                    flow=0.65+0.55*wave*wave;
                    colour=colour*flow+_BaseColor.rgb*wave*0.16;
                }
                return half4(colour+_EmissionColor.rgb*flow,_Opacity);
            }
            ENDHLSL
        }
    }
}
