Shader "Capstone/Creatures/Furnace Glass"
{
    Properties
    {
        _Fill ("Heat", Range(0,1)) = 0
        _VisibleHeight ("Exposed chamber height", Range(0.01,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _Fill, _VisibleHeight;
            struct Varyings { float4 pos : SV_POSITION; float3 local : TEXCOORD0; float3 normal : TEXCOORD1; float3 view : TEXCOORD2; };
            Varyings vert(float3 pos : POSITION, float3 n : NORMAL)
            {
                Varyings o;
                o.pos = TransformObjectToHClip(pos);
                o.local = pos;
                o.normal = TransformObjectToWorldNormal(n);
                o.view = GetWorldSpaceViewDir(TransformObjectToWorld(pos));
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float rim = pow(1-saturate(dot(normalize(i.normal), normalize(i.view))), 2);
                float noise = sin(i.local.x*21 + _Time.y*3)*sin(i.local.z*17 - _Time.y*2);
                float height = (i.local.y + 0.5) / _VisibleHeight;
                clip(1 - height);
                float fire = saturate((_Fill - height + noise*0.035)*17) * saturate(_Fill*40);
                return half4(float3(.035,.045,.056) + rim*float3(.2,.21,.22) + fire*float3(2,.36+.18*_Fill,.035)*(1+noise*.13),1);
            }
            ENDHLSL
        }
    }
}
