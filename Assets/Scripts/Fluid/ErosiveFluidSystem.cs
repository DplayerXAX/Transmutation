using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ErosiveFluidSystem : MonoBehaviour
{
    private struct Request { public Vector3 point; public float radius, amount; }
    [SerializeField] private ProceduralWorld world;
    [SerializeField] private Material dropletMaterial;
    [SerializeField] private Material debrisMaterial;
    [SerializeField] private Material rippleMaterial;
    [Range(16, 1024)] [SerializeField] private int maximumDroplets = 256;
    [Min(1f)] [SerializeField] private float maximumSpeed = 12f;
    [Min(0.01f)] [SerializeField] private float sampleInterval = 0.1f;
    [Min(1f)] [SerializeField] private float visualDistance = 60f;
    [SerializeField] private LayerMask erodibleLayers = 1 << 7;
    [SerializeField] private LayerMask collisionLayers = (1 << 0) | (1 << 4) | (1 << 7) | (1 << 8) | (1 << 9);
    private readonly Stack<ErosiveFluidDroplet> pool = new Stack<ErosiveFluidDroplet>();
    private readonly List<ErosiveFluidDroplet> active = new List<ErosiveFluidDroplet>();
    private readonly Dictionary<Vector3Int, Request> requests = new Dictionary<Vector3Int, Request>();
    private ErosiveFluidSource[] sources;
    private PhysicsMaterial friction;
    private ParticleSystem debris;
    private float sampleTimer, particleTokens;
    private int firstSource;
    private float actorRefreshTimer;
    private readonly List<Collider> actors = new List<Collider>();
    private Camera view;
    private readonly List<GameObject> ripples = new List<GameObject>();
    private readonly List<MaterialPropertyBlock> rippleProperties = new List<MaterialPropertyBlock>();
    private readonly float[] rippleAges = new float[32];
    private int nextRipple;
    public ProceduralWorld World => world;
    public float MaximumSpeed => maximumSpeed;
    public float SampleInterval => sampleInterval;
    public int CollisionLayers => collisionLayers.value | (1 << world.TerrainLayer) | (1 << 4);
    public int ActiveCount => active.Count;
    public int Capacity => maximumDroplets;
    public IReadOnlyList<ErosiveFluidDroplet> Droplets => active;
    public int SkippedSpawns { get; private set; }
    public bool CanErode(Collider collider) => world != null && collider != null &&
        (erodibleLayers.value & (1 << collider.gameObject.layer)) != 0 && world.OwnsCollider(collider);
    private static readonly ProfilerMarker SimulationMarker = new ProfilerMarker("Erosion.Droplets");
    public void SetWorld(ProceduralWorld value) => world = value;
    private void OnEnable() => Creature.Activated += ActorActivated;
    private void ActorActivated(Creature actor) => RegisterActor(actor, true);
    private void Start()
    {
        if (world == null) { enabled = false; return; }
        view = Camera.main;
        sources = GetComponentsInChildren<ErosiveFluidSource>();
        foreach (ErosiveFluidSource source in sources) source.SetFluid(this);
        RefreshActors();
        friction = new PhysicsMaterial("Fluid friction") { dynamicFriction = 0f, staticFriction = 0f, bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum };
        for (int i = 0; i < maximumDroplets; i++) pool.Push(CreateDroplet());
        world.TerrainChanged += Invalidate;
        world.ErosionApplied += EmitDebris;
        var particles = new GameObject("Erosion Debris"); particles.transform.SetParent(transform, false);
        debris = particles.AddComponent<ParticleSystem>();
        var main = debris.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        main.maxParticles = 128; main.startLifetime = 0.6f; main.startSize = 0.045f; main.startSpeed = 0.5f;
        main.gravityModifier = 0.1f; main.startColor = new Color(0.7f, 0.85f, 0.85f, 0.8f);
        var emission = debris.emission; emission.enabled = false;
        var shape = debris.shape; shape.enabled = false;
        debris.GetComponent<ParticleSystemRenderer>().sharedMaterial = debrisMaterial;
        debris.Play();
        for (int i = 0; i < rippleAges.Length; i++)
        {
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ring.name = "Pooled Contact Ripple"; ring.transform.SetParent(transform, false);
            Destroy(ring.GetComponent<Collider>());
            var renderer = ring.GetComponent<Renderer>(); renderer.sharedMaterial = rippleMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.SetActive(false); ripples.Add(ring); rippleProperties.Add(new MaterialPropertyBlock());
        }
    }
    private ErosiveFluidDroplet CreateDroplet()
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Pooled Water Droplet"; sphere.layer = 4;
        sphere.transform.SetParent(transform, false);
        sphere.AddComponent<Rigidbody>();
        var droplet = sphere.AddComponent<ErosiveFluidDroplet>();
        droplet.Initialize(this, dropletMaterial, friction); sphere.SetActive(false);
        return droplet;
    }
    // Creature death is permanent and eating destroys the object, so such droplets are replaced.
    private void Return(ErosiveFluidDroplet droplet)
    {
        if (droplet != null && !droplet.IsDead) { droplet.Recycle(); pool.Push(droplet); return; }
        if (droplet != null) Destroy(droplet.gameObject);
        // No new objects while this system is being disabled or torn down.
        if (isActiveAndEnabled) pool.Push(CreateDroplet());
    }
    public bool Spawn(ErosiveFluidSource source, Vector3 point, Vector3 velocity)
    {
        if (pool.Count == 0 || active.Count >= maximumDroplets) { SkippedSpawns++; return false; }
        ErosiveFluidDroplet droplet = pool.Pop(); droplet.Launch(source, point, velocity); active.Add(droplet); return true;
    }
    private void FixedUpdate()
    {
        if (sources == null || !world.LayoutReady) return;
        if (world.LastUpdateError != null) { SetEmitting(false); ClearDroplets(); return; }
        using (SimulationMarker.Auto())
        {
            float dt = Time.fixedDeltaTime;
            actorRefreshTimer -= dt;
            if (actorRefreshTimer <= 0f) { RefreshActors(); actorRefreshTimer = 1f; }
            for (int i = active.Count - 1; i >= 0; i--)
            {
                ErosiveFluidDroplet droplet = active[i];
                if (droplet != null && droplet.Simulate(dt)) continue;
                active.RemoveAt(i); Return(droplet);
            }
            for (int i = 0; i < sources.Length; i++) if (sources[(i + firstSource) % sources.Length] != null) sources[(i + firstSource) % sources.Length].Emit(dt);
            if (sources.Length > 0) firstSource = (firstSource + 1) % sources.Length;
            sampleTimer += dt;
            if (sampleTimer >= sampleInterval)
            {
                foreach (Request request in requests.Values) world.Erode(request.point, request.radius, request.amount);
                requests.Clear(); sampleTimer = 0f;
            }
            particleTokens = Mathf.Min(12f, particleTokens + dt * 12f);
        }
    }
    private void RefreshActors()
    {
        actors.RemoveAll(collider => collider == null);
        foreach (Creature actor in FindObjectsByType<Creature>(FindObjectsSortMode.None)) RegisterActor(actor);
        foreach (SmoothFirstPersonController actor in FindObjectsByType<SmoothFirstPersonController>(FindObjectsSortMode.None)) RegisterActor(actor);
    }
    private void RegisterActor(Component actor, bool refreshExisting = false)
    {
        if (actor is ErosiveFluidDroplet || actor.gameObject.scene != gameObject.scene) return;
        foreach (Collider collider in actor.GetComponentsInChildren<Collider>())
        {
            // Creature triggers (heat, capture) must still sense water; only solid bodies are ignored.
            if (collider.isTrigger && actor is Creature) continue;
            bool known = actors.Contains(collider);
            if (known && !refreshExisting) continue;
            if (!known) actors.Add(collider);
            foreach (ErosiveFluidDroplet droplet in active) if (droplet != null) Physics.IgnoreCollision(droplet.Collider, collider);
        }
    }
    internal void IgnoreActors(SphereCollider sphere)
    {
        foreach (Collider collider in actors) if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
            Physics.IgnoreCollision(sphere, collider);
    }
    internal void Accumulate(Vector3 point, float radius, float amount)
    {
        if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        // A small bin retains contact accuracy; quantization is independent of sampling frequency.
        float bin = Mathf.Max(0.05f, world.CellSize * 0.25f);
        Vector3Int key = Vector3Int.FloorToInt(point / bin);
        if (requests.TryGetValue(key, out Request old))
        { float sum = old.amount + amount; old.point = (old.point * old.amount + point * amount) / sum; old.amount = sum; old.radius = Mathf.Max(old.radius, radius); requests[key] = old; }
        else requests[key] = new Request { point = point, radius = radius, amount = amount };
    }
    internal bool InBounds(Vector3 point)
    {
        if (!world.LayoutReady) return false;
        // The fixed volume contains solid bedrock; recycle escaped droplets instead of endless falling.
        Bounds bounds = world.VolumeBounds;
        return point.x >= bounds.min.x - 1f && point.x <= bounds.max.x + 1f &&
            point.z >= bounds.min.z - 1f && point.z <= bounds.max.z + 1f && point.y > bounds.min.y - 4f;
    }
    private void Invalidate(Bounds bounds) { foreach (ErosiveFluidDroplet droplet in active) if (droplet != null) droplet.Invalidate(bounds); }
    internal void Evaporate(Vector3 point)
    {
        if (debris == null || (view != null && (view.transform.position - point).sqrMagnitude > visualDistance * visualDistance)) return;
        var emit = new ParticleSystem.EmitParams { position = point, velocity = Vector3.up * 0.8f, startColor = new Color(0.92f, 0.96f, 1f, 0.6f) };
        debris.Emit(emit, 6);
    }
    private void EmitDebris(Bounds bounds, float amount)
    {
        if (debris == null || particleTokens < 1f || (view != null && (view.transform.position - bounds.center).sqrMagnitude > visualDistance * visualDistance)) return;
        // Probe along the actual contact normal, so wall/ceiling erosion does not emit on the
        // unrelated surface above a cave. Effects still follow successful terrain publication.
        if (!FindEffectSurface(bounds, out RaycastHit hit)) return;
        int count = Mathf.Min((int)particleTokens, Mathf.Clamp(Mathf.CeilToInt(amount * 3f), 1, 8));
        particleTokens -= count;
        var emit = new ParticleSystem.EmitParams { position = hit.point + hit.normal * 0.03f, velocity = hit.normal * 0.5f };
        debris.Emit(emit, count);
        GameObject ring = ripples[nextRipple];
        ring.transform.SetPositionAndRotation(hit.point + hit.normal * 0.025f, Quaternion.LookRotation(hit.normal));
        ring.transform.localScale = Vector3.one * Mathf.Clamp(bounds.size.x, 0.5f, 2f);
        rippleAges[nextRipple] = 0f; ring.SetActive(true);
        nextRipple = (nextRipple + 1) % ripples.Count;
    }
    private bool FindEffectSurface(Bounds bounds, out RaycastHit surface)
    {
        int mask = 1 << world.TerrainLayer;
        foreach (ErosiveFluidDroplet droplet in active)
        {
            if (droplet == null || !droplet.TryGetEffectContact(out Vector3 point, out Vector3 normal) || !bounds.Contains(point)) continue;
            float reach = Mathf.Max(0.5f, world.CellSize);
            if (Physics.Raycast(point + normal * reach, -normal, out surface, reach * 2f, mask, QueryTriggerInteraction.Ignore)
                && world.OwnsCollider(surface.collider)) return true;
        }
        // Fixed digging has no droplet contact. Search outward from the changed volume for
        // its nearest owned surface instead of assuming it is a horizontal floor.
        surface = default; float nearest = float.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++) for (int sign = -1; sign <= 1; sign += 2)
        {
            Vector3 direction = Vector3.zero; direction[axis] = sign;
            if (Physics.Raycast(bounds.center, direction, out RaycastHit candidate, bounds.extents[axis] + world.CellSize,
                mask, QueryTriggerInteraction.Ignore) && world.OwnsCollider(candidate.collider) && candidate.distance < nearest)
            { surface = candidate; nearest = candidate.distance; }
        }
        return surface.collider != null;
    }
    private void Update()
    {
        for (int i = 0; i < ripples.Count; i++)
        {
            if (!ripples[i].activeSelf) continue;
            rippleAges[i] += Time.deltaTime;
            if (rippleAges[i] >= 1f) { ripples[i].SetActive(false); continue; }
            rippleProperties[i].SetFloat("_Age", rippleAges[i]);
            ripples[i].GetComponent<Renderer>().SetPropertyBlock(rippleProperties[i]);
        }
    }
    public void SetEmitting(bool value) { if (sources != null) foreach (ErosiveFluidSource source in sources) if (source != null) source.SetEmitting(value); }
    public void ClearDroplets()
    { foreach (ErosiveFluidDroplet droplet in active) Return(droplet); active.Clear(); requests.Clear(); sampleTimer = 0f; }
    private void OnDisable() { Creature.Activated -= ActorActivated; ClearDroplets(); }
    private void OnDestroy()
    { if (world != null) { world.TerrainChanged -= Invalidate; world.ErosionApplied -= EmitDebris; } if (friction != null) Destroy(friction); }
}
