using System.Collections.Generic;
using UnityEngine;

/// <summary>Shared health-hit feedback. Restores its own overrides before normal visual updates.</summary>
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public sealed class CreatureHurtResponse : MonoBehaviour
{
    private sealed class Surface
    {
        public Renderer Renderer;
        public int Slot;
        public MaterialPropertyBlock Original = new MaterialPropertyBlock();
        public MaterialPropertyBlock Tinted = new MaterialPropertyBlock();
        public Material Material;
    }
    private static readonly int[] colors = {
        Shader.PropertyToID("_BaseColor"), Shader.PropertyToID("_Color"),
        Shader.PropertyToID("_RootColor"), Shader.PropertyToID("_TipColor"),
        Shader.PropertyToID("_InnerColor"), Shader.PropertyToID("_TintColor") };
    private static readonly int HurtId = Shader.PropertyToID("_CreatureHurt");
    private readonly List<Surface> surfaces = new List<Surface>();
    private Creature creature;
    private float remaining;
    private bool painted;
    private Transform pivot;
    private Vector3 posePosition;
    private Quaternion poseRotation;
    private bool posed;
    public float Strength => Mathf.Clamp01(remaining / 0.2f);
    public Vector3 LocalDirection { get; private set; }
    private AudioSource audioSource;
    private static AudioClip hitClip;

    public void React(Creature source)
    {
        creature = GetComponent<Creature>();
        remaining = 0.2f;
        LocalDirection = source != null ? transform.InverseTransformDirection((transform.position - source.transform.position).normalized) : Vector3.back;
        Restore();
        surfaces.Clear();
        foreach (var renderer in GetComponentsInChildren<Renderer>(false))
        {
            if (renderer.GetComponentInParent<Creature>() != creature) continue;
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] != null) surfaces.Add(new Surface { Renderer = renderer, Slot = i, Material = materials[i] });
        }
        // Generic creatures with an explicit, collider-free visual can also recoil safely.
        if (GetComponent<AsuraPresentation>() == null)
        {
            var visual = transform.Find("Visual");
            if (visual != null && visual.GetComponentInChildren<Collider>() == null && visual.GetComponentInChildren<Animator>() == null) pivot = visual;
        }
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false; audioSource.spatialBlend = 1f;
            audioSource.minDistance = 1f; audioSource.maxDistance = 18f; audioSource.priority = 180;
        }
        if (hitClip == null)
        {
            const int count = 2205;
            float[] samples = new float[count];
            for (int i = 0; i < count; i++)
            { float t = i / 22050f; float envelope = Mathf.Pow(1f - i / (float)count, 3f); samples[i] = Mathf.Sin(t * 950f + Mathf.Sin(t * 2700f) * 2f) * envelope * 0.4f; }
            hitClip = AudioClip.Create("Creature contact", count, 1, 22050, false); hitClip.SetData(samples, 0);
        }
        audioSource.pitch = Random.Range(0.9f, 1.1f);
        audioSource.clip = hitClip; audioSource.volume = 0.45f; audioSource.Play();
    }

    private void Update()
    {
        Restore();
        if (creature != null && (creature.IsDead || !creature.SimulationEnabled)) remaining = 0f;
        else remaining = Mathf.Max(0f, remaining - Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (remaining <= 0f) return;
        foreach (var surface in surfaces)
        {
            if (surface.Renderer == null) continue;
            surface.Renderer.GetPropertyBlock(surface.Original, surface.Slot);
            // Preserve renderer-level properties used by procedural and faction presentations.
            surface.Renderer.GetPropertyBlock(surface.Tinted, surface.Slot);
            if (surface.Tinted.isEmpty) surface.Renderer.GetPropertyBlock(surface.Tinted);
            if (surface.Material.HasProperty(HurtId)) surface.Tinted.SetFloat(HurtId, Mathf.Clamp01(Strength * 2f));
            foreach (int color in colors)
            {
                if (!surface.Material.HasProperty(color)) continue;
                Color baseline = surface.Tinted.HasProperty(color) ? surface.Tinted.GetColor(color) : surface.Material.GetColor(color);
                Color red = new Color(1f, 0.08f, 0.06f, baseline.a);
                surface.Tinted.SetColor(color, Color.Lerp(baseline, red, 0.8f * Mathf.Clamp01(Strength * 2f)));
            }
            surface.Renderer.SetPropertyBlock(surface.Tinted, surface.Slot);
        }
        painted = true;
        if (pivot != null && creature != null && !creature.IsCarried && !creature.IsBubbleCaptured)
        {
            posePosition = pivot.localPosition; poseRotation = pivot.localRotation;
            pivot.localPosition += LocalDirection * (0.05f * Strength);
            pivot.localRotation *= Quaternion.Euler(-LocalDirection.z * 8f * Strength, 0f, LocalDirection.x * 8f * Strength);
            posed = true;
        }
    }

    private void Restore()
    {
        if (painted)
            foreach (var surface in surfaces)
                if (surface.Renderer != null) surface.Renderer.SetPropertyBlock(surface.Original.isEmpty ? null : surface.Original, surface.Slot);
        painted = false;
        if (posed && pivot != null) { pivot.localPosition = posePosition; pivot.localRotation = poseRotation; }
        posed = false;
    }
    private void OnDisable() { Restore(); remaining = 0f; }
}
