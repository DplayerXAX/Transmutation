using UnityEngine;

/// <summary>
/// Ambient meadow particles: pollen and light motes drifting slowly up and sideways over the flowers, in warm gold
/// with a few petal colours. The emitter box follows the camera while it is inside the meadow area, so the motes
/// are always round the player without filling the whole map; they fade out when the player leaves.
/// All particle settings are applied in code (the ParticleSystem component only stores them).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(ParticleSystem))]
public sealed class MeadowMotes : MonoBehaviour
{
    [Tooltip("Meadow centre (x, z) and radius: motes only spawn while the camera is inside.")]
    [SerializeField] private Vector2 areaCentre = new Vector2(-55f, 45f);
    [Min(5f)] [SerializeField] private float areaRadius = 95f;
    [Tooltip("Size of the box round the camera that motes appear in (metres).")]
    [SerializeField] private Vector3 box = new Vector3(36f, 6f, 36f);
    [Min(1f)] [SerializeField] private float perSecond = 70f;
    [SerializeField] private Material material;

    private ParticleSystem system;

    private void OnEnable()
    {
        system = GetComponent<ParticleSystem>();
        Configure();
    }

    private void OnValidate() { if (isActiveAndEnabled) { system = GetComponent<ParticleSystem>(); Configure(); } }

    private void Configure()
    {
        var main = system.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 12f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.075f);
        main.maxParticles = 1200;
        var gold = new Color(1f, 0.82f, 0.45f, 0.9f);
        main.startColor = new ParticleSystem.MinMaxGradient(Gradient(
            (gold, 0f), (new Color(1f, 0.95f, 0.8f, 0.9f), 0.55f), (new Color(1f, 0.45f, 0.6f, 0.85f), 0.75f), (new Color(0.55f, 0.65f, 1f, 0.85f), 1f)))
            { mode = ParticleSystemGradientMode.RandomColor };

        var emission = system.emission;
        emission.rateOverTime = perSecond;

        var shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = box;
        shape.position = new Vector3(0f, box.y * 0.5f - 0.5f, 0f);

        // Slow rise and drift, wandering on soft noise like pollen in warm air.
        var velocity = system.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
        var noise = system.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 0.25f;
        noise.scrollSpeed = 0.15f;
        noise.octaveCount = 2;

        // Fade in and out, and twinkle slightly.
        var colour = system.colorOverLifetime;
        colour.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        colour.color = fade;
        var size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.7f)));

        var renderer = GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.maxParticleSize = 0.02f;
    }

    private static Gradient Gradient(params (Color colour, float at)[] keys)
    {
        var g = new Gradient();
        var c = new GradientColorKey[keys.Length];
        var a = new GradientAlphaKey[keys.Length];
        for (int i = 0; i < keys.Length; i++) { c[i] = new GradientColorKey(keys[i].colour, keys[i].at); a[i] = new GradientAlphaKey(keys[i].colour.a, keys[i].at); }
        g.SetKeys(c, a);
        return g;
    }

    private void LateUpdate()
    {
        Camera eye = Camera.main;
        if (eye == null || system == null) return;
        Vector3 at = eye.transform.position;
        bool inside = new Vector2(at.x - areaCentre.x, at.z - areaCentre.y).sqrMagnitude < areaRadius * areaRadius;
        var emission = system.emission;
        emission.enabled = inside;
        if (inside) transform.position = new Vector3(at.x, at.y - 1.5f, at.z);
    }

    /// <summary>Fills the air round a point at once (for review captures, where nothing simulates).</summary>
    public void FillAround(Vector3 point)
    {
        transform.position = point;
        system.Simulate(10f, true, true);
    }
}
