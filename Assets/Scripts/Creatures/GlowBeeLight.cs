using UnityEngine;

/// <summary>Emission is always visible; local light strengthens in shadow and pulses like a firefly.</summary>
public sealed class GlowBeeLight : MonoBehaviour
{
    private Renderer glow;
    private Light lamp;
    private Light sun;
    private Creature creature;
    private MaterialPropertyBlock properties;
    private Color tint;
    private float phase, nextSample, darkness = 1f;
    private int sceneryLayers;
    public void Configure(Renderer renderer, Color color, float range, int layers, Light directional)
    {
        glow = renderer;
        tint = color;
        sceneryLayers = layers;
        sun = directional;
        creature = GetComponentInParent<Creature>();
        properties = new MaterialPropertyBlock();
        lamp = gameObject.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = color;
        lamp.range = range;
        lamp.shadows = LightShadows.None;
        phase = Mathf.Abs(GetInstanceID() % 1000) * 0.01f;
        nextSample = Time.time + phase * 0.05f;
    }
    private void Update()
    {
        if (lamp == null || glow == null) return;
        bool alive = creature == null || (!creature.IsDead && !creature.IsConsumptionPending);
        if (Time.time >= nextSample)
        {
            nextSample = Time.time + 0.5f;
            float ambient = RenderSettings.ambientLight.grayscale * RenderSettings.ambientIntensity;
            bool lit = sun != null && sun.isActiveAndEnabled && sun.intensity > 0.05f &&
                !Physics.Raycast(transform.position, -sun.transform.forward, 50f, sceneryLayers, QueryTriggerInteraction.Ignore);
            darkness = lit ? 0.12f : Mathf.Clamp01(1f - ambient);
        }
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 3f + phase);
        float strength = alive ? Mathf.Lerp(0.4f, 3f, darkness) * pulse : 0f;
        glow.GetPropertyBlock(properties);
        properties.SetColor("_EmissionColor", tint * strength);
        glow.SetPropertyBlock(properties);
        lamp.intensity = alive ? darkness * pulse * 1.1f : 0f;
        lamp.enabled = lamp.intensity > 0.02f;
    }
}
