#ifndef CAPSTONE_GLITCH_PATTERN_INCLUDED
#define CAPSTONE_GLITCH_PATTERN_INCLUDED

float Hash(float2 p)
{
    float3 h = frac(float3(p.xyx) * 0.1031);
    h += dot(h, h.yzx + 33.33);
    return frac((h.x + h.y) * h.z);
}

float3 SignalColor(float h)
{
    float3 palette;
    if (h < 0.25) palette = _ColorA.rgb;
    else if (h < 0.5) palette = _ColorB.rgb;
    else if (h < 0.75) palette = _ColorC.rgb;
    else palette = _ColorD.rgb;
    float3 spectrum = saturate(abs(frac(h + float3(0, 0.666667, 0.333333)) * 6 - 3) - 1);
    return lerp(palette, spectrum, _Spectrum);
}

half4 Frag(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float2 p = input.surface;
#if !defined(GLITCH_POOL)
    p.y -= _Time.y * _Speed;
#endif
    float bandPosition = p.y * max(_BandDensity, 0.001);
    float section = floor(bandPosition);
    float sectionRandom = Hash(float2(section, _Seed + 17));
    // Uneven subdivision: broad slabs alternate with narrow strips.
    float split = lerp(0.08, 0.92, Hash(float2(section, _Seed + 39)));
    float localY = frac(bandPosition);
    float row = step(split, localY);
    float rowWidth = row > 0.5 ? 1 - split : split;
    float rowKey = section * 2 + row;
    float tick = 0;
#if defined(GLITCH_POOL)
    // Independently clocked rows flash in place; surface coordinates never scroll.
    float rate = lerp(0.45, 1.5, Hash(float2(rowKey, _Seed + 71)));
    tick = floor(_Time.y * max(_FlashRate, 0) * rate + sectionRandom);
#endif
    float key = rowKey + tick * 131 + _Seed * 13;
    float style = Hash(float2(key, 51));
    float rowRandom = Hash(float2(key, 31));
    float blockScale = exp2(lerp(-2, 2, Hash(float2(key, 67))));
    float blockPosition = p.x * _BlockDensity * blockScale + Hash(float2(key, 7)) * 19;
    float block = floor(blockPosition);
    float blockRandom = Hash(float2(block, key));
    float stripePosition = p.x * _StripeDensity * lerp(0.4, 1.8, sectionRandom);
    float stripe = floor(stripePosition);
    float fine = Hash(float2(stripe, key + 23));
    float streak = Hash(float2(stripe, section + tick * 131 + _Seed * 7));
    float visibility = 1 - smoothstep(0.35, 1.5, fwidth(stripePosition));
    float3 signal = SignalColor(blockRandom);
    float3 fineSignal = SignalColor(fine);
    float3 averageSignal = lerp((_ColorA.rgb + _ColorB.rgb + _ColorC.rgb + _ColorD.rgb) * 0.25,
        float3(0.5, 0.5, 0.5), _Spectrum);
    float colored = step(rowRandom, _ColorAmount);
    float3 color;
    if (style < 0.2)
    {
        // Wide solid bars, including the pale and black interruptions in the reference.
        float shade = step(0.48, blockRandom);
        color = lerp(_DarkColor.rgb, _LightColor.rgb, shade);
        color = lerp(color, signal, colored);
    }
    else if (style < 0.43)
    {
        // Blocks of varying width with streaks dragged through them.
        color = lerp(_DarkColor.rgb, _LightColor.rgb, pow(blockRandom, 0.7));
        color = lerp(color, signal, colored);
        color *= lerp(1, lerp(0.6, 0.2 + 0.8 * streak, visibility), _StreakStrength);
    }
    else if (style < 0.7)
    {
        // Fine grayscale / multicolor curtains.
        float3 strands = lerp(lerp(_DarkColor.rgb, _LightColor.rgb, streak), fineSignal, colored);
        float3 average = lerp(lerp(_DarkColor.rgb, _LightColor.rgb, 0.5), averageSignal, colored);
        color = lerp(average, strands, visibility);
    }
    else
    {
        // Sparse luminous RGB lines over near-black bands.
        float occupancy = lerp(0.12, 0.65, Hash(float2(key, 89)));
        float on = step(1 - occupancy, fine);
        float3 strands = lerp(_DarkColor.rgb, lerp(_LightColor.rgb, fineSignal, colored), on);
        float3 average = lerp(_DarkColor.rgb, lerp(_LightColor.rgb, averageSignal, colored), occupancy);
        color = lerp(average, strands, visibility);
    }
    float bandVisibility = 1 - smoothstep(0.5, 2, fwidth(bandPosition) / max(rowWidth, 0.01));
    float3 distantColor = lerp(lerp(_DarkColor.rgb, _LightColor.rgb, 0.4), averageSignal * 0.7, _ColorAmount);
    return half4(lerp(distantColor, color, bandVisibility), 1);
}
#endif
