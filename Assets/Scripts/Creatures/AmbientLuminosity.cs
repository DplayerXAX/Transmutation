using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>CPU estimate of background illumination, in the same gameplay units as luminous creatures.</summary>
public static class AmbientLuminosity
{
    private static Light[] lights = System.Array.Empty<Light>();
    private static float refreshAt = float.NegativeInfinity;
    private static readonly Vector3[] directions = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
    private static readonly Color[] samples = new Color[6];
    private static readonly Vector3[] skyDirections = { Vector3.up, (Vector3.up + Vector3.right).normalized,
        (Vector3.up + Vector3.left).normalized, (Vector3.up + Vector3.forward).normalized,
        (Vector3.up + Vector3.back).normalized };
    private static RaycastHit[] hits = new RaycastHit[16];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Invalidate() { lights = System.Array.Empty<Light>(); refreshAt = float.NegativeInfinity; }

    private static float Brightness(Color color) => Mathf.Max(0f, color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f);

    public static float Sample(Creature observer)
    {
        float ambient;
        if (RenderSettings.ambientMode == AmbientMode.Flat) ambient = Brightness(RenderSettings.ambientLight);
        else if (RenderSettings.ambientMode == AmbientMode.Trilight)
            ambient = (Brightness(RenderSettings.ambientSkyColor) + Brightness(RenderSettings.ambientEquatorColor) +
                Brightness(RenderSettings.ambientGroundColor)) / 3f;
        else
        {
            LightProbes.GetInterpolatedProbe(observer.AimPosition, null, out var probe);
            probe.Evaluate(directions, samples);
            ambient = 0f;
            foreach (Color sample in samples) ambient += Brightness(sample) / samples.Length;
            ambient *= RenderSettings.ambientIntensity;
        }
        // Shelters must also reduce sky/ambient light, otherwise a roof only blocks the directional lamp.
        int openSky = 0;
        foreach (Vector3 direction in skyDirections)
            if (!observer.gameObject.scene.GetPhysicsScene().Raycast(observer.AimPosition, direction, out _,
                20f, LuminosityQuery.DefaultObstructionMask, QueryTriggerInteraction.Ignore)) openSky++;
        float background = ambient * LuminosityLighting.ReferenceLuminosity * Mathf.Lerp(0.02f, 1f, openSky / 5f);
        if (Time.time >= refreshAt)
        {
            lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            refreshAt = Time.time + 0.5f;
        }
        foreach (Light light in lights)
        {
            if (light == null || !light.isActiveAndEnabled || light.intensity <= 0f ||
                light.gameObject.scene != observer.gameObject.scene ||
                (light.cullingMask & (1 << observer.gameObject.layer)) == 0 ||
                light.GetComponentInParent<Creature>() != null || light.GetComponentInParent<PlayerCreatureStats>() != null) continue;
            // Creature/player lights are accounted for by LuminosityQuery, not counted twice as background.
            float illumination = light.intensity * Brightness(light.color) * (50f / 1.2f);
            Vector3 endpoint;
            if (light.type == LightType.Directional) endpoint = observer.AimPosition - light.transform.forward * 500f;
            else if (light.type == LightType.Point || light.type == LightType.Spot)
            {
                Vector3 delta = observer.AimPosition - light.transform.position;
                float distance = delta.magnitude;
                if (distance >= light.range) continue;
                illumination *= Mathf.Pow(1f - distance / Mathf.Max(0.01f, light.range), 2f) / (1f + delta.sqrMagnitude);
                if (light.type == LightType.Spot)
                {
                    float cosine = Vector3.Dot(light.transform.forward, delta.normalized);
                    float outer = Mathf.Cos(light.spotAngle * Mathf.Deg2Rad * 0.5f);
                    float inner = Mathf.Cos(light.innerSpotAngle * Mathf.Deg2Rad * 0.5f);
                    illumination *= Mathf.InverseLerp(outer, Mathf.Max(outer + 0.0001f, inner), cosine);
                }
                endpoint = light.transform.position;
            }
            else continue;
            if (!CreatureSpatialQuery.FirstSolid(observer.gameObject.scene, observer.AimPosition, endpoint,
                observer, light.gameObject, ref hits, out _)) background += illumination;
        }
        return Mathf.Max(0f, background);
    }
}
