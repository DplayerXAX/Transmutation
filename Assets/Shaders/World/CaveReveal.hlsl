// Cave reveal lights (set by World/CaveReveal.cs). By default the shown ground fades gradually into the dark round each
// light (_CaveRevealSoft 1); with it off it is the older clean cut, like a stencil: inside a light's radius the terrain is drawn
// with its own normal look, cut cleanly out of the black fog, so a light far away shows the shape of the ground round it
// while everything else stays black. No extra brightness or pattern; only a slight shift towards the light's colour.
// With no lights registered (_CaveRevealCount = 0) nothing changes.
#ifndef CAVE_REVEAL_INCLUDED
#define CAVE_REVEAL_INCLUDED

#define CAVE_REVEAL_MAX 16
float4 _CaveReveal[CAVE_REVEAL_MAX];      // xyz centre, w radius
float4 _CaveRevealTint[CAVE_REVEAL_MAX];  // rgb tint, a strength 0..1 (how much it reveals now)
float _CaveRevealCount;
float _CaveRevealSoft;                    // 1 = fades gradually into the dark, 0 = clean stencil edge

// reveal: 0 = fogged as usual, 1 = shown as it is. tint: colour to multiply the shown surface by.
// Soft mode is a dim light in the dark, not a lit zone: strong only close to the source, a long continuous tail,
// and surfaces turned away from it fall off into black, so the rock's shape reads instead of a patch.
void CaveReveal(float3 positionWS, float3 normalWS, out half reveal, out half3 tint)
{
    reveal = 0;
    tint = 1;
    int count = (int)_CaveRevealCount;
    float total = 0, hardest = 0;
    half3 sum = 0;
    [loop] for (int i = 0; i < count; i++)
    {
        float r = _CaveReveal[i].w;
        float3 d = _CaveReveal[i].xyz - positionWS;
        float dist = length(d);
        if (dist >= r) continue;
        float strength = _CaveRevealTint[i].a;
        hardest = max(hardest, (1 - smoothstep(r - 0.12, r, dist)) * strength);
        float u = dist / r;
        float window = 1 - u * u;
        // Fourth power of the window: the screen's gamma lifts faint values, so a gentler end still shows as an edge.
        window *= window;
        float falloff = window * window / (1 + 14 * u * u);
        float facing = smoothstep(-0.35, 0.75, dot(normalWS, d / max(dist, 0.001)));
        float k = falloff * facing * facing * strength;
        total += k;
        sum += _CaveRevealTint[i].rgb * k;
    }
    float soft = 1 - exp(-total * 1.5);
    reveal = lerp(hardest, soft, _CaveRevealSoft);
    if (total > 0.0001) tint = lerp(1, sum / total, saturate(total * 2));
}
#endif
