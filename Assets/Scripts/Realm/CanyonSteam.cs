using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Warm air rising out of a deep canyon.
///   - Steam: slow, wide puffs seep up from the floor along the whole canyon, swell as they rise and hang about
///     the rim, so looking down from the plateau the floor is half hidden.
///   - Updraft: only straight above a geothermal vent. A fire bubble that drifts into a vent's column is carried
///     up and out of the canyon, then drifts over the plateau and comes down there; elsewhere bubbles float as
///     usual and stay in the canyon.
/// Built by FlameCanyonSetup, which gives it the seep points along the canyon floor.
/// </summary>
[DisallowMultipleComponent]
public sealed class CanyonSteam : MonoBehaviour
{
    [Header("Canyon")]
    [Tooltip("World points on the canyon floor that steam seeps up from.")]
    [SerializeField] private List<Vector3> seeps = new List<Vector3>();
    [Tooltip("World height of the canyon rim.")]
    [SerializeField] private float rimHeight = 42f;

    [Header("Steam")]
    [Tooltip("Puffs per second from each seep.")]
    [Min(0f)] [SerializeField] private float puffsPerSecond = 0.9f;
    [Tooltip("Width of a seep (the canyon floor is narrow; keep it under the floor's half width).")]
    [Min(0.1f)] [SerializeField] private float seepRadius = 2.2f;
    [Tooltip("Size of a puff when it leaves the floor and when it reaches the rim.")]
    [SerializeField] private Vector2 puffSize = new Vector2(3f, 11f);
    [Tooltip("Strongest opacity of a puff (it is thinnest low down and thickest about the rim).")]
    [Range(0f, 1f)] [SerializeField] private float opacity = 0.32f;

    [Header("Updraft")]
    [Tooltip("Bubbles are carried up to this far above the rim before they start to fall.")]
    [Min(0f)] [SerializeField] private float liftAboveRim = 14f;
    [Tooltip("Rising speed of a bubble in the updraft (m/s).")]
    [Min(0.1f)] [SerializeField] private float liftSpeed = 3.5f;
    [Tooltip("Radius of the warm column over each geothermal vent. A bubble that drifts into it is caught and carried out.")]
    [Min(0.5f)] [SerializeField] private float columnRadius = 4f;

    private readonly List<ParticleSystem> plumes = new List<ParticleSystem>();
    private GeothermalVent[] vents;
    private float nextScan;

    public void Setup(List<Vector3> seepPoints, float rim)
    {
        seeps = new List<Vector3>(seepPoints);
        rimHeight = rim;
    }

    private void Start()
    {
        foreach (Vector3 seep in seeps) plumes.Add(BuildPlume(seep));
    }

    private void FixedUpdate()
    {
        if (Time.time < nextScan) return;
        nextScan = Time.time + 0.4f;
        // Time to climb out, plus a little drifting once over the plateau.
        float ceiling = rimHeight + liftAboveRim;
        if (vents == null) vents = FindObjectsByType<GeothermalVent>(FindObjectsSortMode.None);
        foreach (FireBubble bubble in FindObjectsByType<FireBubble>(FindObjectsSortMode.None))
        {
            if (bubble.gameObject.scene != gameObject.scene) continue;
            Vector3 at = bubble.transform.position;
            if (at.y < rimHeight && InColumn(at))
                bubble.Updraft(ceiling, liftSpeed, (ceiling - at.y) / liftSpeed + 8f);
        }
    }

    /// <summary>Inside the rising column of warm air straight above a vent.</summary>
    private bool InColumn(Vector3 at)
    {
        foreach (GeothermalVent vent in vents)
        {
            if (vent == null || !vent.Settled || vent.gameObject.scene != gameObject.scene) continue;
            Vector3 offset = at - vent.transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude < columnRadius * columnRadius) return true;
        }
        return false;
    }

    private ParticleSystem BuildPlume(Vector3 at)
    {
        var go = new GameObject("Canyon Steam");
        go.transform.SetParent(transform, false);
        go.transform.SetPositionAndRotation(at + Vector3.up * 0.3f, Quaternion.Euler(-90f, 0f, 0f));
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // Long-lived puffs that slow as they rise, so they gather in a loose band round the rim.
        float climb = rimHeight - at.y + 6f;
        float life = 26f;
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.8f, life * 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0f);
        main.startSize = new ParticleSystem.MinMaxCurve(puffSize.x * 0.8f, puffSize.x * 1.2f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = Color.white;
        main.maxParticles = 80;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;

        var emission = ps.emission;
        emission.rateOverTime = puffsPerSecond;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = seepRadius;
        shape.rotation = new Vector3(0f, 0f, 0f);

        // Rising speed over life: quick out of the floor, slowing to a hang near the rim. The curve's area is the
        // climb, so the puffs settle round the rim whatever the depth.
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        var rise = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.35f, 0.75f), new Keyframe(0.7f, 0.12f), new Keyframe(1f, 0.04f));
        const float area = 0.48f; // integral of the curve above over 0..1
        velocity.y = new ParticleSystem.MinMaxCurve(climb / (life * area), rise);
        velocity.x = new ParticleSystem.MinMaxCurve(0f, AnimationCurve.Constant(0f, 1f, 0f));
        velocity.z = new ParticleSystem.MinMaxCurve(0f, AnimationCurve.Constant(0f, 1f, 0f));

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.6f;
        noise.frequency = 0.08f;
        noise.scrollSpeed = 0.05f;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        float grow = puffSize.y / Mathf.Max(0.1f, puffSize.x);
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.5f, grow * 0.7f), new Keyframe(1f, grow)));

        var rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);

        // Faintly lit by the fires low down, pale and thick about the rim, then thinning away above it.
        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.62f, 0.4f), 0f), new GradientColorKey(new Color(0.92f, 0.82f, 0.74f), 0.35f),
                    new GradientColorKey(new Color(0.86f, 0.84f, 0.82f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(opacity * 0.35f, 0.12f), new GradientAlphaKey(opacity, 0.55f),
                    new GradientAlphaKey(opacity * 0.8f, 0.8f), new GradientAlphaKey(0f, 1f) });
        colour.color = gradient;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = GeothermalVent.SteamMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.maxParticleSize = 6f;
        ps.Play();
        return ps;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.6f);
        foreach (Vector3 seep in seeps) Gizmos.DrawWireSphere(seep, seepRadius);
    }
}
