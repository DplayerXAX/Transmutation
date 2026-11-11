Shader "Transmutation/Golden Leaf Garden"
{
    Properties { _Gold ("Gold", Color) = (1,0.65,0.12,1) _Surface ("Ground support", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Leaf { float4 home; float4 position; float4 velocity; float4 rotation; };
            StructuredBuffer<Leaf> _Leaves;
            StructuredBuffer<uint> _Visible;
            float4 _Gold;
            Texture2D<float4> _Surface; SamplerState sampler_Surface;
            float4 _SurfaceRect; float _Mapped;
            float3 GroundAligned(float3 value,float3 normal)
            {
                float3 right=normalize(cross(normal,float3(0,0,1)));
                return right*value.x+normal*value.y+cross(right,normal)*value.z;
            }
            struct Attributes { float3 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; uint instance:SV_InstanceID; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float2 uv:TEXCOORD1; float4 data:TEXCOORD2; };
            float3 Rotate(float3 p,float3 r)
            {
                float sx,cx,sy,cy,sz,cz; sincos(r.x,sx,cx); sincos(r.y,sy,cy); sincos(r.z,sz,cz);
                p = float3(p.x,p.y*cx-p.z*sx,p.y*sx+p.z*cx);
                p = float3(p.x*cy+p.z*sy,p.y,-p.x*sy+p.z*cy);
                return float3(p.x*cz-p.y*sz,p.x*sz+p.y*cz,p.z);
            }
            Varyings Vert(Attributes v)
            {
                Leaf p = _Leaves[_Visible[v.instance]]; Varyings o;
                float3 vertex = v.positionOS;
                vertex.y += sin(vertex.z*12+_Time.y*7+p.home.w*80)*0.035*p.velocity.w*abs(vertex.x)*5;
                float3 normal=float3(0,1,0);
                if(_Mapped>0.5)
                {
                    float4 ground=_Surface.SampleLevel(sampler_Surface,saturate((p.home.xz-_SurfaceRect.xy)/_SurfaceRect.zw),0);
                    normal=normalize(lerp(float3(ground.g,sqrt(saturate(1-ground.g*ground.g-ground.b*ground.b)),ground.b),float3(0,1,0),p.velocity.w));
                }
                float3 world = GroundAligned(Rotate(vertex*p.position.w,p.rotation.xyz),normal)+p.position.xyz;
                o.positionCS = TransformWorldToHClip(world);
                o.normalWS = GroundAligned(Rotate(v.normalOS,p.rotation.xyz),normal); o.uv=v.uv;
                o.data=float4(p.rotation.w,p.home.w,ComputeFogFactor(o.positionCS.z),p.velocity.w);
                return o;
            }
            half4 Frag(Varyings i, bool front:SV_IsFrontFace):SV_Target
            {
                float3 normal = normalize(i.normalWS)*(front?1:-1);
                Light light = GetMainLight();
                float diffuse = abs(dot(normal,light.direction));
                float3 amber = lerp(float3(0.48,0.18,0.025),_Gold.rgb,saturate(i.data.x*1.3));
                amber = lerp(amber,float3(1,0.87,0.36),pow(i.data.y,4)*0.7);
                float vein = 1-smoothstep(0.008,0.025,abs(i.uv.x-0.5));
                float branches = pow(saturate(cos((abs(i.uv.x-0.5)*1.4-i.uv.y)*65)),18)*0.14;
                amber *= 0.82+0.18*i.uv.y+vein*0.12-branches;
                float3 color = amber*(0.48+diffuse*0.65)*lerp(float3(1,1,1),light.color,0.45);
                color += pow(saturate(dot(reflect(-light.direction,normal),float3(0.2,0.7,0.4))),12)*0.12;
                return half4(MixFog(color,i.data.z),1);
            }
            ENDHLSL
        }
    }
}
