using UnityEngine;

/// <summary>Draws the owning creature's luminosity without changing its stat.</summary>
public sealed class GlowBeeLight : MonoBehaviour
{
    private Renderer glow;
    private Light lamp;
    private ICreatureStats source;
    private MaterialPropertyBlock properties;
    private Color tint;
    public void Configure(Renderer renderer, Color color, float range)
    {
        glow = renderer;
        tint = color;
        source = GetComponentInParent<Creature>();
        if (source == null) source = GetComponentInParent<PlayerCreatureStats>();
        properties = new MaterialPropertyBlock();
        lamp = GetComponent<Light>();
        if (lamp == null) lamp = gameObject.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = color;
        lamp.range = range;
        lamp.shadows = LightShadows.None;
        Refresh();
    }
    private void Update() => Refresh();
    public void Refresh()
    {
        if (lamp == null) return;
        bool available = source is Object owner && owner != null && source.IsAvailableForPerception;
        var controller = GetComponentInParent<SmoothFirstPersonController>();
        float emitted = available && (controller == null || controller.enabled) ? source.Luminosity : 0f;
        if (glow != null)
        {
            glow.GetPropertyBlock(properties);
            properties.SetColor("_EmissionColor", LuminosityLighting.Emission(tint, emitted));
            glow.SetPropertyBlock(properties);
        }
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
