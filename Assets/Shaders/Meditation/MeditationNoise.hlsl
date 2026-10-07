#ifndef MEDITATION_NOISE_INCLUDED
#define MEDITATION_NOISE_INCLUDED

// Small value noise helpers shared by the meditation shaders.

float MedHash31(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.x + p.y) * p.z);
}

float MedNoise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);

    float n000 = MedHash31(i);
    float n100 = MedHash31(i + float3(1, 0, 0));
    float n010 = MedHash31(i + float3(0, 1, 0));
    float n110 = MedHash31(i + float3(1, 1, 0));
    float n001 = MedHash31(i + float3(0, 0, 1));
    float n101 = MedHash31(i + float3(1, 0, 1));
    float n011 = MedHash31(i + float3(0, 1, 1));
    float n111 = MedHash31(i + float3(1, 1, 1));

    float x00 = lerp(n000, n100, u.x);
    float x10 = lerp(n010, n110, u.x);
    float x01 = lerp(n001, n101, u.x);
    float x11 = lerp(n011, n111, u.x);
    return lerp(lerp(x00, x10, u.y), lerp(x01, x11, u.y), u.z);
}

// Two octaves, roughly 0..1.
float MedFbm3(float3 p)
{
    return MedNoise3(p) * 0.65 + MedNoise3(p * 2.17 + 11.3) * 0.35;
}

// Anti-aliased thin line where value crosses whole numbers. Width in pixels.
float MedLine(float value, float widthPixels)
{
    float d = abs(frac(value + 0.5) - 0.5);
    float w = max(fwidth(value), 1e-5) * widthPixels;
    return 1.0 - smoothstep(w * 0.5, w, d);
}

#endif
