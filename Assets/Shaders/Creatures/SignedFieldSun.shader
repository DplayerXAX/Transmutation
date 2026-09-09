Shader "Capstone/Creature/SignedFieldSun"
{
    Properties
    {
        [Header(Orbiting Spheres)]
        [IntRange] _SphereCount("Sphere Count (including center)", Range(2, 8)) = 6
        _SphereRadius("Sphere Radius", Range(0.04, 0.4)) = 0.18
        _Movement("Orbit Radius", Range(0, 0.7)) = 0.27
        _Speed("Orbit Speed (radians per second)", Range(0, 4)) = 0.45
        _OrbitTilt("Orbit Tilt", Range(0, 1)) = 0.65
        _SizeVariation("Size Variation", Range(0, 0.75)) = 0.3
        _SizeSpeed("Size Change Speed", Range(0, 2)) = 0.35
        _Blend("Gel Blend", Range(0.01, 0.4)) = 0.12
        _ViewSize("Size Inside Mesh", Range(0.1, 1)) = 1
        _Center("Object Center Offset", Vector) = (0, 0, 0, 0)
        [Header(Purple Night Sky)]
        _SkyColor("Night Sky Color", Color) = (0.018, 0.003, 0.075, 1)
        _NebulaColor("Nebula Color", Color) = (0.19, 0.025, 0.55, 1)
        [HDR] _StarColor("Star Color", Color) = (1.1, 0.8, 1.6, 1)
        _StarDensity("Star Density", Range(10, 100)) = 45
        _StarBrightness("Star Brightness", Range(0, 4)) = 1.4
        _TwinkleSpeed("Star Twinkle Speed", Range(0, 4)) = 0.8
        [Header(Gel Surface)]
        _BaseColor("Gel Tint", Color) = (0.85, 0.72, 1, 1)
        [HDR] _RimColor("Rim Tint", Color) = (1, 1, 1, 1)
        _RimStrength("Rim Strength", Range(0, 5)) = 2
        _RimWidth("Rim Width", Range(1, 12)) = 5
        _RainbowAmount("Rainbow Rim", Range(0, 1)) = 1
        _Gloss("Gloss", Range(8, 128)) = 64
        _Refraction("Sky Distortion", Range(0, 0.5)) = 0.12
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }
        Pass
        {
            Name "SignedFieldSun"
            Tags { "LightMode"="SRPDefaultUnlit" }
            // The enclosing mesh is only a ray-marching proxy. Its empty pixels are discarded.
            Cull Front
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Center, _SkyColor, _NebulaColor, _StarColor, _BaseColor, _RimColor;
                float _SphereCount, _SphereRadius, _Movement, _Speed, _OrbitTilt, _Blend, _ViewSize;
                float _SizeVariation, _SizeSpeed;
                float _StarDensity, _StarBrightness, _TwinkleSpeed;
                float _RimStrength, _RimWidth, _RainbowAmount, _Gloss, _Refraction;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            struct FragmentOutput
            {
                half4 color : SV_Target;
                float depth : SV_Depth;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }
            float Noise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1,0,0)), f.x),
                                 lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x),
                                 lerp(Hash(i + float3(0,1,1)), Hash(i + 1), f.x), f.y), f.z);
            }
            float3 NightSky(float3 direction)
            {
                float3 p = normalize(direction) * 3.5 + float3(7.3, 19.1, 3.7);
                float cloud = Noise(p) * 0.57 + Noise(p * 2.03) * 0.28 + Noise(p * 4.11) * 0.15;
                float3 sky = lerp(_SkyColor.rgb, _NebulaColor.rgb, smoothstep(0.25, 0.85, cloud));
                // A 3D star lattice avoids longitude seams at the poles.
                float3 starPosition = normalize(direction) * _StarDensity;
                float3 cell = floor(starPosition);
                float3 local = frac(starPosition) - 0.5;
                float random = Hash(cell + 13.7);
                float radius = lerp(0.045, 0.13, random);
                float width = max(length(fwidth(starPosition)) * 0.5, 0.003);
                float star = 1 - smoothstep(radius, radius + width, length(local));
                star *= step(0.78, random);
                float twinkle = 0.65 + 0.35 * sin(_Time.y * _TwinkleSpeed * (1 + random) + random * 31);
                return sky + star * twinkle * _StarBrightness * _StarColor.rgb;
            }
            float SmoothUnion(float a, float b)
            {
                float k = max(_Blend, 0.0001);
                float h = saturate(0.5 + 0.5 * (b - a) / k);
                return lerp(b, a, h) - k * h * (1 - h);
            }
            float SphereSize(float index)
            {
                // Independent smooth random targets, rather than synchronized pulses.
                float seed = Hash(float3(index, 23, 51));
                float clock = _Time.y * _SizeSpeed * lerp(0.65, 1.35, seed) + seed * 17;
                float tick = floor(clock);
                float blend = frac(clock);
                blend = blend * blend * (3 - 2 * blend);
                float a = Hash(float3(index, tick, 97));
                float b = Hash(float3(index, tick + 1, 97));
                return 1 + (lerp(a, b, blend) * 2 - 1) * _SizeVariation;
            }
            float Field(float3 p, float4 spheres[8], int count)
            {
                float d = length(p - spheres[0].xyz) - spheres[0].w;
                [loop] for (int i = 1; i < count; i++)
                    d = SmoothUnion(d, length(p - spheres[i].xyz) - spheres[i].w);
                return d;
            }

            FragmentOutput Frag(Varyings input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                int count = clamp((int)round(_SphereCount), 2, 8);
                float4 spheres[8];
                spheres[0] = float4(0, 0, 0, _SphereRadius * 1.2 * SphereSize(0));
                float time = _Time.y * _Speed;
                [loop] for (int i = 1; i < 8; i++)
                {
                    float phase = TWO_PI * (i - 1) / (count - 1);
                    float seed = Hash(float3(i, 31, 73));
                    float direction = ((i & 1) == 0) ? -1 : 1;
                    float localTime = time * direction * lerp(0.65, 1.35, seed);
                    float angle = localTime + phase;
                    // Slowly precess each orbit so it does not stay on one fixed plane.
                    float tilt = _OrbitTilt * (lerp(-1.4, 1.4, seed) + 0.45 * sin(localTime * 0.37 + phase));
                    float3 orbit = float3(cos(angle), sin(angle) * cos(tilt), sin(angle) * sin(tilt));
                    float heading = _OrbitTilt * (seed * TWO_PI + localTime * 0.23);
                    orbit = float3(orbit.x * cos(heading) + orbit.z * sin(heading), orbit.y,
                                   -orbit.x * sin(heading) + orbit.z * cos(heading));
                    float radius = _SphereRadius * lerp(0.55, 0.95, Hash(float3(i, 7, 11))) * SphereSize(i);
                    spheres[i] = float4(orbit * _Movement, radius);
                }

                // Fit the complete field inside a standard unit cube or sphere proxy.
                // Use maximum possible size, so breathing never changes the overall scale.
                float bound = _Movement + _SphereRadius * 1.2 * (1 + _SizeVariation) + _Blend * 1.75 + 0.01;
                float fieldScale = 0.48 * _ViewSize / max(bound, 0.001);
                float3 cameraOS = TransformWorldToObject(GetCameraPositionWS());
                float3 rayDirection = normalize(input.positionOS - cameraOS);
                float3 rayOrigin = (cameraOS - _Center.xyz) / fieldScale;
                if (unity_OrthoParams.w > 0.5)
                {
                    rayDirection = normalize(TransformWorldToObjectDir(-UNITY_MATRIX_V[2].xyz, false));
                    float3 surface = (input.positionOS - _Center.xyz) / fieldScale;
                    rayOrigin = surface - rayDirection * (dot(surface, rayDirection) + bound + 0.01);
                }

                float b = dot(rayOrigin, rayDirection);
                float c = dot(rayOrigin, rayOrigin) - bound * bound;
                float discriminant = b * b - c;
                clip(discriminant);
                float root = sqrt(max(discriminant, 0));
                float travel = max(0, -b - root);
                float farDistance = -b + root;
                clip(farDistance);
                float3 p = rayOrigin;
                bool hit = false;
                [loop] for (int stepIndex = 0; stepIndex < 96; stepIndex++)
                {
                    p = rayOrigin + rayDirection * travel;
                    float d = Field(p, spheres, count);
                    if (abs(d) < 0.001) { hit = true; break; }
                    travel += max(abs(d) * 0.85, 0.0005);
                    if (travel > farDistance) break;
                }
                if (!hit) discard;

                float2 e = float2(0.001, 0);
                float3 normal = normalize(float3(
                    Field(p + e.xyy, spheres, count) - Field(p - e.xyy, spheres, count),
                    Field(p + e.yxy, spheres, count) - Field(p - e.yxy, spheres, count),
                    Field(p + e.yyx, spheres, count) - Field(p - e.yyx, spheres, count)));
                float3 view = -rayDirection;
                float facing = saturate(dot(normal, view));
                float3 skyDirection = TransformObjectToWorldDir(normalize(rayDirection + normal * _Refraction));
                float3 sky = NightSky(skyDirection);
                float3 color = sky * _BaseColor.rgb * lerp(0.65, 1, facing);
                float hue = atan2(normal.y, normal.x) / TWO_PI + normal.z * 0.17;
                float3 rainbow = saturate(abs(frac(hue + float3(0, 0.666667, 0.333333)) * 6 - 3) - 1);
                float rim = pow(1 - facing, _RimWidth);
                color += lerp(1.0.xxx, rainbow, _RainbowAmount) * _RimColor.rgb * rim * _RimStrength;
                float3 lightDirection = normalize(float3(-0.5, 0.65, -1));
                float gloss = pow(saturate(dot(normal, normalize(lightDirection + view))), _Gloss);
                color += gloss * 0.12 * _StarColor.rgb;

                float3 hitOS = _Center.xyz + p * fieldScale;
                float4 hitCS = TransformObjectToHClip(hitOS);
                FragmentOutput output;
                output.color = half4(color, 1);
                output.depth = hitCS.z / hitCS.w;
                #if UNITY_REVERSED_Z == 0
                    output.depth = (output.depth - UNITY_NEAR_CLIP_VALUE) / (1 - UNITY_NEAR_CLIP_VALUE);
                #endif
                return output;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
