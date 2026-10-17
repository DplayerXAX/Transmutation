using UnityEngine;

/// <summary>
/// Aerial perspective for the procedural world, after the constant-density air model (extinction and
/// in-scattering both grow with distance) plus an exponential height falloff:
///   - No haze near the player (clear distance), then a long, gentle ramp to fully hidden far away, in the
///     sky's horizon grey on the surface and a dark violet inside the inner world.
///   - The air thins with height: climbing the spire, the far land comes back out of the haze.
///   - Looking towards the sun the haze is brighter and a little warm (forward scattering); away from it, cooler.
///   - Line detail (lines, hatching, dots) fades with the same haze, so far land reads soft and flat.
/// While chunks are still loading the haze is thickened so the unloaded edge is never seen.
/// Put it on the Procedural World object.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ProceduralWorld))]
public sealed class AerialPerspective : MonoBehaviour
{
    [Header("Surface")]
    [Tooltip("Match the sky's horizon haze so far land melts into it.")]
    [SerializeField] private Color surfaceFog = new Color(0.82f, 0.82f, 0.82f);
    [Tooltip("No haze at all nearer than this (metres), standing on the ground.")]
    [Min(0f)] [SerializeField] private float clearDistance = 300f;
    [Tooltip("From the clear distance, how far (metres) until the land is fully hidden.")]
    [Min(5f)] [SerializeField] private float hazeDepth = 420f;
    [Tooltip("Height (metres above the ground under the spawn) over which the air thins to about a third.")]
    [Min(1f)] [SerializeField] private float scaleHeight = 70f;

    [Header("Sun")]
    [Tooltip("Haze colour looking into the sun (forward scattering).")]
    [SerializeField] private Color sunwardFog = new Color(0.95f, 0.92f, 0.86f);
    [Tooltip("Haze colour looking away from the sun.")]
    [SerializeField] private Color awayFog = new Color(0.74f, 0.76f, 0.8f);
    [Tooltip("How tightly the bright haze gathers round the sun (Mie g, 0..0.95).")]
    [Range(0f, 0.95f)] [SerializeField] private float sunFocus = 0.7f;
    [Range(0f, 1f)] [SerializeField] private float sunTint = 0.6f;

    [Header("Inner World")]
    [SerializeField] private Color innerFog = new Color(0.01f, 0f, 0.03f);
    [Min(0f)] [SerializeField] private float innerClearDistance = 4f;
    [Min(1f)] [SerializeField] private float innerHazeDepth = 50f;
    [Tooltip("Metres over which the fog changes when passing between the two worlds.")]
    [Min(0.1f)] [SerializeField] private float blendDistance = 6f;

    [Header("Line Detail")]
    [Tooltip("Lines, hatching and dots start to fade where the haze reaches this strength...")]
    [Range(0f, 1f)] [SerializeField] private float detailFadeStart = 0.12f;
    [Tooltip("...and are gone where it reaches this.")]
    [Range(0f, 1f)] [SerializeField] private float detailFadeEnd = 0.6f;

    [Header("Loading")]
    [Tooltip("Keep the haze opaque this far inside the edge of the loaded area.")]
    [Min(0f)] [SerializeField] private float loadMargin = 20f;
    [Tooltip("How fast the haze clears as chunks arrive (half distance, metres per second).")]
    [Min(1f)] [SerializeField] private float fogFollowSpeed = 60f;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private float insideInner;
    [SerializeField] private float loadedRadius;
    [SerializeField] private float fogEnd;

    private static readonly int DetailStartId = Shader.PropertyToID("_AerialDetailStart");
    private static readonly int DetailEndId = Shader.PropertyToID("_AerialDetailEnd");

    private ProceduralWorld world;
    private float groundY;
    private bool savedFog;
    private FogMode savedMode;
    private Color savedColor;
    private float savedStart, savedEnd, savedDensity;

    /// <summary>Puts the haze back to the recommended settings.</summary>
    public void UseRecommendedDistances()
    {
        clearDistance = 300f;
        hazeDepth = 420f;
        scaleHeight = 70f;
        innerClearDistance = 4f;
        innerHazeDepth = 50f;
        detailFadeStart = 0.12f;
        detailFadeEnd = 0.6f;
    }

    private void OnEnable()
    {
        world = GetComponent<ProceduralWorld>();
        savedFog = RenderSettings.fog;
        savedMode = RenderSettings.fogMode;
        savedColor = RenderSettings.fogColor;
        savedStart = RenderSettings.fogStartDistance;
        savedEnd = RenderSettings.fogEndDistance;
        savedDensity = RenderSettings.fogDensity;
        fogEnd = 60f;
        groundY = float.NaN;
    }

    private void OnDisable()
    {
        // Give the scene its own fog settings back.
        RenderSettings.fog = savedFog;
        RenderSettings.fogMode = savedMode;
        RenderSettings.fogColor = savedColor;
        RenderSettings.fogStartDistance = savedStart;
        RenderSettings.fogEndDistance = savedEnd;
        RenderSettings.fogDensity = savedDensity;
        Shader.SetGlobalFloat(DetailStartId, 0f);
        Shader.SetGlobalFloat(DetailEndId, 0f);
    }

    private void LateUpdate()
    {
        Camera view = Camera.main;
        if (view == null || world == null) return;
        Vector3 eye = view.transform.position;

        // Below the middle of the crust we are in the inner world.
        float target = 0f;
        if (world.LayoutReady)
        {
            world.ColumnHeights(eye, out float surface, out float ceiling, out _);
            float crustMiddle = (surface + ceiling) * 0.5f;
            target = Mathf.Clamp01((crustMiddle - eye.y) / blendDistance + 0.5f);
            if (float.IsNaN(groundY))
            {
                world.ColumnHeights(world.transform.position, out float spawnSurface, out _, out _);
                groundY = spawnSurface;
            }
        }
        insideInner = Mathf.MoveTowards(insideInner, target, Time.deltaTime * 2f);

        // Thinner air higher up: the half distance stretches as the eye climbs above the ground level.
        float height = float.IsNaN(groundY) ? 0f : Mathf.Max(0f, eye.y - groundY);
        float thin = Mathf.Exp(height / scaleHeight * 0.5f);
        float start = Mathf.Lerp(clearDistance * thin, innerClearDistance, insideInner);
        float wanted = start + Mathf.Lerp(hazeDepth * thin, innerHazeDepth, insideInner);

        // While loading, the haze is opaque just inside the loaded disc round the spawn.
        if (world.LayoutReady && !world.GenerationComplete)
        {
            Vector2 size = world.WorldSize;
            loadedRadius = Mathf.Sqrt(world.LoadProgress * size.x * size.y / Mathf.PI);
            wanted = Mathf.Min(wanted, Mathf.Max(loadedRadius - loadMargin, 20f));
        }
        else loadedRadius = float.PositiveInfinity;
        // Thickening is instant (never show a gap); clearing is gradual.
        fogEnd = wanted < fogEnd ? wanted : Mathf.MoveTowards(fogEnd, wanted, fogFollowSpeed * Time.deltaTime);
        start = Mathf.Min(start, fogEnd * 0.7f);

        // Clear up to the start, then a long, gentle ramp to fully hidden.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = start;
        RenderSettings.fogEndDistance = fogEnd;

        // Brighter and warmer towards the sun, cooler away from it (Henyey-Greenstein phase, normalised).
        Color surfaceColour = surfaceFog;
        Light sun = RenderSettings.sun;
        if (sun != null)
        {
            float cosAngle = Vector3.Dot(view.transform.forward, -sun.transform.forward);
            float g = sunFocus;
            float phase = (1f - g * g) / Mathf.Pow(1f + g * g - 2f * g * cosAngle, 1.5f);
            float peak = (1f - g * g) / Mathf.Pow(1f - g, 3f);
            float towardSun = Mathf.Clamp01(Mathf.Pow(phase / peak, 0.35f));
            Color tint = Color.Lerp(awayFog, sunwardFog, towardSun);
            surfaceColour = Color.Lerp(surfaceFog, tint, sunTint);
        }
        RenderSettings.fogColor = Color.Lerp(surfaceColour, innerFog, insideInner);

        // Line detail fades where the haze reaches the given strengths.
        Shader.SetGlobalFloat(DetailStartId, Mathf.Lerp(start, fogEnd, detailFadeStart));
        Shader.SetGlobalFloat(DetailEndId, Mathf.Lerp(start, fogEnd, detailFadeEnd));
    }
}
