#ifndef LINE_WORLD_LIGHTING_INCLUDED
#define LINE_WORLD_LIGHTING_INCLUDED

// Shared lighting for the black / white line world.
// Light is reduced to a few flat steps; dark areas are filled with hand-drawn style hatching
// instead of smooth gradients. Include after Lighting.hlsl.

// Light level 0..1 from the sun (with shadows), a fake fill direction and nearby point lights.
half LineWorldLight(float3 positionWS, half3 normalWS, float2 screenUV,
    half mainAmount, half3 fakeDirection, half fakeAmount, half levels, out half indirectAO)
{
    Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
    half sun = saturate(dot(normalWS, mainLight.direction)) * mainLight.shadowAttenuation * mainAmount;

    // Wrapped fill light so the side facing away is never fully black.
    half fake = saturate(dot(normalWS, SafeNormalize(fakeDirection)) * 0.5h + 0.5h) * fakeAmount;

    half pointLight = 0.0h;
#if defined(_ADDITIONAL_LIGHTS)
    uint count = GetAdditionalLightsCount();
    for (uint i = 0u; i < count; ++i)
    {
        Light extra = GetAdditionalLight(i, positionWS, half4(1, 1, 1, 1));
        pointLight += saturate(dot(normalWS, extra.direction)) * extra.distanceAttenuation
            * extra.shadowAttenuation * Luminance(extra.color);
    }
#endif

    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(screenUV);
    indirectAO = ao.indirectAmbientOcclusion;

    half light = saturate((sun + fake + pointLight) * ao.directAmbientOcclusion);
    levels = max(levels, 1.0h);
    return floor(light * levels + 0.5h) / levels;
}

// Anti-aliased thin line on integer values of a coordinate. Fades out when lines get too dense.
half LineWorldStripe(float coordinate, half widthPixels)
{
    float fw = max(fwidth(coordinate), 1e-5);
    float d = abs(frac(coordinate + 0.5) - 0.5);
    half stripe = 1.0h - smoothstep(fw * widthPixels * 0.5, fw * (widthPixels * 0.5 + 1.0), d);
    return stripe * saturate(1.5h - fw * 4.0h);
}

// World-space hatching: one direction in half shadow, crossed in deep shadow.
// The lines wobble slightly so they never look ruled.
half LineWorldHatch(float3 positionWS, half light, half scale, half widthPixels)
{
    float3 a = float3(0.71, 0.31, 0.63);
    float3 b = float3(-0.58, 0.45, 0.68);
    float wobbleA = 0.18 * sin(dot(positionWS, float3(0.9, 1.7, -1.3)) * 1.1);
    float wobbleB = 0.18 * sin(dot(positionWS, float3(-1.4, 0.8, 1.6)) * 1.3);
    half hatchA = LineWorldStripe(dot(positionWS, a) * scale + wobbleA, widthPixels);
    half hatchB = LineWorldStripe(dot(positionWS, b) * scale * 1.13 + wobbleB, widthPixels);
    half dark = 1.0h - light;
    return max(hatchA * step(0.3h, dark), hatchB * step(0.65h, dark));
}

// Thin-film style shift between two colours by view angle and position, for optical surfaces.
half3 LineWorldSheen(half3 colorA, half3 colorB, half3 normalWS, half3 viewDirWS, float3 positionWS, half amount)
{
    half facing = saturate(dot(normalWS, viewDirWS));
    float phase = facing * 2.3 + dot(positionWS, float3(0.043, 0.071, 0.057));
    half t = 0.5h + 0.5h * sin(phase * 6.2831853);
    return lerp(colorA, lerp(colorA, colorB, t), amount);
}

// Simple distance haze per material; alpha of the colour is the maximum strength.
half3 LineWorldHaze(half3 color, float3 positionWS, half4 hazeColor, float hazeStart, float hazeEnd)
{
    float dist = distance(positionWS, GetCameraPositionWS());
    half f = saturate((dist - hazeStart) / max(hazeEnd - hazeStart, 1e-3)) * hazeColor.a;
    return lerp(color, hazeColor.rgb, f);
}

#endif
