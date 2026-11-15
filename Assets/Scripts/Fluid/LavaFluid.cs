using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Lava: a pooled particle fluid of LavaBlob spheres, one per scene (made on first use by whoever spits lava,
/// such as the Fire Snail). Each step it
///   - moves the blobs and lets them cool (LavaBlob);
///   - holds runny blobs together: neighbours pull towards each other and share their velocity, so lava runs
///     as one sticky stream and pools instead of scattering like gravel;
///   - burns what it touches: creatures take damage, except fire creatures, which take heat from it instead
///     (a fire flower in a lava flow blooms). Hotter lava burns harder; set crust no longer burns.
/// </summary>
[DisallowMultipleComponent]
public sealed class LavaFluid : MonoBehaviour
{
    [Header("Blobs")]
    [Min(8)] [SerializeField] private int maximumBlobs = 260;
    [Min(0.05f)] [SerializeField] private float blobRadius = 0.17f;
    [Tooltip("Seconds for a blob to cool from molten to crust.")]
    [Min(0.5f)] [SerializeField] private float coolTime = 9f;
    [Tooltip("Seconds a blob exists in all; once set it stays on the surface until then and crumbles away.")]
    [Min(1f)] [SerializeField] private float lifetime = 26f;
    [Tooltip("Below this heat a resting blob sets into crust.")]
    [Range(0f, 1f)] [SerializeField] private float setBelow = 0.18f;
    [Tooltip("Drag when molten and when nearly set: hot lava runs, cooling lava crawls.")]
    [SerializeField] private Vector2 viscosity = new Vector2(0.6f, 4.5f);
    [SerializeField] private Material lavaMaterial;

    [Header("Cohesion")]
    [Tooltip("Runny blobs closer than this pull together and share their motion.")]
    [Min(0.1f)] [SerializeField] private float cohesionRange = 0.55f;
    [Min(0f)] [SerializeField] private float cohesion = 9f;
    [Range(0f, 1f)] [SerializeField] private float velocitySharing = 0.12f;
    [Tooltip("How much harder crowded blobs push apart than stretched ones pull together.")]
    [Min(0f)] [SerializeField] private float pressure = 10f;

    [Header("Burning")]
    [Tooltip("Damage per second to a creature touching molten lava (less as it cools).")]
    [Min(0f)] [SerializeField] private float damagePerSecond = 30f;
    [Tooltip("Heat per second a fire creature takes from molten lava touching it.")]
    [Min(0f)] [SerializeField] private float heatPerSecond = 20f;
    [Min(0.05f)] [SerializeField] private float burnInterval = 0.2f;

    private readonly List<LavaBlob> active = new List<LavaBlob>();
    private readonly Stack<LavaBlob> pool = new Stack<LavaBlob>();
    private readonly Dictionary<Vector3Int, List<int>> grid = new Dictionary<Vector3Int, List<int>>();
    private readonly Stack<List<int>> cellPool = new Stack<List<int>>();
    private readonly Dictionary<Creature, float> burning = new Dictionary<Creature, float>();
    private readonly Collider[] overlaps = new Collider[16];
    private PhysicsMaterial friction;
    private Mesh blobMesh;
    private float burnTimer;

    public const int LavaLayer = 2; // Ignore Raycast: out of the way of aiming, pickup and ground probes
    // Ground and walls only. Blobs do not collide with each other (they would stack like marbles); Cohere keeps
    // them apart and together instead, the way a fluid's pressure does, so lava spreads into a flat pool.
    public LayerMask CollisionLayers => (1 << 7) | (1 << 8);
    public float BlobRadius => blobRadius;
    public float CoolTime => coolTime;
    public float Lifetime => lifetime;
    public float SetBelow => setBelow;
    public Vector2 Viscosity => viscosity;
    public IReadOnlyList<LavaBlob> Blobs => active;

    /// <summary>The lava of this scene, made if it has none yet.</summary>
    public static LavaFluid For(Scene scene, Material material)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.TryGetComponent(out LavaFluid found)) { if (found.lavaMaterial == null) found.lavaMaterial = material; return found; }
        var go = new GameObject("Lava Fluid");
        SceneManager.MoveGameObjectToScene(go, scene);
        var fluid = go.AddComponent<LavaFluid>();
        fluid.lavaMaterial = material;
        return fluid;
    }

    /// <summary>Sends one blob out. False when the pool is used up.</summary>
    public bool Spawn(Vector3 position, Vector3 velocity, float heat = 1f)
    {
        LavaBlob blob = Take();
        if (blob == null) return false;
        blob.Launch(position, velocity, heat);
        active.Add(blob);
        return true;
    }

    /// <summary>
    /// Drinks the hot blobs within radius of a point (a fire snail feeding), up to the given count.
    /// Returns the sum of their heat (each 0..1).
    /// </summary>
    public float Absorb(Vector3 point, float radius, int maxCount, float minimumHeat = 0.2f)
    {
        float total = 0f;
        float reach = radius * radius;
        for (int i = active.Count - 1; i >= 0 && maxCount > 0; i--)
        {
            LavaBlob blob = active[i];
            if (blob.Temperature < minimumHeat || (blob.Position - point).sqrMagnitude > reach) continue;
            total += blob.Absorb();
            active.RemoveAt(i);
            pool.Push(blob);
            maxCount--;
        }
        return total;
    }

    /// <summary>The nearest blob still hot enough to be worth drinking.</summary>
    public bool NearestHot(Vector3 point, float range, out Vector3 at, float minimumHeat = 0.35f)
    {
        at = point;
        float best = range * range;
        bool found = false;
        foreach (LavaBlob blob in active)
        {
            if (blob.Temperature < minimumHeat) continue;
            float distance = (blob.Position - point).sqrMagnitude;
            if (distance < best) { best = distance; at = blob.Position; found = true; }
        }
        return found;
    }

    private LavaBlob Take()
    {
        while (pool.Count > 0) { LavaBlob pooled = pool.Pop(); if (pooled != null) return pooled; }
        if (active.Count + pool.Count >= maximumBlobs)
        {
            // Full: the oldest blob (crumbling crust, usually) makes way for the new one.
            if (active.Count == 0) return null;
            LavaBlob oldest = active[0];
            active.RemoveAt(0);
            oldest.Recycle();
            return oldest;
        }
        EnsureShared();
        var go = new GameObject("Lava Blob");
        go.layer = LavaLayer;
        go.transform.SetParent(transform, false);
        go.SetActive(false);
        go.AddComponent<Rigidbody>();
        go.AddComponent<SphereCollider>();
        var blob = go.AddComponent<LavaBlob>();
        blob.Initialize(this, blobMesh, lavaMaterial, friction);
        return blob;
    }

    private void EnsureShared()
    {
        if (friction == null)
            friction = new PhysicsMaterial("Lava") { dynamicFriction = 0.2f, staticFriction = 0.3f, bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum };
        if (blobMesh == null)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            blobMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
            Destroy(sphere);
        }
        if (lavaMaterial == null)
        {
            Shader shader = Shader.Find("Capstone/Fluid/Lava");
            lavaMaterial = shader != null ? new Material(shader) { name = "Lava (runtime)" }
                : OreColossus.MakeMaterial("Lava (runtime)", new Color(1f, 0.3f, 0.05f), new Color(2f, 0.5f, 0.05f));
        }
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            LavaBlob blob = active[i];
            if (blob != null && blob.Simulate(dt)) continue;
            if (blob != null) { blob.Recycle(); pool.Push(blob); }
            active.RemoveAt(i);
        }
        Cohere(dt);
        burnTimer -= dt;
        if (burnTimer <= 0f) { burnTimer = burnInterval; Burn(burnInterval); }
    }

    // ---------------- Cohesion ----------------

    private Vector3Int Cell(Vector3 position) => Vector3Int.FloorToInt(position / cohesionRange);

    private void Cohere(float dt)
    {
        foreach (List<int> cell in grid.Values) { cell.Clear(); cellPool.Push(cell); }
        grid.Clear();
        for (int i = 0; i < active.Count; i++)
        {
            if (!active[i].Fluid) continue;
            Vector3Int key = Cell(active[i].Position);
            if (!grid.TryGetValue(key, out List<int> cell)) grid[key] = cell = cellPool.Count > 0 ? cellPool.Pop() : new List<int>(8);
            cell.Add(i);
        }
        float range = cohesionRange * cohesionRange;
        float rest = blobRadius * 2.1f;
        foreach (KeyValuePair<Vector3Int, List<int>> entry in grid)
        foreach (int i in entry.Value)
        {
            LavaBlob a = active[i];
            Vector3 pull = Vector3.zero, shared = Vector3.zero;
            int neighbours = 0;
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                if (!grid.TryGetValue(entry.Key + new Vector3Int(x, y, z), out List<int> near)) continue;
                foreach (int j in near)
                {
                    if (j == i) continue;
                    Vector3 to = active[j].Position - a.Position;
                    float distance = to.sqrMagnitude;
                    if (distance > range || distance < 1e-6f) continue;
                    distance = Mathf.Sqrt(distance);
                    // Towards a resting spacing: crowded blobs push apart (pressure: the pool spreads and
                    // flattens), stretched ones pull together (it holds as one sticky flow).
                    if (distance > rest) pull += to / distance * (distance - rest);
                    else pull += to / distance * (distance - rest) * pressure;
                    shared += active[j].Body.linearVelocity;
                    neighbours++;
                }
            }
            if (neighbours == 0) continue;
            Rigidbody body = a.Body;
            float hot = Mathf.Clamp01(a.Temperature * 1.5f);
            // Pressure keeps working while it is runny at all; the pull is the part that fades with cooling.
            body.AddForce(pull * cohesion * Mathf.Max(hot, 0.35f), ForceMode.Acceleration);
            Vector3 average = shared / neighbours;
            body.linearVelocity = Vector3.Lerp(body.linearVelocity, average, velocitySharing * hot);
        }
    }

    // ---------------- Burning ----------------

    private void Burn(float seconds)
    {
        burning.Clear();
        int mask = ~((1 << LavaLayer) | (1 << 7));
        foreach (LavaBlob blob in active)
        {
            if (blob.Temperature <= setBelow) continue;
            int count = Physics.OverlapSphereNonAlloc(blob.Position, blobRadius + 0.2f, overlaps, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Creature creature = overlaps[i].GetComponentInParent<Creature>();
                if (creature == null || creature.IsDead) continue;
                // A creature in a pool is burnt by the hottest lava touching it, not once per blob.
                burning.TryGetValue(creature, out float hottest);
                burning[creature] = Mathf.Max(hottest, blob.Temperature);
            }
        }
        foreach (KeyValuePair<Creature, float> entry in burning)
        {
            Creature creature = entry.Key;
            if (creature == null) continue;
            float strength = Mathf.InverseLerp(setBelow, 1f, entry.Value) * seconds;
            if (IsFireCreature(creature)) creature.AddHeat(heatPerSecond * strength);
            else creature.ApplyDamage(damagePerSecond * strength);
        }
    }

    /// <summary>Creatures of the heat loop live on heat: lava feeds them instead of burning them.</summary>
    public static bool IsFireCreature(Creature creature) =>
        creature is FireFlower || creature is FireBubble || creature is FireSource || creature is FireEater || creature is FireSnail;

    private void OnDestroy()
    {
        if (friction != null) Destroy(friction);
    }
}
