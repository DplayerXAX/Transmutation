using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dresses the land between the Hanging Spire and the Bone Kiln (the Human Realm):
/// a pale glowing stream between low wet banks, winding from the spire's foot to the bones; glowing lamp
/// stalks along both banks (the web that binds them to each other and to the spire is SpireOvergrowth);
/// root shelters you can climb; crystals floating over the path and round the spire;
/// fossils pressed into the ground near the bones; dark anemones with glowing tips along the water; and
/// sticky fluorescent slime lying on the ground, wrapped round lamp feet and dripping from the lamps.
/// Every family has its own shader (Shaders/Realm), so none of them reads like the ground or the decor.
/// Put it on any object in the scene; it builds itself once the world, the spire and the kiln exist.
/// </summary>
[DisallowMultipleComponent]
public sealed class RealmDressing : MonoBehaviour
{
    [Header("Materials")]
    [SerializeField] private Material lampMaterial;
    [SerializeField] private Material rootMaterial;
    [SerializeField] private Material anemoneMaterial;
    [SerializeField] private Material crystalMaterial;
    [SerializeField] private Material filmMaterial;
    [SerializeField] private Material threadMaterial;
    [SerializeField] private Material fossilMaterial;
    [SerializeField] private Material streamMaterial;
    [SerializeField] private Material slimeMaterial;
    [SerializeField] private Material mudMaterial;

    [Header("Layout")]
    [SerializeField] private int seed = 5;
    [Min(2)] [SerializeField] private int lampsPerSide = 4;
    [Range(0, 6)] [SerializeField] private int shelters = 1;
    [Range(0, 40)] [SerializeField] private int floatingCrystals = 4;
    [Range(0, 10)] [SerializeField] private int fossils = 2;
    [Range(0, 16)] [SerializeField] private int anemoneClusters = 2;
    [Range(0, 30)] [SerializeField] private int slimePatches = 3;
    [Tooltip("How far the stream wanders to either side of the straight line (metres).")]
    [Min(0f)] [SerializeField] private float meander = 7f;
    [Tooltip("Lamps light the ground round them (no shadows).")]
    [SerializeField] private bool lampLights = true;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool built;
    [SerializeField] private int pieces;

    private ProceduralWorld world;
    private Transform root;
    private readonly List<GameObject> streamPieces = new List<GameObject>();
    private bool streamDirty;
    private float streamRefreshAt;
    private float nextStreamRefresh;
    private Coroutine streamBuild;
    private readonly List<GameObject> stagedStreamPieces = new List<GameObject>();
    public bool StreamBuilding => streamBuild != null;
    private readonly List<Bounds> boneFootprints = new List<Bounds>();
    private readonly RaycastHit[] groundHits = new RaycastHit[128];
    private ProceduralWorld subscribedWorld;
    private Vector3 start, end;
    private RealmDecorLayout.Snapshot layout;

    private Vector3 ThemePoint(Vector3 centre, System.Random random, float min = 15f, float max = 25f)
    {
        float angle = (float)random.NextDouble() * Mathf.PI * 2f;
        return centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Mathf.Lerp(min, max, (float)random.NextDouble());
    }
    private float waited;
    private readonly List<Vector3> lampBases = new List<Vector3>();
    private readonly List<Vector3> fossilSpots = new List<Vector3>();
    private readonly List<Vector3> shelterSpots = new List<Vector3>();
    private readonly List<(Transform transform, Vector3 home, float phase, Vector3 spin)> floaters = new List<(Transform, Vector3, float, Vector3)>();

    /// <summary>True once everything has been placed.</summary>
    public bool Built => built && root != null;

    /// <summary>Centres of the lamp orbs (for the web round the spire).</summary>
    public IReadOnlyList<Vector3> LampTops => lampTops;
    private readonly List<Vector3> lampTops = new List<Vector3>();

    private void OnEnable() => ProceduralWorld.AnyTerrainChanged += TerrainChanged;
    private void OnDisable()
    {
        if (streamBuild != null) StopCoroutine(streamBuild);
        streamBuild = null;
        foreach (GameObject piece in stagedStreamPieces) DestroyStreamPiece(piece);
        pieces -= stagedStreamPieces.Count;
        stagedStreamPieces.Clear();
        ProceduralWorld.AnyTerrainChanged -= TerrainChanged;
        if (subscribedWorld != null) subscribedWorld.ChunkCreated -= ChunkReady;
        subscribedWorld = null;
    }
    private void ChunkReady(GameObject chunk, Mesh mesh)
    {
        if (mesh != null && chunk.TryGetComponent<Renderer>(out var renderer)) TerrainChanged(world, renderer.bounds);
    }
    private void TerrainChanged(ProceduralWorld changed, Bounds bounds)
    {
        if (changed != world || !built) return;
        Bounds path = new Bounds((start + end) * 0.5f, Vector3.zero);
        path.Encapsulate(start);
        path.Encapsulate(end);
        path.Expand(new Vector3(meander * 2f + 16f, 200f, meander * 2f + 16f));
        if (!path.Intersects(bounds)) return;
        bool touches = streamPieces.Count == 0;
        foreach (GameObject piece in streamPieces)
            if (piece != null && piece.GetComponent<Renderer>().bounds.Intersects(bounds))
            {
                touches = true;
            }
        if (!touches)
            for (int i = 0; i <= 20; i++)
            {
                Vector3 point = PathPoint(i / 20f);
                if (point.x < bounds.min.x - 3f || point.x > bounds.max.x + 3f
                    || point.z < bounds.min.z - 3f || point.z > bounds.max.z + 3f) continue;
                float surface = RealmSites.Surface(world, point);
                if (surface >= bounds.min.y - 0.5f && surface <= bounds.max.y + 0.5f) { touches = true; break; }
            }
        if (!touches) return; // Cave edits and unrelated chunks must not rebuild the surface river.
        if (!streamDirty) streamRefreshAt = Time.time + 0.4f;
        streamDirty = true;
    }

    public void Setup(Material lamp, Material rootWood, Material anemone, Material crystal, Material film, Material thread, Material fossil, Material stream,
        Material slime, Material mud)
    {
        slimeMaterial = slime;
        mudMaterial = mud;
        lampMaterial = lamp;
        rootMaterial = rootWood;
        anemoneMaterial = anemone;
        crystalMaterial = crystal;
        filmMaterial = film;
        threadMaterial = thread;
        fossilMaterial = fossil;
        streamMaterial = stream;
    }

    private void Update()
    {
        if (!built)
        {
            if (world == null) world = FindFirstObjectByType<ProceduralWorld>();
            waited += Time.deltaTime;
            if (!RealmSites.ReadyToPlace(world, waited)) return;
            if (!PlanPath()) { built = true; return; }
            // Wait for the chunks along the path so everything sits on the real ground.
            if (!PathLoaded()) return;
            Build();
            return;
        }
        if (world != null && subscribedWorld != world)
        {
            subscribedWorld = world;
            subscribedWorld.ChunkCreated += ChunkReady;
        }
        if (streamDirty && streamBuild == null && Time.time >= Mathf.Max(streamRefreshAt, nextStreamRefresh))
        {
            Stream(new System.Random(seed));
        }
        // Floating crystals bob and turn slowly.
        float time = Time.time;
        foreach (var f in floaters)
        {
            if (f.transform == null) continue;
            f.transform.position = f.home + Vector3.up * Mathf.Sin(time * 0.5f + f.phase) * 0.6f;
            f.transform.Rotate(f.spin * Time.deltaTime, Space.Self);
        }
    }

    // ---------------- Path ----------------

    private bool PlanPath()
    {
        HangingSpire spire = FindFirstObjectByType<HangingSpire>();
        BoneKiln kiln = FindFirstObjectByType<BoneKiln>();
        Vector3 spireFoot = RealmSites.AreaCentre(world);
        Vector3 kilnPoint = kiln != null && kiln.Root != null ? kiln.Root.position : world.transform.position;
        if (RealmSites.Flat(kilnPoint - spireFoot).magnitude < 20f) kilnPoint = spireFoot + Vector3.forward * 80f;

        // Both landmarks stand over sinkholes: start and end just outside their rims.
        Vector3 direction = RealmSites.Flat(kilnPoint - spireFoot).normalized;
        start = spireFoot + direction * (HoleRadiusAt(spireFoot) + 7f);
        end = kilnPoint - direction * (HoleRadiusAt(kilnPoint) + 7f);
        if (RealmSites.Flat(end - start).magnitude < 15f) end = start + direction * 40f;
        return spire != null || kiln != null;
    }

    private float HoleRadiusAt(Vector3 point)
    {
        float best = 0f, bestDistance = float.MaxValue;
        for (int i = 0; i < world.SinkholeCount; i++)
        {
            Vector3 hole = world.SinkholeCentre(i, point.y, out float radius);
            float d = RealmSites.Flat(hole - point).magnitude;
            if (d < bestDistance) { bestDistance = d; best = d < radius + 12f ? radius : 0f; }
        }
        return best;
    }

    /// <summary>Point on the winding path, t = 0 at the spire .. 1 at the bones (height not set).</summary>
    private Vector3 PathPoint(float t)
    {
        Vector3 along = RealmSites.Flat(end - start);
        Vector3 side = Vector3.Cross(Vector3.up, along.normalized);
        float wander = (Mathf.Sin(t * Mathf.PI * 2.3f + seed) * 0.7f + Mathf.Sin(t * Mathf.PI * 5.1f + seed * 1.7f) * 0.3f) * meander;
        wander *= Mathf.Sin(t * Mathf.PI); // pinned at both ends
        return start + along * t + side * wander;
    }

    private Vector3 PathSide(float t)
    {
        Vector3 forward = RealmSites.Flat(PathPoint(Mathf.Min(1f, t + 0.01f)) - PathPoint(Mathf.Max(0f, t - 0.01f))).normalized;
        return Vector3.Cross(Vector3.up, forward);
    }

    private bool PathLoaded()
    {
        for (float t = 0f; t <= 1f; t += 0.1f)
        {
            Vector3 p = PathPoint(t);
            if (InSinkhole(p, 3f)) continue;
            p.y = RealmSites.Surface(world, p);
            if (!Ground(p, out _, out _)) return false;
        }
        return true;
    }

    /// <summary>The real ground (terrain chunks only) under a point, with its normal.</summary>
    private bool Ground(Vector3 point, out Vector3 hit, out Vector3 normal)
    {
        float surface = RealmSites.Surface(world, point);
        Vector3 from = new Vector3(point.x, surface + 40f, point.z);
        int count = Physics.RaycastNonAlloc(from, Vector3.down, groundHits, 90f, 1 << world.TerrainLayer, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        hit = new Vector3(point.x, surface, point.z);
        normal = Vector3.up;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = groundHits[i];
            if (!world.OwnsCollider(h.collider) || h.point.y <= best) continue;
            // Prefer the top surface near the expected height, not a cave floor far below.
            if (h.point.y < surface - 15f) continue;
            best = h.point.y;
            hit = h.point;
            normal = h.normal;
            found = true;
        }
        return found;
    }

    private bool InSinkhole(Vector3 point, float margin)
    {
        foreach (Bounds bone in boneFootprints)
            if (point.x >= bone.min.x - margin && point.x <= bone.max.x + margin
                && point.z >= bone.min.z - margin && point.z <= bone.max.z + margin) return true;
        for (int i = 0; i < world.SinkholeCount; i++)
        {
            Vector3 hole = world.SinkholeCentre(i, point.y, out float radius);
            if (RealmSites.Flat(hole - point).magnitude < radius + margin) return true;
        }
        return false;
    }

    // ---------------- Building ----------------

    private void Build()
    {
        built = true;
        layout = RealmDecorLayout.Capture(world);
        var random = new System.Random(seed);
        root = new GameObject("Realm Dressing").transform;
        root.position = start;
        BoneKiln kiln = FindFirstObjectByType<BoneKiln>();
        if (kiln != null && kiln.Root != null)
            foreach (Renderer renderer in kiln.Root.GetComponentsInChildren<Renderer>())
                if (renderer.GetComponentInParent<BoneChild>() == null && renderer.GetComponentInParent<Creature>() == null)
                    boneFootprints.Add(renderer.bounds);

        Stream(random);
        lampTops.AddRange(Lamps(random));
        // The links between lamps are now part of the spire's web (SpireOvergrowth), not separate cables.
        for (int i = 0; i < shelters; i++) Shelter(Mathf.Lerp(0.3f, 0.7f, shelters == 1 ? 0.5f : i / (float)(shelters - 1)), i % 2 == 0 ? 1f : -1f, random);
        Crystals(random);
        for (int i = 0; i < fossils; i++) Fossil(Mathf.Lerp(0.62f, 0.95f, (i + (float)random.NextDouble() * 0.5f) / Mathf.Max(1, fossils)), random.NextDouble() < 0.5 ? 1f : -1f, i, random);
        Slime(random);
        for (int i = 0; i < anemoneClusters; i++) Anemones(Mathf.Lerp(0.25f, 0.95f, (i + (float)random.NextDouble() * 0.6f) / Mathf.Max(1, anemoneClusters)), random.NextDouble() < 0.5 ? 1f : -1f, random);
    }

    private GameObject Part(string name, Mesh mesh, Material material, int layer, bool collider)
    {
        pieces++;
        GameObject part = RealmSites.Part(name, root, mesh, material, layer, collider);
        part.transform.position = Vector3.zero; // meshes are built in world space
        return part;
    }

    /// <summary>The stream: a dark ribbon laid on the ground, broken where it would cross a sinkhole.</summary>
    private void Stream(System.Random random)
    {
        if (Application.isPlaying)
        {
            if (streamBuild != null) { streamDirty = true; return; }
            streamDirty = false;
            nextStreamRefresh = Time.time + 2f;
            streamBuild = StartCoroutine(BuildStream(new System.Random(seed), true));
        }
        else
        {
            IEnumerator routine = BuildStream(random, false);
            while (routine.MoveNext()) { }
        }
    }

    private static void DestroyStreamPiece(GameObject piece)
    {
        if (piece == null) return;
        Mesh mesh = piece.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying) { Destroy(mesh); Destroy(piece); }
        else { DestroyImmediate(mesh); DestroyImmediate(piece); }
    }

    private IEnumerator BuildStream(System.Random random, bool incremental)
    {
        // Project the footprint onto terrain faces themselves: a height grid bridged sharp ridges,
        // while its neighbourhood maximum made the water visibly hover above slopes.
        var surface = new RealmStreamSurface(start);
        Bounds corridor = new Bounds(start, Vector3.zero);
        for (int i = 0; i <= 40; i++) corridor.Encapsulate(PathPoint(i / 40f));
        corridor.Expand(new Vector3(6f, 200f, 6f));
        long sliceStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        int work = 0;
        bool SliceExpired() => ++work >= 128 ||
            (System.Diagnostics.Stopwatch.GetTimestamp() - sliceStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency >= 1.5;
        void ResetSlice() { work = 0; sliceStarted = System.Diagnostics.Stopwatch.GetTimestamp(); }
        if (incremental) { yield return null; ResetSlice(); }
        foreach (ProceduralWorldChunk chunk in FindObjectsByType<ProceduralWorldChunk>(FindObjectsSortMode.None))
        {
            if (chunk.Owner != world || !chunk.TryGetComponent(out MeshCollider collider)
                || !collider.enabled || !collider.bounds.Intersects(corridor) || collider.sharedMesh == null) continue;
            // Snapshot one chunk at a time; later erosion may replace or destroy its mesh.
            Mesh terrain = collider.sharedMesh;
            Vector3[] vertices = terrain.vertices, normals = terrain.normals;
            int[] triangles = terrain.triangles;
            Matrix4x4 matrix = chunk.transform.localToWorldMatrix;
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                if (incremental && SliceExpired()) { yield return null; ResetSlice(); }
                int ia = triangles[i], ib = triangles[i + 1], ic = triangles[i + 2];
                Vector3 a = matrix.MultiplyPoint3x4(vertices[ia]), b = matrix.MultiplyPoint3x4(vertices[ib]), c = matrix.MultiplyPoint3x4(vertices[ic]);
                Vector3 faceNormal = Vector3.Cross(b - a, c - a).normalized;
                if (faceNormal.y < 0.2f) continue;
                Bounds faceBounds = new Bounds(a, Vector3.zero); faceBounds.Encapsulate(b); faceBounds.Encapsulate(c);
                if (!corridor.Intersects(faceBounds)) continue;
                // Upward cave floors can share the same XZ coordinates. Only stamp the top exposed surface.
                Vector3 centre = (a + b + c) / 3f;
                if (!Ground(centre, out Vector3 top, out _) || Mathf.Abs(top.y - centre.y) > 0.06f) continue;
                bool smooth = normals.Length == vertices.Length;
                surface.Add(a, b, c, smooth ? normalMatrix.MultiplyVector(normals[ia]).normalized : faceNormal,
                    smooth ? normalMatrix.MultiplyVector(normals[ib]).normalized : faceNormal,
                    smooth ? normalMatrix.MultiplyVector(normals[ic]).normalized : faceNormal);
            }
            if (incremental) { yield return null; ResetSlice(); }
        }
        int steps = Mathf.Max(2, Mathf.CeilToInt(RealmSites.Flat(end - start).magnitude * 1.15f / 0.5f));
        var mesh = new RealmMeshes();
        int part = 0, sectionSteps = 0;
        float distance = 0f, previousDistance = 0f;
        Vector3 previous = PathPoint(0f), left = Vector3.zero, right = Vector3.zero;
        bool connected = false;
        void Flush()
        {
            if (mesh.VertexCount > 0)
            {
                GameObject piece = Part("Stream " + part++, mesh.Build("Stream"), streamMaterial, 0, false);
                stagedStreamPieces.Add(piece);
                var renderer = piece.GetComponent<MeshRenderer>();
                renderer.enabled = !incremental;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            mesh = new RealmMeshes(); sectionSteps = 0;
        }
        for (int i = 0; i <= steps; i++)
        {
            if (incremental && (work >= 6 || SliceExpired())) { yield return null; ResetSlice(); }
            float t = i / (float)steps;
            Vector3 point = PathPoint(t);
            distance += RealmSites.Flat(point - previous).magnitude;
            float halfWidth = 1.3f + 0.5f * Mathf.Sin(distance * 0.6f + seed) + 0.25f * Mathf.Sin(distance * 1.64f);
            Vector3 side = PathSide(t);
            Vector3 nextLeft = point - side * halfWidth, nextRight = point + side * halfWidth;
            bool clear = !InSinkhole(point, 3f);
            if (connected && clear && !InSinkhole((point + previous) * 0.5f, 3f))
            {
                surface.Stamp(mesh, left, right, nextLeft, nextRight, previousDistance, distance);
                if (++sectionSteps >= 12 || mesh.VertexCount >= 512) Flush();
            }
            else Flush();
            // These endpoints and flow distances carry across render sections without any reset.
            left = nextLeft; right = nextRight; previous = point; previousDistance = distance; connected = clear;
        }
        Flush();
        pieces -= streamPieces.Count;
        foreach (GameObject piece in streamPieces) DestroyStreamPiece(piece);
        streamPieces.Clear();
        streamPieces.AddRange(stagedStreamPieces);
        stagedStreamPieces.Clear();
        foreach (GameObject piece in streamPieces) if (piece != null) piece.GetComponent<Renderer>().enabled = true;
        streamBuild = null;
    }

    /// <summary>Lamp stalks on both banks: twisted root trunks holding a glowing orb in a cupped top.</summary>
    private List<Vector3> Lamps(System.Random random)
    {
        var tops = new List<Vector3>();
        for (int side = -1; side <= 1; side += 2)
        for (int i = 0; i < lampsPerSide; i++)
        {
            float t = Mathf.Lerp(0.06f, 0.94f, (i + (side > 0 ? 0.25f : 0.75f)) / lampsPerSide);
            Vector3 p = PathPoint(t) + PathSide(t) * side * Mathf.Lerp(4f, 7f, (float)random.NextDouble());
            if (InSinkhole(p, 2f)) continue;
            if (!Ground(p, out Vector3 basePoint, out _)) continue;
            float height = Mathf.Lerp(3f, 5.5f, (float)random.NextDouble());
            tops.Add(Lamp(basePoint, height, random));
            lampBases.Add(basePoint);
        }
        return tops;
    }

    private Vector3 Lamp(Vector3 basePoint, float height, System.Random random)
    {
        var stalk = new RealmMeshes();
        float piece = (float)random.NextDouble();
        float twist = Mathf.Lerp(1.5f, 3f, (float)random.NextDouble()) * (random.NextDouble() < 0.5 ? 1f : -1f);
        Vector3 lean = new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f) * 0.6f;
        Vector3 top = basePoint + Vector3.up * height + lean;
        const int strands = 3;
        for (int s = 0; s < strands; s++)
        {
            var path = new List<Vector3>();
            float phase = s / (float)strands * Mathf.PI * 2f;
            for (int k = 0; k <= 16; k++)
            {
                float u = k / 16f;
                // Splayed roots at the foot, a tight twist up the trunk, a cup opening at the top.
                float spread = Mathf.Lerp(0.6f, 0.1f, Mathf.SmoothStep(0f, 1f, u * 4f)) + Mathf.SmoothStep(0.8f, 1f, u) * 0.35f;
                float a = phase + u * twist * Mathf.PI;
                Vector3 axis = Vector3.Lerp(basePoint - Vector3.up * 0.3f, top, u);
                path.Add(axis + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * spread);
            }
            stalk.Tube(path, u => Mathf.Lerp(0.13f, 0.07f, u), 7, 0f, 0.95f, piece, 0.05f);
        }
        GameObject trunk = Part("Lamp Stalk", stalk.Build("Lamp Stalk"), rootMaterial, 0, false);
        var capsule = trunk.AddComponent<CapsuleCollider>();
        capsule.center = (basePoint + top) * 0.5f;
        capsule.height = height;
        capsule.radius = 0.25f;

        var orb = new RealmMeshes();
        float radius = Mathf.Lerp(0.32f, 0.48f, (float)random.NextDouble());
        Vector3 centre = top + Vector3.up * radius * 0.6f;
        orb.Sphere(centre, Vector3.one * radius, Quaternion.identity, 10, 14, 1f, piece);
        Part("Lamp Orb", orb.Build("Lamp Orb"), lampMaterial, 0, false).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (random.NextDouble() < 0.6) Drips(top + Vector3.up * 0.05f, radius, random);

        if (lampLights)
        {
            var glow = new GameObject("Lamp Light", typeof(Light));
            glow.transform.SetParent(root, false);
            glow.transform.position = centre;
            Light light = glow.GetComponent<Light>();
            light.type = LightType.Point;
            light.range = 7f;
            light.intensity = 1.4f;
            light.color = new Color(0.9f, 0.94f, 1f);
            light.shadows = LightShadows.None;
        }
        return centre;
    }

    /// <summary>
    /// Sticky fluorescent slime: lumps lying on the ground near the spire and along the water, collars wrapped
    /// round lamp feet, a lump on each fossil, and drips hanging from the lamp cups.
    /// </summary>
    private void Slime(System.Random random)
    {
        var mesh = new RealmMeshes();

        void Lump(Vector3 at, Vector3 normal, float length, float thick)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            Vector3 dir = Vector3.ProjectOnPlane(new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)), normal).normalized;
            Vector3 side = Vector3.Cross(normal, dir);
            float bend = Mathf.Lerp(-0.6f, 0.6f, (float)random.NextDouble());
            var path = new List<Vector3>();
            for (int k = 0; k <= 10; k++)
            {
                float u = k / 10f;
                float s = (u - 0.5f) * length;
                // Half sunk, so it drapes over the ground like something resting there.
                path.Add(at + dir * s + side * bend * s * s / length + normal * thick * 0.25f);
            }
            float phase = (float)random.NextDouble() * 6f;
            mesh.Tube(path, u => Mathf.Max(0.03f, thick * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * Mathf.PI)), 0.7f) * (1f + 0.15f * Mathf.Sin(u * 12f + phase))),
                9, 0f, 1f, (float)random.NextDouble(), thick * 0.3f);
        }

        // Near the spire's foot and along the water.
        Vector3 spireFoot = RealmSites.AreaCentre(world);
        float rim = HoleRadiusAt(spireFoot);
        for (int i = 0; i < slimePatches; i++)
        {
            Vector3 p;
            // A few clumps round the spire's foot and beside the shelters; never in a row along the water.
            if (layout.active) p = ThemePoint(layout.kelp, random);
            else if (i % 2 == 0 || shelterSpots.Count == 0)
            {
                float a = (float)random.NextDouble() * Mathf.PI * 2f;
                p = spireFoot + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (rim + Mathf.Lerp(2f, 9f, (float)random.NextDouble()));
            }
            else
            {
                Vector3 shelter = shelterSpots[random.Next(shelterSpots.Count)];
                p = shelter + new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f) * 7f;
            }
            if (InSinkhole(p, 1.5f)) continue;
            if (layout.active && (layout.District(p) != 0 || layout.OnApproach(p))) continue;
            if (!Ground(p, out Vector3 ground, out Vector3 normal)) continue;
            int lumps = random.Next(1, 4);
            for (int k = 0; k < lumps; k++)
                Lump(ground + new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f) * 1.2f, normal,
                    Mathf.Lerp(0.9f, 2.4f, (float)random.NextDouble()), Mathf.Lerp(0.16f, 0.32f, (float)random.NextDouble()));
        }

        // Collars round lamp feet, and drips from the cups.
        for (int i = 0; i < lampBases.Count; i++)
        {
            if (layout.active && layout.District(lampBases[i]) != 0) continue;
            if (random.NextDouble() < 0.2)
            {
                var ring = new List<Vector3>();
                float start = (float)random.NextDouble() * Mathf.PI * 2f, sweep = Mathf.Lerp(2.5f, 5.5f, (float)random.NextDouble());
                for (int k = 0; k <= 14; k++)
                {
                    float a = start + k / 14f * sweep;
                    ring.Add(lampBases[i] + new Vector3(Mathf.Cos(a), 0.1f + k * 0.01f, Mathf.Sin(a)) * 0.45f);
                }
                mesh.Tube(ring, u => 0.14f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * Mathf.PI)), 0.6f) + 0.03f, 8, 0f, 1f, (float)random.NextDouble(), 0.04f);
            }
        }

        GameObject slime = Part("Slime", mesh.Build("Slime"), slimeMaterial, 7, true);
        slime.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>Slime dripping from a lamp cup: a stretched drop with a bulb at the end.</summary>
    private void Drips(Vector3 cup, float radius, System.Random random)
    {
        if (slimeMaterial == null) return;
        if (layout.active && layout.District(cup) != 0) return;
        var mesh = new RealmMeshes();
        int count = random.Next(1, 3);
        for (int i = 0; i < count; i++)
        {
            float a = (float)random.NextDouble() * Mathf.PI * 2f;
            Vector3 from = cup + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * 0.8f;
            float length = Mathf.Lerp(0.35f, 0.9f, (float)random.NextDouble());
            var path = new List<Vector3>();
            for (int k = 0; k <= 8; k++) path.Add(from + Vector3.down * length * k / 8f);
            mesh.Tube(path, u => Mathf.Lerp(0.05f, 0.02f, Mathf.Sin(u * Mathf.PI * 0.8f)) + Mathf.SmoothStep(0.75f, 1f, u) * 0.05f, 7, 0.2f, 1f, (float)random.NextDouble(), 0.05f);
        }
        Part("Lamp Drips", mesh.Build("Lamp Drips"), slimeMaterial, 0, false).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>A shelter of arching roots meeting in a tangle, half roofed with a shimmering film. Climbable.</summary>
    private void Shelter(float t, float side, System.Random random)
    {
        Vector3 centre = PathPoint(t) + PathSide(t) * side * Mathf.Lerp(9f, 12f, (float)random.NextDouble());
        if (InSinkhole(centre, 5f)) return;
        if (!Ground(centre, out Vector3 ground, out _)) return;
        shelterSpots.Add(ground);
        float radius = Mathf.Lerp(3f, 3.8f, (float)random.NextDouble());
        float height = Mathf.Lerp(3.5f, 4.6f, (float)random.NextDouble());
        Vector3 crown = ground + Vector3.up * height;

        var roots = new RealmMeshes();
        int count = random.Next(9, 13);
        for (int i = 0; i < count; i++)
        {
            float a = (i + (float)random.NextDouble() * 0.5f) / count * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 foot = ground + dir * radius * Mathf.Lerp(0.9f, 1.3f, (float)random.NextDouble());
            Ground(foot, out foot, out _);
            Vector3 meet = crown + new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() * 0.6f, (float)random.NextDouble() - 0.5f) * 1.2f;
            var path = new List<Vector3>();
            for (int k = 0; k <= 14; k++)
            {
                float u = k / 14f;
                // Up from the ground, bowing outwards, then curling in over the top.
                Vector3 p = Vector3.Lerp(foot - Vector3.up * 0.5f, meet, u);
                p += dir * Mathf.Sin(u * Mathf.PI) * radius * 0.35f;
                p += Vector3.up * Mathf.Sin(u * Mathf.PI) * height * 0.25f;
                p += new Vector3(Mathf.Sin(u * 9f + i), Mathf.Sin(u * 7f + i * 2f) * 0.5f, Mathf.Cos(u * 8f + i)) * 0.15f;
                path.Add(p);
            }
            float thick = Mathf.Lerp(0.14f, 0.26f, (float)random.NextDouble());
            roots.Tube(path, u => thick * Mathf.Lerp(1.3f, 0.6f, u), 8, 0f, 1f, (float)random.NextDouble(), thick * 0.5f);
        }
        GameObject shelter = Part("Root Shelter", roots.Build("Root Shelter"), rootMaterial, 7, true);

        // The film: a sagging sheet over part of the crown, seen from both sides.
        var film = new RealmMeshes();
        var rows = new List<Vector3>();
        var ups = new List<Vector3>();
        float filmAngle = (float)random.NextDouble() * Mathf.PI * 2f;
        Vector3 across = new Vector3(Mathf.Cos(filmAngle), 0f, Mathf.Sin(filmAngle));
        for (int k = 0; k <= 10; k++)
        {
            float u = k / 10f * 2f - 1f;
            Vector3 p = crown + across * u * radius * 0.9f + Vector3.down * (u * u) * height * 0.35f + Vector3.up * 0.4f;
            rows.Add(p);
            ups.Add((Vector3.up - across * u * 0.8f).normalized);
        }
        film.Ribbon(rows, ups, radius * 0.75f, 6, (float)random.NextDouble());
        Part("Root Film", film.Build("Root Film"), filmMaterial, 0, false).transform.SetParent(shelter.transform, true);
    }

    /// <summary>Crystals floating over the path and round the upper spire; they bob and turn slowly.</summary>
    private void Crystals(System.Random random)
    {
        HangingSpire spire = FindFirstObjectByType<HangingSpire>();
        var spots = new List<Vector3>();
        if (!layout.active && spire != null && spire.Built)
            foreach (var landing in spire.Landings)
                if (random.NextDouble() < 0.5) spots.Add(landing.top - landing.inward * Mathf.Lerp(4f, 8f, (float)random.NextDouble()) + Vector3.up * Mathf.Lerp(1f, 5f, (float)random.NextDouble()));
        for (int attempt = 0; spots.Count < floatingCrystals && attempt < floatingCrystals * 12; attempt++)
        {
            float t = (float)random.NextDouble();
            Vector3 p = layout.active ? ThemePoint(layout.crystal, random) : PathPoint(t) + PathSide(t) * Mathf.Lerp(-10f, 10f, (float)random.NextDouble());
            if (layout.active && (layout.District(p) != 1 || layout.OnApproach(p))) continue;
            if (!Ground(p, out Vector3 floor, out _)) continue;
            p.y = floor.y + Mathf.Lerp(5f, 9f, (float)random.NextDouble());
            spots.Add(p);
        }
        for (int i = 0; i < Mathf.Min(floatingCrystals, spots.Count); i++)
        {
            var mesh = new RealmMeshes();
            float length = Mathf.Lerp(1.2f, 3.2f, (float)random.NextDouble());
            mesh.Crystal(Vector3.zero, Quaternion.identity, length, length * Mathf.Lerp(0.16f, 0.26f, (float)random.NextDouble()), random.Next(5, 8), (float)random.NextDouble(), random);
            GameObject crystal = RealmSites.Part("Floating Crystal", root, mesh.Build("Floating Crystal"), crystalMaterial, 7, true);
            pieces++;
            crystal.transform.position = spots[i];
            crystal.transform.rotation = Quaternion.Euler((float)random.NextDouble() * 40f - 20f, (float)random.NextDouble() * 360f, (float)random.NextDouble() * 40f - 20f);
            floaters.Add((crystal.transform, spots[i], (float)random.NextDouble() * 6.3f, new Vector3(0f, Mathf.Lerp(6f, 18f, (float)random.NextDouble()), 0f)));
        }
    }

    /// <summary>A fossil half sunk into the ground: an ammonite spiral, or a row of curved ribs. Climbable.</summary>
    private void Fossil(float t, float side, int index, System.Random random)
    {
        Vector3 centre = PathPoint(t) + PathSide(t) * side * Mathf.Lerp(7f, 14f, (float)random.NextDouble());
        if (InSinkhole(centre, 4f)) return;
        if (!Ground(centre, out Vector3 ground, out Vector3 normal)) return;
        var mesh = new RealmMeshes();
        float piece = (float)random.NextDouble();
        Quaternion tilt = Quaternion.FromToRotation(Vector3.up, normal) *
            Quaternion.Euler(Mathf.Lerp(55f, 80f, (float)random.NextDouble()), (float)random.NextDouble() * 360f, 0f);

        if (index % 2 == 0)
        {
            // Ammonite: a tube winding inwards on a flat spiral, standing half buried.
            float size = Mathf.Lerp(1.8f, 3f, (float)random.NextDouble());
            var path = new List<Vector3>();
            const int steps = 70;
            const float turns = 2.6f;
            for (int k = 0; k <= steps; k++)
            {
                float u = k / (float)steps;
                float a = u * turns * Mathf.PI * 2f;
                float r = size * Mathf.Exp(-u * 1.8f);
                path.Add(ground + tilt * new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
            mesh.Tube(path, u => size * 0.32f * Mathf.Exp(-u * 1.8f), 10, 0f, 1f, piece, 0f);
        }
        else
        {
            // A row of curved ribs coming out of the ground.
            int ribs = random.Next(5, 8);
            float span = Mathf.Lerp(3f, 4.5f, (float)random.NextDouble());
            for (int r = 0; r < ribs; r++)
            {
                var path = new List<Vector3>();
                float offset = (r - ribs * 0.5f) * 0.7f;
                float size = span * (1f - Mathf.Abs(r - ribs * 0.5f) / ribs * 0.6f);
                for (int k = 0; k <= 12; k++)
                {
                    float u = k / 12f;
                    float a = Mathf.Lerp(0.1f, Mathf.PI * 0.9f, u);
                    path.Add(ground + tilt * new Vector3(Mathf.Cos(a) * size * 0.6f, offset, Mathf.Sin(a) * size) - normal * 0.4f);
                }
                mesh.Tube(path, u => 0.16f * (1f - u * 0.4f), 7, r / (float)ribs, (r + 1f) / ribs, piece, 0f);
            }
        }
        Part("Fossil", mesh.Build("Fossil"), fossilMaterial, 7, true);
        fossilSpots.Add(ground);
    }

    /// <summary>A clump of dark anemone fingers by the water, glowing at the tips.</summary>
    private void Anemones(float t, float side, System.Random random)
    {
        Vector3 p = layout.active ? ThemePoint(layout.kelp, random)
            : PathPoint(t) + PathSide(t) * side * Mathf.Lerp(1.8f, 3.2f, (float)random.NextDouble());
        if (layout.active && (layout.District(p) != 0 || layout.OnApproach(p))) return;
        if (InSinkhole(p, 2f)) return;
        if (!Ground(p, out Vector3 ground, out Vector3 normal)) return;
        var mesh = new RealmMeshes();
        int fingers = random.Next(7, 13);
        for (int i = 0; i < fingers; i++)
        {
            float a = (float)random.NextDouble() * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float length = Mathf.Lerp(0.7f, 1.6f, (float)random.NextDouble());
            Vector3 foot = ground + dir * (float)random.NextDouble() * 0.4f - normal * 0.1f;
            var path = new List<Vector3>();
            for (int k = 0; k <= 8; k++)
            {
                float u = k / 8f;
                path.Add(foot + normal * u * length + dir * u * u * length * 0.5f);
            }
            mesh.Tube(path, u => Mathf.Lerp(0.08f, 0.035f, u), 6, 0f, 1f, (float)random.NextDouble(), 0.05f);
        }
        Part("Anemones", mesh.Build("Anemones"), anemoneMaterial, 7, true);
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }
}
