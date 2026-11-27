using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Play-mode look for a WindArea, built from code so any area (prefab or hand-placed) shows its flow:
/// ink-outlined streaks born on the upwind face that run the length of the box (dense and fast for a wall,
/// sparse for a breeze), plus a few tumbling hollow motes drifting through. Both use Capstone/Wind/Streak.
/// </summary>
internal sealed class WindVisuals
{
    private static Shader shader;
    private static readonly List<ParticleSystemVertexStream> streams = new List<ParticleSystemVertexStream>
    {
        ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
        ParticleSystemVertexStream.UV, ParticleSystemVertexStream.StableRandomX,
    };

    private readonly WindArea area;
    private GameObject root;
    private ParticleSystem streaks, motes;
    private Material streakMaterial, moteMaterial;
    private bool ownsStreakMaterial;

    public WindVisuals(WindArea area) { this.area = area; }

    public void Rebuild()
    {
        if (area == null) return;
        if (root == null)
        {
            root = new GameObject("Wind Visuals");
            root.transform.SetParent(area.transform, false);
            streaks = Create("Streaks");
            motes = Create("Motes");
            BuildMaterials();
        }
        ConfigureStreaks();
        ConfigureMotes();
    }

    public void Destroy()
    {
        if (root != null) Object.Destroy(root);
        if (ownsStreakMaterial && streakMaterial != null) Object.Destroy(streakMaterial);
        if (moteMaterial != null) Object.Destroy(moteMaterial);
        root = null;
    }

    private ParticleSystem Create(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        var system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.SetActiveVertexStreams(streams);
        return system;
    }

    private void BuildMaterials()
    {
        if (shader == null) shader = Resources.Load<Shader>("WindStreak") ?? Shader.Find("Capstone/Wind/Streak");
        ownsStreakMaterial = area.StreakMaterial == null;
        streakMaterial = ownsStreakMaterial && shader != null ? new Material(shader) { name = "Wind Streak (runtime)" } : area.StreakMaterial;
        if (streakMaterial != null && ownsStreakMaterial)
        {
            streakMaterial.SetColor("_Color", area.StreakColor);
            streakMaterial.SetColor("_InkColor", area.InkColor);
        }
        if (streakMaterial != null)
        {
            moteMaterial = new Material(streakMaterial) { name = "Wind Mote (runtime)" };
            moteMaterial.SetFloat("_Shape", 1f);
        }
        streaks.GetComponent<ParticleSystemRenderer>().sharedMaterial = streakMaterial;
        motes.GetComponent<ParticleSystemRenderer>().sharedMaterial = moteMaterial;
    }

    private void ConfigureStreaks()
    {
        Vector3 size = area.Size;
        float speed = Mathf.Max(0.5f, area.Speed);
        float lifetime = Mathf.Clamp(size.z / speed, 0.15f, 6f);
        float face = size.x * size.y;

        var main = streaks.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startSpeed = 0f;
        main.startLifetime = lifetime;
        main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        main.startColor = Color.white;
        main.maxParticles = 600;
        main.gravityModifier = 0f;

        var emission = streaks.emission;
        // Density follows face area and speed: a wall reads as a solid curtain, a breeze as a few wisps.
        emission.rateOverTime = Mathf.Clamp(face * speed * 1.6f, 4f, 300f);

        var shape = streaks.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.position = new Vector3(0f, 0f, -size.z * 0.5f);
        shape.scale = new Vector3(size.x, size.y, 0.01f);
        shape.rotation = Vector3.zero;

        var velocity = streaks.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.z = new ParticleSystem.MinMaxCurve(speed * 0.85f, speed * 1.1f);

        var noise = streaks.noise;
        noise.enabled = true;
        noise.strength = Mathf.Min(1.2f, 0.12f * speed);
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.4f;
        noise.quality = ParticleSystemNoiseQuality.Low;

        var color = streaks.colorOverLifetime;
        color.enabled = true;
        color.color = Fade(0.12f, 0.8f);

        var renderer = streaks.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = Mathf.Lerp(0.18f, 0.1f, Mathf.InverseLerp(2f, 14f, speed));
        renderer.lengthScale = 2.5f;
        renderer.sortMode = ParticleSystemSortMode.None;

        streaks.Play();
    }

    private void ConfigureMotes()
    {
        Vector3 size = area.Size;
        float speed = Mathf.Max(0.5f, area.Speed);

        var main = motes.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.startSpeed = 0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
        main.maxParticles = 120;

        var emission = motes.emission;
        emission.rateOverTime = Mathf.Clamp(size.x * size.y * size.z * 0.35f, 1f, 40f);

        var shape = motes.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.position = Vector3.zero;
        shape.scale = size;

        // Motes lag the wind a little, as if they had weight.
        var velocity = motes.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
        velocity.y = new ParticleSystem.MinMaxCurve(-0.2f, 0.25f);
        velocity.z = new ParticleSystem.MinMaxCurve(speed * 0.35f, speed * 0.6f);

        var noise = motes.noise;
        noise.enabled = true;
        noise.strength = 0.6f;
        noise.frequency = 0.9f;
        noise.quality = ParticleSystemNoiseQuality.Low;

        var color = motes.colorOverLifetime;
        color.enabled = true;
        color.color = Fade(0.2f, 0.75f);

        var renderer = motes.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.None;

        motes.Play();
    }

    private static ParticleSystem.MinMaxGradient Fade(float fadeIn, float fadeOut)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(1f, fadeOut), new GradientAlphaKey(0f, 1f) });
        return new ParticleSystem.MinMaxGradient(gradient);
    }
}
