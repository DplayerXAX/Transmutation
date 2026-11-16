Shader "Capstone/Fluid/Lava"
{
    // Molten rock for LavaFluid blobs. _Heat (set per blob) runs 1 = just spat out, all glowing and flowing,
    // to 0 = cooled: a dark crust with only a few dull veins. Pattern is in world space so neighbouring blobs
    // read as one flow.
    Properties
    {
        _Heat("Heat (0 cooled .. 1 molten)", Range(0, 1)) = 1
        _CrustColor("Crust", Color) = (0.06, 0.035, 0.03, 1)
        _EmberColor("Ember (cooling veins)", Color) = (0.55, 0.07, 0.02, 1)
        _GlowColor("Glow", Color) = (1, 0.36, 0.04, 1)
        _CoreColor("Core (hottest)", Color) = (1, 0.86, 0.45, 1)
        _Scale("Pattern Scale", Range(0.2, 8)) = 2.2
        _FlowSpeed("Flow Speed", Range(0, 2)) = 0.35
        _VeinWidth("Vein Width", Range(0.01, 0.5)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Lava"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Heat;
                half4 _CrustColor;
                half4 _EmberColor;
                half4 _GlowColor;
                half4 _CoreColor;
                float _Scale;
                float _FlowSpeed;
                float _VeinWidth;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionHCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            float Fbm(float3 p)
            {
                float sum = 0.0, amplitude = 0.55;
                for (int octave = 0; octave < 4; octave++) { sum += Noise(p) * amplitude; p = p * 2.03 + 11.7; amplitude *= 0.5; }
                return sum;
            }

            // Crust -> ember -> glow -> core.
            half3 Ramp(float t)
            {
                t = saturate(t) * 3.0;
                half3 low = lerp(_CrustColor.rgb, _EmberColor.rgb, saturate(t));
                half3 mid = lerp(low, _GlowColor.rgb, saturate(t - 1.0));
                return lerp(mid, _CoreColor.rgb, saturate(t - 2.0));
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float heat = saturate(_Heat);
                float3 p = input.positionWS * _Scale;
                // The molten skin slides and churns; a cooled crust has set and barely moves.
                float3 drift = float3(0.21, -0.6, 0.37) * _Time.y * _FlowSpeed * (0.15 + heat);
                float warp = Fbm(p * 0.6 + drift * 0.5);
                float n = Fbm(p + drift + warp * 1.7);
                // Veins: the glowing cracks between plates of crust. They widen as it heats until the crust is gone.
                float vein = 1.0 - smoothstep(0.0, _VeinWidth * (0.6 + heat * 1.6), abs(n - 0.5));
                // Hot: an orange-red skin churning with yellow veins. Cooling: the skin darkens to crust and only
                // the veins still glow, duller and duller.
                float skin = heat * heat * (0.3 + 0.3 * n);
                float temperature = skin + vein * (0.18 + 0.38 * heat);
                half3 colour = Ramp(temperature);

                // Only the crust takes light; glowing rock is its own light.
                Light sun = GetMainLight();
                float diffuse = saturate(dot(normalize(input.normalWS), sun.direction)) * 0.8 + 0.25;
                half3 crustLit = colour * diffuse * sun.color;
                float glow = saturate(temperature * 1.6);
                return half4(lerp(crustLit, colour, glow), 1);
            }
            ENDHLSL
        }

        // Depth only, so the lava sorts and shadows like solid rock.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            float4 Vert(Attributes input) : SV_POSITION { return TransformObjectToHClip(input.positionOS.xyz); }
            half Frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
