using UnityEngine;

/// <summary>Shared gameplay brightness scale: 50 luminosity gives a 1.2-intensity local light.</summary>
public static class LuminosityLighting
{
    public const float ReferenceLuminosity = 50f;
    public static float PointIntensity(float luminosity) => Mathf.Max(0f, luminosity) * (1.2f / ReferenceLuminosity);
    public static Color Emission(Color tint, float luminosity) => tint * (Mathf.Max(0f, luminosity) * 3f / ReferenceLuminosity);
}
