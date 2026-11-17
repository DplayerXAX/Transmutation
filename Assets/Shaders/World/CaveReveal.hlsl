// Cave reveal lights (set by World/CaveReveal.cs). Works like a stencil: inside a light's radius the terrain is drawn
// with its own normal look, cut cleanly out of the black fog, so a light far away shows the shape of the ground round it
// while everything else stays black. No extra brightness or pattern; only a slight shift towards the light's colour.
// With no lights registered (_CaveRevealCount = 0) nothing changes.
#ifndef CAVE_REVEAL_INCLUDED
#define CAVE_REVEAL_INCLUDED

#define CAVE_REVEAL_MAX 16
float4 _CaveReveal[CAVE_REVEAL_MAX];      // xyz centre, w radius
float4 _CaveRevealTint[CAVE_REVEAL_MAX];  // rgb tint, a strength 0..1 (how much it reveals now)
float _CaveRevealCount;

// reveal: 0 = fogged as usual, 1 = shown as it is. tint: colour to multiply the shown surface by.
void CaveReveal(float3 positionWS, out half reveal, out half3 tint)
{
    reveal = 0;
    tint = 1;
    int count = (int)_CaveRevealCount;
    [loop] for (int i = 0; i < count; i++)
    {
        float r = _CaveReveal[i].w;
        float dist = length(positionWS - _CaveReveal[i].xyz);
        // A clean cut at the edge of the light, a few centimetres wide.
        float k = (1 - smoothstep(r - 0.12, r, dist)) * _CaveRevealTint[i].a;
        if (k > reveal) { reveal = k; tint = lerp(1, _CaveRevealTint[i].rgb, k); }
    }
}
#endif
