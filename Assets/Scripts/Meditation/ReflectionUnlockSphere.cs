using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public enum ReflectionFragmentMode { Dense, OnePerCreature }

[Serializable]
public sealed class ReflectionUnlockSphereSettings
{
    [Tooltip("Relative to the amphitheater: above and just beyond the tablet.")]
    public Vector3 position = new(0f, 19f, 25f);
    [Min(0.1f)] public float radius = 2.3f;
    public int fractureSeed = 17041;
    public ReflectionFragmentMode fragmentMode;
    [Range(1, 512)] public int fragmentCount = 232;
    [Range(2, 5)] public int surfaceSubdivisions = 4;
    [Header("Suspension (metres, degrees, seconds)")]
    [Tooltip("Minimum clearance outside the assembled sphere; locked chunks always clear its entire volume.")]
    [Min(0f)] public float scatterDistance = 3.6f;
    [Range(0f, 180f)] public float scatterRotation = 48f;
    public bool motion = true;
    [Min(0f)] public float driftAmount = 0.055f;
    [Min(0f)] public float driftSpeed = 0.5f;
    [Min(0f)] public float rotationAmount = 7f;
    [Min(0f)] public float rotationSpeed = 0.23f;
    [Header("Restoration")]
    [Min(0.01f)] public float restorationDuration = 1.65f;
    [Min(0f)] public float stagger = 0.045f;
    [Header("Appearance")]
    [Tooltip("Optional override. Defaults to the reflection space's ink stone, with subtle pale highlights.")]
    public Material material;
}

/// <summary>Decorative, visit-owned view of ReflectionCatalog + ReflectionProgress. Never writes saves.</summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class ReflectionUnlockSphere : MonoBehaviour
{
    private sealed class Fragment
    {
        public string owner;
        public Transform transform;
        public MeshRenderer renderer;
        public Mesh mesh;
        public Vector3 assembledPosition, scatterDirection, rotationAxis, fromPosition;
        public Quaternion assembledRotation = Quaternion.identity, fromRotation;
        public float spread, angle, phase, speed, start, boundRadius;
        public bool unlocked, restoring;
    }

    private readonly List<Fragment> fragments = new();
    private readonly HashSet<string> unlocked = new(StringComparer.Ordinal);
    private readonly List<string> roster = new();
    private ReflectionCatalog catalog;
    private ReflectionProgress progress;
    private ReflectionUnlockSphereSettings settings;
    private Material fallbackSource, ownedMaterial, activeMaterial;
    private ReflectionProgress subscribed;
    private GameObject generated;
    private Mesh wholeMesh;
    private MeshRenderer wholeRenderer;
    private string rosterKey;
    private int generationSeed, generationCount, generationDetail;
    private ReflectionFragmentMode generationMode;
    private float nextCheck, clock;
    private bool initialized;
#if UNITY_EDITOR
    private bool preview;
    private readonly List<string> previewUnlocked = new();
#endif

    public int CreatureCount => roster.Count;
    public int UnlockedCreatureCount { get; private set; }
    public int FragmentCount => fragments.Count;
    public int RestoringFragmentCount { get { int count = 0; foreach (var f in fragments) if (f.restoring) count++; return count; } }
    public bool IsComplete => UnlockedCreatureCount == CreatureCount && RestoringFragmentCount == 0;
    public ReflectionUnlockSphereSettings Settings => settings;
    public float MaximumExtent
    {
        get
        {
            float extent = settings.radius;
            foreach (var f in fragments)
                extent = Mathf.Max(extent, settings.radius * (1f + 2f * f.boundRadius) +
                    Mathf.Max(0f, settings.scatterDistance) * (1f + f.spread) + Mathf.Sqrt(3f) * Mathf.Abs(settings.driftAmount) * 2f);
            return extent;
        }
    }

    public void Initialize(ReflectionCatalog sourceCatalog, ReflectionProgress sourceProgress,
        ReflectionUnlockSphereSettings configuration, Material reflectionStone)
    {
        Unsubscribe();
        catalog = sourceCatalog; progress = sourceProgress;
        settings = configuration ?? new ReflectionUnlockSphereSettings();
        fallbackSource = reflectionStone;
        initialized = true;
#if UNITY_EDITOR
        preview = false;
#endif
        CheckConfiguration(true);
        Subscribe();
    }

    private void OnEnable()
    {
        if (!initialized) return;
        Subscribe();
        CheckConfiguration();
        ApplyProgress(false);
    }

    private void OnDisable() => Unsubscribe();
    private void OnDestroy() { Unsubscribe(); ClearGeometry(); ReflectionSphereGeometry.Release(ownedMaterial); }

    private void Subscribe()
    {
        if (subscribed == progress || progress == null || !isActiveAndEnabled) return;
        Unsubscribe(); subscribed = progress; subscribed.Changed += OnProgressChanged;
    }
    private void Unsubscribe()
    {
        if (subscribed != null) subscribed.Changed -= OnProgressChanged;
        subscribed = null;
    }
    private void OnProgressChanged() { CheckConfiguration(); ApplyProgress(true); }

    private void Update()
    {
        if (!initialized || !Application.IsPlaying(gameObject)) return;
        Advance(Time.unscaledDeltaTime);
    }

    private void Advance(float deltaTime)
    {
        clock += Mathf.Max(0f, deltaTime);
        if (clock >= nextCheck) { nextCheck = clock + 0.5f; CheckConfiguration(); }
        foreach (Fragment fragment in fragments)
        {
            if (fragment.restoring)
            {
                if (clock < fragment.start) continue;
                float t = Mathf.Clamp01((clock - fragment.start) / Mathf.Max(0.01f, settings.restorationDuration));
                float ease = t * t * t * (t * (t * 6f - 15f) + 10f); // zero velocity and acceleration at either end
                fragment.transform.localPosition = Vector3.LerpUnclamped(fragment.fromPosition, fragment.assembledPosition, ease);
                fragment.transform.localRotation = Quaternion.SlerpUnclamped(fragment.fromRotation, fragment.assembledRotation, ease);
                if (t >= 1f) { fragment.restoring = false; Assemble(fragment); }
            }
            else if (!fragment.unlocked) Scatter(fragment);
        }
        ShowWholeWhenSettled();
    }

    /// <summary>Called periodically and on events; geometry is only rebuilt when the key changes.</summary>
    public void CheckConfiguration(bool force = false)
    {
        if (!initialized) return;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (catalog != null && catalog.Exhibits != null)
            foreach (var item in catalog.Exhibits)
                if (item != null && !string.IsNullOrWhiteSpace(item.id)) ids.Add(item.id);
        var sorted = new List<string>(ids); sorted.Sort(StringComparer.Ordinal);
        // Length-prefix identities so unusual IDs cannot collide with a separator.
        string key = string.Empty;
        foreach (string id in sorted) key += id.Length + ":" + id;
        int count = sorted.Count == 0 ? 1 : settings.fragmentMode == ReflectionFragmentMode.OnePerCreature
            ? sorted.Count : Mathf.Max(sorted.Count, Mathf.Clamp(settings.fragmentCount, 1, 512));
        int detail = Mathf.Clamp(settings.surfaceSubdivisions, 2, 5);
        transform.localPosition = settings.position;
        transform.localScale = Vector3.one * Mathf.Max(0.1f, settings.radius);
        UpdateMaterial();
        if (!force && generated != null && key == rosterKey && generationSeed == settings.fractureSeed &&
            generationCount == count && generationDetail == detail && generationMode == settings.fragmentMode) return;
        rosterKey = key; generationSeed = settings.fractureSeed; generationCount = count;
        generationDetail = detail; generationMode = settings.fragmentMode;
        roster.Clear(); roster.AddRange(sorted);
        Rebuild(count, detail);
    }

    private void Rebuild(int count, int detail)
    {
        ClearGeometry();
        generated = new GameObject("Generated fragments");
        generated.transform.SetParent(transform, false);
        var chunks = ReflectionSphereGeometry.Generate(count, settings.fractureSeed, detail);
        // Height bands produce a rising, naturally jagged rim. Coordinate tie-breaks are deterministic.
        chunks.Sort((a, b) => {
            int compare = a.centre.y.CompareTo(b.centre.y);
            if (compare == 0) compare = a.centre.x.CompareTo(b.centre.x);
            return compare != 0 ? compare : a.centre.z.CompareTo(b.centre.z);
        });
        for (int i = 0; i < count; i++)
        {
            var chunk = chunks[i];
            var part = new GameObject("Fragment " + i.ToString("D3"));
            part.transform.SetParent(generated.transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = chunk.mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { activeMaterial, activeMaterial };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            // Scatter belongs to the geometry, so assigning the next unlock never moves other debris.
            var random = new System.Random(unchecked((int)StableHash("fragment", settings.fractureSeed ^ i * 7919)));
            var fragment = new Fragment {
                transform = part.transform, renderer = renderer, mesh = chunk.mesh,
                assembledPosition = chunk.centre,
                boundRadius = chunk.mesh.bounds.center.magnitude + chunk.mesh.bounds.extents.magnitude,
                // Independent of assembled height: the remaining top pieces still surround the whole sphere.
                scatterDirection = ReflectionSphereGeometry.Direction(random),
                rotationAxis = ReflectionSphereGeometry.Direction(random),
                spread = Mathf.Lerp(0.16f, 1f, Mathf.Pow((float)random.NextDouble(), 1.6f)),
                angle = ReflectionSphereGeometry.Range(random, -1f, 1f),
                phase = ReflectionSphereGeometry.Range(random, 0f, Mathf.PI * 2f),
                speed = ReflectionSphereGeometry.Range(random, 0.75f, 1.25f)
            };
            fragments.Add(fragment);
        }
        // The identical uncut surface replaces the settled chunks at completion, eliminating internal seams.
        wholeMesh = ReflectionSphereGeometry.Generate(1, settings.fractureSeed, detail)[0].mesh;
        var whole = new GameObject("Seamless completed sphere");
        whole.transform.SetParent(generated.transform, false);
        whole.AddComponent<MeshFilter>().sharedMesh = wholeMesh;
        wholeRenderer = whole.AddComponent<MeshRenderer>();
        wholeRenderer.sharedMaterials = new[] { activeMaterial, activeMaterial };
        wholeRenderer.shadowCastingMode = ShadowCastingMode.Off;
        ApplyProgress(false);
    }

    private void UpdateMaterial()
    {
        Material chosen = settings.material;
        if (chosen == null && ownedMaterial == null)
        {
            Shader shader = fallbackSource != null ? fallbackSource.shader : Shader.Find("Capstone/Meditation/Ink");
            ownedMaterial = fallbackSource != null ? new Material(fallbackSource) : new Material(shader);
            ownedMaterial.name = "Reflection sphere · black ink";
            ownedMaterial.hideFlags = HideFlags.DontSave;
            ReflectionSpace.MakeBlackStone(ownedMaterial);
            ownedMaterial.SetFloat("_RimInk", 0.24f);
            ownedMaterial.SetFloat("_RimPower", 4f);
            ownedMaterial.SetFloat("_HatchStrength", 0.025f);
            ownedMaterial.SetFloat("_ShadeLevels", 6f);
            ownedMaterial.SetColor("_PaperColor", new Color(0.12f, 0.125f, 0.14f, 1f));
            ownedMaterial.SetColor("_ShadeColor", new Color(0.012f, 0.014f, 0.019f, 1f));
            ownedMaterial.SetColor("_InkColor", new Color(0.52f, 0.56f, 0.62f, 1f));
        }
        chosen = chosen != null ? chosen : ownedMaterial;
        if (activeMaterial == chosen) return;
        activeMaterial = chosen;
        foreach (var f in fragments) f.renderer.sharedMaterials = new[] { chosen, chosen };
        if (wholeRenderer != null) wholeRenderer.sharedMaterials = new[] { chosen, chosen };
    }

    private void ApplyProgress(bool animate)
    {
        unlocked.Clear();
        IEnumerable<string> source = progress?.Unlocked;
#if UNITY_EDITOR
        if (preview) source = previewUnlocked;
#endif
        // Saved discovery order assigns each creature the next lowest band. Ignore stale and duplicate IDs.
        var owners = new List<string>();
        if (source != null) foreach (string id in source)
            if (id != null && roster.BinarySearch(id, StringComparer.Ordinal) >= 0 && unlocked.Add(id)) owners.Add(id);
        UnlockedCreatureCount = owners.Count;
        foreach (string id in roster) if (!unlocked.Contains(id)) owners.Add(id);
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < fragments.Count; i++)
        {
            Fragment fragment = fragments[i];
            string owner = owners.Count == 0 ? null : owners[(int)((long)i * owners.Count / fragments.Count)];
            if (fragment.owner != owner || fragment.transform.name.IndexOf('·') < 0)
            {
                fragment.owner = owner;
                fragment.transform.name = "Fragment " + i.ToString("D3") + " · " + (owner ?? "empty roster");
            }
            bool restore = fragment.owner == null || unlocked.Contains(fragment.owner);
            if (restore && !fragment.unlocked && animate)
            {
                order.TryGetValue(fragment.owner ?? "", out int index);
                order[fragment.owner ?? ""] = index + 1;
                fragment.fromPosition = fragment.transform.localPosition;
                fragment.fromRotation = fragment.transform.localRotation;
                fragment.start = clock + index * Mathf.Max(0f, settings.stagger);
                fragment.restoring = true;
            }
            else if (!animate || !restore)
            {
                fragment.restoring = false;
                if (restore) Assemble(fragment); else Scatter(fragment);
            }
            fragment.unlocked = restore;
        }
        ShowWholeWhenSettled();
    }

    private void Scatter(Fragment fragment)
    {
        float inverseRadius = 1f / Mathf.Max(0.1f, settings.radius);
        // Include the complete rotated chunk and maximum drift, keeping the assembled volume empty.
        float driftMargin = settings.motion ? Mathf.Sqrt(3f) * Mathf.Abs(settings.driftAmount) * inverseRadius : 0f;
        Vector3 position = fragment.scatterDirection * (1f + fragment.boundRadius + driftMargin +
            Mathf.Max(0f, settings.scatterDistance) * (1f + fragment.spread) * inverseRadius);
        float angle = fragment.angle * settings.scatterRotation;
        if (settings.motion)
        {
            float t = clock * settings.driftSpeed * fragment.speed + fragment.phase;
            position += new Vector3(Mathf.Sin(t), Mathf.Sin(t * 0.83f + 1f), Mathf.Cos(t * 1.13f)) *
                (settings.driftAmount * inverseRadius);
            angle += Mathf.Sin(clock * settings.rotationSpeed * fragment.speed + fragment.phase) * settings.rotationAmount;
        }
        fragment.transform.localPosition = position;
        fragment.transform.localRotation = Quaternion.AngleAxis(angle, fragment.rotationAxis);
    }
    private static void Assemble(Fragment f) => f.transform.SetLocalPositionAndRotation(f.assembledPosition, f.assembledRotation);

    private void ShowWholeWhenSettled()
    {
        if (wholeRenderer == null) return;
        bool complete = IsComplete;
        // Only change renderer state when necessary; restored fragments stay motionless.
        if (wholeRenderer.enabled == complete && fragments.Count > 0 && fragments[0].renderer.enabled == !complete) return;
        wholeRenderer.enabled = complete;
        foreach (var f in fragments) f.renderer.enabled = !complete;
    }

    private void ClearGeometry()
    {
        if (generated != null) { generated.SetActive(false); ReflectionSphereGeometry.Release(generated); }
        foreach (var f in fragments) ReflectionSphereGeometry.Release(f.mesh);
        fragments.Clear(); ReflectionSphereGeometry.Release(wholeMesh);
        wholeMesh = null; wholeRenderer = null; generated = null;
    }

    public static uint StableHash(string id, int seed)
    {
        unchecked { uint hash = 2166136261u ^ (uint)seed;
            foreach (char c in id) { hash ^= c; hash *= 16777619u; }
            return hash; }
    }

#if UNITY_EDITOR
    /// <summary>In-memory presentation override. Does not call Discover, Unlock, or any save API.</summary>
    public void PreviewFraction(float fraction, bool animate = false)
    {
        preview = true; previewUnlocked.Clear();
        int count = Mathf.RoundToInt(roster.Count * Mathf.Clamp01(fraction));
        for (int i = 0; i < count; i++) previewUnlocked.Add(roster[i]);
        ApplyProgress(animate);
    }
    public void PreviewNextCreature()
    {
        if (!preview)
        {
            previewUnlocked.Clear();
            if (progress != null) foreach (string id in progress.Unlocked)
                if (unlocked.Contains(id) && !previewUnlocked.Contains(id)) previewUnlocked.Add(id);
            preview = true;
        }
        foreach (string id in roster) if (!previewUnlocked.Contains(id)) { previewUnlocked.Add(id); ApplyProgress(true); break; }
    }
    public void PreviewSavedProgress() { preview = false; ApplyProgress(false); }
#endif
}
