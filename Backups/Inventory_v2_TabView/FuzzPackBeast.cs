using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A grown-up fuzz that carries creatures in its fur. Goes on the same object as a FuzzCreature with Grown ticked.
///
/// - Its spots are used from the player's inventory view (BodyMounts, Tab): when the beast is near, it is in the
///   picture and each spot in its fur can be clicked like a body part.
/// - What it carries stays alive and shares heat with it: a warm beast keeps cold creatures warm, a fire on its
///   back keeps the beast warm until the fire is spent.
/// - It follows only while warm (FuzzCreature.FollowHeat). Cold, its fur goes black and it goes to the nearest
///   fire by itself to warm up, then comes back to the player (see FuzzCreature, Grown). Petting warms it a little.
/// </summary>
[RequireComponent(typeof(FuzzCreature))]
[DefaultExecutionOrder(1020)] // After BodyMounts.
public sealed class FuzzPackBeast : MonoBehaviour
{
    private static readonly List<FuzzPackBeast> All = new List<FuzzPackBeast>();

    [Header("Pack")]
    [Tooltip("Where things sit in its fur, as directions from its middle (it faces +z). Their count is how many it carries.")]
    [SerializeField] private Vector3[] packSpots =
    {
        new Vector3(0f, 1f, -0.25f),
        new Vector3(-0.75f, 0.7f, -0.1f),
        new Vector3(0.75f, 0.7f, -0.1f),
        new Vector3(0f, 0.75f, -0.75f),
        new Vector3(0f, 1f, 0.45f),
    };
    [Tooltip("Largest size a creature is shrunk to on its back (metres).")]
    [Min(0.05f)] [SerializeField] private float itemSize = 0.75f;
    [Tooltip("How deep things sink into the fur (0 = resting on top, 1 = half buried).")]
    [Range(0f, 1f)] [SerializeField] private float sinkIn = 0.45f;
    [Tooltip("How quickly the load swings round after the beast turns (lower = more delay).")]
    [Min(0.5f)] [SerializeField] private float swingRate = 4f;
    [Tooltip("How quickly the load catches up with the beast's hops (lower = more bounce).")]
    [Min(0.5f)] [SerializeField] private float bounceRate = 9f;

    [Header("Heat")]
    [Tooltip("Heat it starts with, so it follows from the start.")]
    [Min(0f)] [SerializeField] private float startHeat = 14f;
    [Tooltip("Share of the heat difference that flows between it and each thing it carries, per second.")]
    [Range(0f, 2f)] [SerializeField] private float heatShare = 0.3f;
    [Tooltip("Most heat per second that flows between it and one thing it carries.")]
    [Min(0f)] [SerializeField] private float maxHeatFlow = 4f;
    [Tooltip("Heat per second the player's hand gives it while petting.")]
    [Min(0f)] [SerializeField] private float petWarmthPerSecond = 0.8f;
    [Tooltip("Petting warms it only up to this many times its Follow Heat: enough to wake it, fire is needed for more.")]
    [Min(0f)] [SerializeField] private float petWarmthLimit = 2f;

    private readonly List<StowedCreature> pack = new List<StowedCreature>();
    private FuzzCreature fuzz;
    private PlayerCreatureCarrier carrier;

    public int Count => pack.Count;
    public int SpotCount => packSpots.Length;
    public FuzzCreature Fuzz => fuzz;
    public float SpotRadius => itemSize * 0.55f;

    private void Awake() => fuzz = GetComponent<FuzzCreature>();
    private void OnEnable() => All.Add(this);

    private void Start()
    {
        carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        if (fuzz != null && fuzz.Heat < startHeat) fuzz.SetHeat(startHeat);
    }

    private void OnDisable()
    {
        All.Remove(this);
        if (ReflectionPhysics.SwitchingWorldRoots) return;
        UnloadAll();
    }

    /// <summary>The nearest living pack beast within range of a point, or null.</summary>
    public static FuzzPackBeast Nearest(Vector3 point, float range)
    {
        FuzzPackBeast best = null;
        float bestDistance = range;
        foreach (FuzzPackBeast beast in All)
        {
            if (beast == null || beast.fuzz == null || beast.fuzz.IsDead) continue;
            float d = Vector3.Distance(point, beast.transform.position);
            if (d < bestDistance) { bestDistance = d; best = beast; }
        }
        return best;
    }

    public bool Accepts(Creature creature) => fuzz != null && !fuzz.IsDead && creature != null &&
                                              !(creature is FuzzCreature f && f.Grown) && !(creature is TentacleCreature);

    public StowedCreature At(int spot)
    {
        foreach (StowedCreature s in pack) if (s.Slot == spot) return s;
        return null;
    }

    /// <summary>Takes what is in a spot off its back (still owned by the player: the caller places it elsewhere).</summary>
    public StowedCreature Take(int spot)
    {
        StowedCreature s = At(spot);
        if (s != null) pack.Remove(s);
        return s;
    }

    /// <summary>Puts a stowed creature into a free spot (moving it there from wherever it is).</summary>
    public void Put(StowedCreature item, int spot)
    {
        if (item == null || spot < 0 || spot >= packSpots.Length || At(spot) != null) return;
        item.MoveTo(spot, itemSize, transform.rotation);
        pack.Add(item);
    }

    /// <summary>Loads a free creature straight onto the first free spot (tests and other scripts).</summary>
    public bool Load(Creature creature)
    {
        if (carrier == null) carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        if (carrier == null || !Accepts(creature) || !StowedCreature.CanStow(creature)) return false;
        for (int i = 0; i < packSpots.Length; i++)
            if (At(i) == null) { pack.Add(new StowedCreature(creature, carrier, i, itemSize, transform.rotation)); return true; }
        return false;
    }

    /// <summary>Where the middle of a spot is now (what sits there, or the empty place in the fur).</summary>
    public Vector3 SpotCentre(int spot)
    {
        StowedCreature s = At(spot);
        if (s != null) return s.Centre;
        return transform.position + transform.rotation * SpotOffset(spot, SpotRadius * 0.6f);
    }

    private Vector3 SpotOffset(int spot, float itemRadius)
    {
        Vector3 dir = packSpots[Mathf.Clamp(spot, 0, packSpots.Length - 1)].normalized;
        float r = fuzz != null ? fuzz.Radius : 1f;
        float squash = fuzz != null ? fuzz.Squash : 1f;
        // On the fur surface (flattened like the body), pushed out by its own size, less how deep it sinks in.
        Vector3 surface = new Vector3(dir.x * r, dir.y * r * squash, dir.z * r) * 1.1f;
        return surface + dir * (itemRadius * (1f - sinkIn));
    }

    private void UnloadAll()
    {
        foreach (StowedCreature s in pack) s.Restore();
        pack.Clear();
    }

    private void Update()
    {
        // Something that died or was taken by something else falls off.
        for (int i = pack.Count - 1; i >= 0; i--)
        {
            if (pack[i].Valid) continue;
            pack[i].Restore();
            pack.RemoveAt(i);
        }
        if (fuzz == null || !fuzz.SimulationEnabled) return;
        if (fuzz.IsDead) { UnloadAll(); return; }
        float dt = Time.deltaTime;

        // Heat evens out between the beast and everything in its fur.
        float k = 1f - Mathf.Exp(-heatShare * dt);
        foreach (StowedCreature s in pack) HeatFlow.Share(fuzz, s.Creature, k, maxHeatFlow * dt);

        // The player's hand is a little warm: enough to wake it, not to fill it.
        if (fuzz.IsPetted && fuzz.Heat < fuzz.FollowHeat * petWarmthLimit)
            fuzz.AddHeat(petWarmthPerSecond * dt);
    }

    // After the beast has moved this frame.
    private void LateUpdate()
    {
        if (pack.Count == 0 || fuzz == null) return;
        float dt = Time.deltaTime;
        foreach (StowedCreature s in pack)
        {
            if (s.Creature == null) continue;
            Vector3 dir = packSpots[Mathf.Clamp(s.Slot, 0, packSpots.Length - 1)].normalized;
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, dir, 0.5f));
            s.Ride(transform.position, transform.rotation, SpotOffset(s.Slot, s.Radius), tilt, swingRate, bounceRate, dt);
            s.SetHidden(false);
        }
    }
}
