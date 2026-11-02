using UnityEngine;

/// <summary>Draws the owning creature's luminosity without changing its stat.</summary>
public sealed class GlowBeeLight : MonoBehaviour
{
    private Renderer glow;
    private Light lamp;
    private Creature creature;
    private MaterialPropertyBlock properties;
    private Color tint;
    public void Configure(Renderer renderer, Color color, float range)
    {
        glow = renderer;
        tint = color;
        creature = GetComponentInParent<Creature>();
        properties = new MaterialPropertyBlock();
        lamp = gameObject.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = color;
        lamp.range = range;
        lamp.shadows = LightShadows.None;
    }
    private void Update()
    {
        if (lamp == null || glow == null) return;
        float emitted = creature != null && creature.IsAvailableForPerception ? creature.Luminosity : 0f;
        glow.GetPropertyBlock(properties);
        properties.SetColor("_EmissionColor", LuminosityLighting.Emission(tint, emitted));
        glow.SetPropertyBlock(properties);
        lamp.intensity = LuminosityLighting.PointIntensity(emitted);
        lamp.enabled = lamp.intensity > 0.02f;
    }

    private void OnDisable()
    {
        if (lamp != null) lamp.enabled = false;
        if (glow != null && properties != null)
        {
            glow.GetPropertyBlock(properties);
            properties.SetColor("_EmissionColor", Color.black);
            glow.SetPropertyBlock(properties);
        }
    }
}
