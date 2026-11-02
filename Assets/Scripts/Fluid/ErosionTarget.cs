using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A solid that erosive water wears away without being terrain, such as a creature's weak point.
/// Its colliders stay solid to droplets even under a Creature (water passes through other creature
/// colliders), and touching, held and thrown water all deliver erosion to it through Eroded.
/// Held water measures overlap by penetration, which handles primitives and convex or concave meshes.
/// </summary>
[DisallowMultipleComponent]
public sealed class ErosionTarget : MonoBehaviour
{
    [Tooltip("Colliders water wears away. Empty uses every non-trigger collider in children.")]
    [SerializeField] private Collider[] surfaces = new Collider[0];
    [Tooltip("Multiplier on the erosion water delivers.")]
    [Min(0f)] [SerializeField] private float sensitivity = 1f;
    [Tooltip("Most erosion taken per second from all touching water together; 0 is uncapped.")]
    [Min(0f)] [SerializeField] private float maximumPerSecond = 1f;
    [Tooltip("Erosion from one thrown droplet bursting on it at full speed.")]
    [Min(0f)] [SerializeField] private float splashAmount = 0.9f;
    [Tooltip("Shortest time between counted bursts, in seconds.")]
    [Min(0f)] [SerializeField] private float splashInterval = 0.15f;

    private static readonly Dictionary<Collider, ErosionTarget> registry = new Dictionary<Collider, ErosionTarget>();
    private static readonly List<ErosionTarget> targets = new List<ErosionTarget>();
    private readonly List<Collider> registered = new List<Collider>();
    private float budgetStep = float.NegativeInfinity, budgetUsed, nextSplash;

    /// <summary>Number of enabled targets.</summary>
    public static int Count => targets.Count;
    /// <summary>Layer mask covering every registered surface.</summary>
    public static int SurfaceLayers { get; private set; }
    /// <summary>Raised after a target registers (true) or unregisters (false) its surfaces.</summary>
    public static event System.Action<ErosionTarget, bool> SurfacesChanged;
    /// <summary>Erosion actually taken: (target, contact point, amount after sensitivity and cap).</summary>
    public event System.Action<ErosionTarget, Vector3, float> Eroded;
    /// <summary>False stops all water from counting, without unregistering the surfaces.</summary>
    public bool Accepting { get; set; } = true;
    public float TotalReceived { get; private set; }
    public float Sensitivity => sensitivity;
    public float MaximumPerSecond => maximumPerSecond;
    public IReadOnlyList<Collider> Surfaces => registered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { registry.Clear(); targets.Clear(); SurfaceLayers = 0; SurfacesChanged = null; }

    /// <summary>Sets up a target added from code; call before its GameObject is activated, or it re-registers.</summary>
    public void Configure(Collider[] surfaceColliders, float sensitivityValue, float perSecond, float splash, float splashGap)
    {
        surfaces = surfaceColliders ?? new Collider[0];
        sensitivity = Mathf.Max(0f, sensitivityValue);
        maximumPerSecond = Mathf.Max(0f, perSecond);
        splashAmount = Mathf.Max(0f, splash);
        splashInterval = Mathf.Max(0f, splashGap);
        if (isActiveAndEnabled) { OnDisable(); OnEnable(); }
    }

    /// <summary>Whether this collider belongs to an enabled target, accepting or not.</summary>
    public static bool IsSurface(Collider collider) => collider != null && registry.ContainsKey(collider);

    /// <summary>The target that takes erosion through this collider right now.</summary>
    public static bool TryGet(Collider collider, out ErosionTarget target)
    {
        if (collider == null || !registry.TryGetValue(collider, out target) || target == null || !target.isActiveAndEnabled ||
            !target.Accepting || !collider.enabled || !collider.gameObject.activeInHierarchy) { target = null; return false; }
        return true;
    }

    /// <summary>Contact erosion from touching water; all droplets share the per-second cap.</summary>
    internal float Erode(Vector3 point, float amount)
    {
        if (this == null || !Accepting || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return 0f;
        float applied = amount * sensitivity;
        if (maximumPerSecond > 0f)
        {
            if (budgetStep != Time.fixedTime) { budgetStep = Time.fixedTime; budgetUsed = 0f; }
            applied = Mathf.Min(applied, maximumPerSecond * Time.fixedDeltaTime - budgetUsed);
            if (applied <= 0f) return 0f;
            budgetUsed += applied;
        }
        return Deliver(point, applied);
    }

    /// <summary>A thrown droplet bursting on the surface, scaled by impact speed.</summary>
    internal float Splash(Vector3 point, float impactSpeed)
    {
        if (this == null || !Accepting || Time.time < nextSplash) return 0f;
        nextSplash = Time.time + splashInterval;
        return Deliver(point, splashAmount * sensitivity * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(impactSpeed / 12f)));
    }

    private float Deliver(Vector3 point, float applied)
    {
        if (applied <= 0f) return 0f;
        TotalReceived += applied;
        if (Eroded != null)
            foreach (System.Action<ErosionTarget, Vector3, float> listener in Eroded.GetInvocationList())
                try { listener(this, point, applied); }
                catch (System.Exception exception) { Debug.LogException(exception, this); }
        return applied;
    }

    private void OnEnable()
    {
        registered.Clear();
        Collider[] candidates = surfaces != null && surfaces.Length > 0 ? surfaces : GetComponentsInChildren<Collider>(true);
        foreach (Collider surface in candidates)
        {
            if (surface == null || surface.isTrigger || registered.Contains(surface)) continue;
            if (registry.TryGetValue(surface, out ErosionTarget owner) && owner != null && owner != this)
            { Debug.LogWarning($"{surface.name} already belongs to erosion target {owner.name}.", this); continue; }
            registry[surface] = this;
            registered.Add(surface);
        }
        targets.Add(this);
        RecomputeLayers();
        SurfacesChanged?.Invoke(this, true);
    }

    private void OnDisable()
    {
        // Scan by owner: destroyed colliders compare equal to null but still sit in the dictionary.
        var stale = new List<Collider>();
        foreach (KeyValuePair<Collider, ErosionTarget> entry in registry) if (entry.Value == this) stale.Add(entry.Key);
        foreach (Collider key in stale) registry.Remove(key);
        targets.Remove(this);
        RecomputeLayers();
        SurfacesChanged?.Invoke(this, false);
    }

    private static void RecomputeLayers()
    {
        int mask = 0;
        foreach (Collider surface in registry.Keys) if (surface != null) mask |= 1 << surface.gameObject.layer;
        SurfaceLayers = mask;
    }
}
