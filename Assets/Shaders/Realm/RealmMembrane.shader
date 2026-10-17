Shader "Capstone/Realm/RealmMembrane"
{
    // Thin living film: web membranes between strands, jelly arches, the shelter roof.
    // See-through, with a thin-film rainbow that shifts with the view and slowly drifts,
    // brighter and more opaque at grazing angles, and faint veins running through it.
    // Mesh data: uv0.y = random per piece, uv1 = (across, metres along) or any surface coordinates.
    Properties
    {
        _Tint("Tint", Color) = (0.75, 0.85, 1, 1)
        _FilmA("Film A", Color) = (0.55, 0.9, 1, 1)
        _FilmB("Film B", Color) = (1, 0.6, 0.85, 1)
        _FilmC("Film C", Color) = (0.85, 1, 0.7, 1)
        _Opacity("Opacity Facing", Range(0, 1)) = 0.22
        _EdgeOpacity("Opacity At Edges", Range(0, 1)) = 0.75
        _Iridescence("Rainbow Strength", Range(0, 1)) = 0.8
        _Veins("Vein Lines", Range(0, 1)) = 0.35
        _VeinScale("Veins Per Metre", Float) = 3
        _Glow("Self Glow", Range(0, 2)) = 0.35
        _Sway("Sway (metres)", Float) = 0
        _SwaySpeed("Sway Speed", Float) = 0.5
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Shaders/World/LineWorldLighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half4 _FilmA;
                half4 _FilmB;
                half4 _FilmC;
                half _Opacity;
                half _EdgeOpacity;
                half _Iridescence;
                half _Veins;
                float _VeinScale;
                half _Glow;
                float _Sway;
                float _SwaySpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float2 uv1 : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                // Films breathe in and out a little.
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                positionWS += normalWS * sin(_Time.y * _SwaySpeed + input.uv.y * 6.28 + dot(positionWS, float3(0.3, 0.2, 0.25))) * _Sway;
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = normalWS;
                output.uv = input.uv;
                output.uv1 = input.uv1;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half3 Rainbow(float t)
            {
                t = frac(t);
                half3 a = lerp(_FilmA.rgb, _FilmB.rgb, smoothstep(0.0, 0.33, t));
                half3 b = lerp(a, _FilmC.rgb, smoothstep(0.33, 0.66, t));
                return lerp(b, _FilmA.rgb, smoothstep(0.66, 1.0, t));
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS) * IS_FRONT_VFACE(isFront, 1.0, -1.0);
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                half facing = saturate(dot(normalWS, viewDirWS));
                half edge = pow(1.0h - facing, 2.0h);

                // Thin-film colour: depends on the view angle, the place on the film, and drifts over time.
                float film = facing * 1.7 + dot(input.positionWS, float3(0.07, 0.11, 0.05)) + input.uv.y + _Time.y * 0.05;
                half3 color = lerp(_Tint.rgb, Rainbow(film), _Iridescence);

                // Faint veins through the film.
                float veinCoord = dot(input.positionWS, float3(0.8, 0.35, -0.5)) * _VeinScale + sin(dot(input.positionWS, float3(1.7, 2.3, 1.1))) * 0.6;
                half vein = LineWorldStripe(veinCoord, 1.2h) * _Veins * LineWorldDetail(input.positionWS);
                color = lerp(color, half3(1, 1, 1), vein * 0.6h);

                Light mainLight = GetMainLight();
                half glint = pow(saturate(dot(reflect(-viewDirWS, normalWS), mainLight.direction)), 30.0h);
                color = color * (0.6h + _Glow) + glint;
                half alpha = saturate(lerp(_Opacity, _EdgeOpacity, edge) + vein * 0.3h + glint * 0.5h);
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
