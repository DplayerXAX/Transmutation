using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class ProceduralWorld
{
    private sealed class ChunkState
    {
        public Vector3Int coord;
        public int revision;
        public bool complete;
        public GameObject gameObject;
        public Mesh mesh;
        public int failures;
    }
    private sealed class ChunkResult
    {
        public Vector3Int coord;
        public int epoch, revision;
        public Vector3[] vertices, normals;
        public int[] surfaceTriangles, innerTriangles;
        public Mesh mesh;
        public volatile bool baked;
        public Exception error;
        public float bakeMilliseconds;
    }
    private sealed class EditBatch
    {
        public readonly HashSet<Vector3Int> coords = new HashSet<Vector3Int>();
        public readonly Dictionary<Vector3Int, ChunkResult> results = new Dictionary<Vector3Int, ChunkResult>();
        public Bounds bounds;
        public float amount;
        public double started;
    }
    private readonly Dictionary<Vector3Int, ChunkState> chunks = new Dictionary<Vector3Int, ChunkState>();
    private readonly Dictionary<Vector3Int, float> modifications = new Dictionary<Vector3Int, float>();
    private readonly Dictionary<Vector3Int, float> incoming = new Dictionary<Vector3Int, float>();
    private readonly Dictionary<Vector3Int, float> pending = new Dictionary<Vector3Int, float>();
    private readonly Queue<Vector3Int> initialJobs = new Queue<Vector3Int>();
    private readonly Queue<Vector3Int> editJobs = new Queue<Vector3Int>();
    private readonly ConcurrentQueue<ChunkResult> finished = new ConcurrentQueue<ChunkResult>();
    private readonly Queue<ChunkResult> bakeQueue = new Queue<ChunkResult>();
    private readonly Queue<ChunkResult> editBakeQueue = new Queue<ChunkResult>();
    private readonly List<Task> workers = new List<Task>();
    private readonly List<Vector3Int> sampleScratch = new List<Vector3Int>();
    private readonly Collider[] wakeBuffer = new Collider[128];
    private CancellationTokenSource cancellation;
    private DensityField field;
    private EditBatch batch;
    private Vector3 gridMin;
    private Vector3Int chunkCount;
    private Sinkhole[] sinkholes;
    private Vector2 noiseOffset;
    private float yMin, yMax, erosionTimer;
    private int epoch;
    private string fingerprint;
    private Rigidbody playerBody;
    private Vector3 spawnPosition;
    // Retained for the existing climbing batch driver.
    private bool generating;
    [SerializeField] private int chunksBuilt, chunksTotal;
    public event Action<GameObject, Mesh> ChunkCreated;
    public event Action<GameObject, Mesh, Bounds> ChunkUpdated;
    public event Action<Bounds> TerrainChanged;
    public static event Action<ProceduralWorld, Bounds> AnyTerrainChanged;
    public event Action<Bounds, float> ErosionApplied;
    public bool LayoutReady => field != null;
    public bool GenerationComplete => LayoutReady && chunksBuilt == chunksTotal;
    /// <summary>Share of chunks built so far, 0..1. Chunks load outward from the spawn.</summary>
    public float LoadProgress => chunksTotal == 0 ? 0f : (float)chunksBuilt / chunksTotal;
    public bool UpdatesPending => batch != null || pending.Count > 0 || incoming.Count > 0;
    public int SinkholeCount => sinkholes == null ? 0 : sinkholes.Length;
    /// <summary>Size of the world on x and z (metres), centred on this transform.</summary>
    public Vector2 WorldSize => worldSize;
    public int ModifiedSampleCount => modifications.Count;
    public int PendingSampleCount => pending.Count + incoming.Count;
    public int ActiveWorkers => workers.Count;
    public int StaleResultsRejected { get; private set; }
    public float TotalDensityRemoved { get; private set; }
    public float CellSize => field != null ? field.cellSize : cellSize;
    private int GridChunkCells => field != null ? field.chunkCells : chunkCells;
    public float DensityBudgetPerSecond => densityBudgetPerSecond;
    public float Erodibility => erodibility;
    public int TerrainLayer => chunkLayer;
    public Vector3 GridMinimum => transform.position + gridMin;
    public float ChunkWidth => GridChunkCells * CellSize;
    public Bounds VolumeBounds => new Bounds(transform.position + gridMin + (Vector3)chunkCount * ChunkWidth * 0.5f, (Vector3)chunkCount * ChunkWidth);
    public float LastUploadMilliseconds { get; private set; }
    public float LastCommitMilliseconds { get; private set; }
    public float LastBakeMilliseconds { get; private set; }
    public float LastUpdateLatencyMilliseconds { get; private set; }
    public string LastUpdateError { get; private set; }
    private static readonly ProfilerMarker UploadMarker = new ProfilerMarker("Erosion.MeshUpload");
    private static readonly ProfilerMarker CommitMarker = new ProfilerMarker("Erosion.Commit");
    private static readonly ProfilerMarker BakeMarker = new ProfilerMarker("Erosion.CollisionBake");
    private static readonly ProfilerMarker ScheduleMarker = new ProfilerMarker("Erosion.Schedule");
    private static readonly ProfilerMarker BatchMarker = new ProfilerMarker("Erosion.PrepareBatch");
    private const MeshColliderCookingOptions Cooking = MeshColliderCookingOptions.CookForFasterSimulation |
        MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices |
        MeshColliderCookingOptions.UseFastMidphase;

    public Vector3 SinkholeCentre(int index, float worldY, out float radius)
    {
        Sinkhole hole = sinkholes[index];
        radius = hole.radius;
        float localY = worldY - transform.position.y;
        return transform.position + new Vector3(hole.position.x, localY, hole.position.y - localY * 0.07f);
    }
    public void ColumnHeights(Vector3 worldPoint, out float surface, out float ceiling, out float innerGround)
    {
        Vector3 local = worldPoint - transform.position;
        float h = SurfaceHeight(local.x, local.z);
        surface = transform.position.y + h;
        ceiling = transform.position.y + CeilingHeight(h);
        innerGround = transform.position.y + InnerGroundHeight(local.x, local.z, h);
    }
    private void Start()
    {
        foreach (Rigidbody body in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
            if (body.gameObject.scene == gameObject.scene && body.GetComponent<SmoothFirstPersonController>() != null) playerBody = body;
        if (playerBody != null) spawnPosition = playerBody.position;
        if (!LayoutReady) Generate(false);
        PlacePlayerOnSurface();
    }
    private void PlacePlayerOnSurface()
    {
        if (playerBody == null) return;
        Physics.SyncTransforms();
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(spawnPosition.x, transform.position.y + yMax + 5f, spawnPosition.z),
            Vector3.down, yMax - yMin + 10f, 1 << chunkLayer, QueryTriggerInteraction.Ignore);
        float top = float.MinValue;
        foreach (RaycastHit hit in hits) if (OwnsCollider(hit.collider)) top = Mathf.Max(top, hit.point.y);
        if (top == float.MinValue) return;
        Collider collider = playerBody.GetComponent<Collider>();
        spawnPosition.y = top + (collider != null ? collider.bounds.extents.y : 1f) + 0.05f;
        playerBody.position = spawnPosition;
        playerBody.linearVelocity = Vector3.zero;
    }
    public bool OwnsCollider(Collider collider)
    {
        if (collider == null || collider.gameObject.layer != chunkLayer) return false;
        ProceduralWorldChunk marker = collider.GetComponent<ProceduralWorldChunk>();
        return marker != null && marker.Owner == this && collider is MeshCollider;
    }
    public bool IsRegionReady(Vector3 position, float radius)
    {
        if (!LayoutReady) return false;
        Vector3 centre = transform.InverseTransformPoint(position);
        Vector3Int min = ChunkAt(centre - Vector3.one * radius), max = ChunkAt(centre + Vector3.one * radius);
        for (int y = min.y; y <= max.y; y++)
        for (int z = min.z; z <= max.z; z++)
        for (int x = min.x; x <= max.x; x++)
            if (!chunks.TryGetValue(new Vector3Int(x, y, z), out ChunkState state) || !state.complete) return false;
        return true;
    }
    private Vector3Int ChunkAt(Vector3 local) => Vector3Int.FloorToInt((local - gridMin) / ChunkWidth);
    public float SampleDensity(Vector3 worldPosition, bool includeErosion = true)
    {
        if (!LayoutReady) return 0f;
        Vector3 g = (transform.InverseTransformPoint(worldPosition) - gridMin) / CellSize;
        Vector3Int lo = Vector3Int.FloorToInt(g);
        Vector3 f = g - lo;
        float value = 0f;
        for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++) for (int x = 0; x < 2; x++)
        {
            Vector3Int key = lo + new Vector3Int(x, y, z);
            Vector3 p = gridMin + (Vector3)key * CellSize;
            float h = field.SurfaceHeight(p.x, p.z);
            float d = field.Density(p, h, field.CeilingHeight(h), field.InnerGroundHeight(p.x, p.z, h));
            if (includeErosion && modifications.TryGetValue(key, out float delta)) d += delta;
            value += d * (x == 0 ? 1f - f.x : f.x) * (y == 0 ? 1f - f.y : f.y) * (z == 0 ? 1f - f.z : f.z);
        }
        return value;
    }
    /// <summary>Queues positive, time-integrated density removal. Main-thread only.</summary>
    public void Erode(Vector3 worldPosition, float radius, float strength)
    {
        if (!LayoutReady || LastUpdateError != null || !Finite(worldPosition.x) || !Finite(worldPosition.y) || !Finite(worldPosition.z) ||
            !Finite(radius) || !Finite(strength) || radius <= 0f || strength <= 0f) return;
        Vector3 centre = transform.InverseTransformPoint(worldPosition);
        // Enough support to reach lattice samples even between voxel corners.
        radius = Mathf.Max(radius, CellSize * 1.5f);
        Vector3Int min = Vector3Int.FloorToInt((centre - Vector3.one * radius - gridMin) / CellSize);
        Vector3Int max = Vector3Int.CeilToInt((centre + Vector3.one * radius - gridMin) / CellSize);
        min = Vector3Int.Max(min, Vector3Int.zero);
        max = Vector3Int.Min(max, chunkCount * GridChunkCells);
        for (int y = min.y; y <= max.y; y++) for (int z = min.z; z <= max.z; z++) for (int x = min.x; x <= max.x; x++)
        {
            var key = new Vector3Int(x, y, z);
            float t = Vector3.Distance(centre, gridMin + (Vector3)key * CellSize) / radius;
            if (t >= 1f) continue;
            float amount = strength * Mathf.Clamp01(erosionFalloff.Evaluate(t));
            if (amount <= 0f) continue;
            incoming.TryGetValue(key, out float previous);
            incoming[key] = previous + amount;
        }
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private void FixedUpdate()
    {
        if (LastUpdateError != null) return;
        erosionTimer += Time.fixedDeltaTime;
        if (erosionTimer >= Mathf.Max(0.01f, erosionInterval))
        {
            float total = 0f;
            foreach (float amount in incoming.Values) total += amount;
            float scale = total > 0f ? Mathf.Min(1f, densityBudgetPerSecond * erosionTimer / total) : 0f;
            foreach (var pair in incoming)
            {
                if (pair.Value * scale <= 0f) continue;
                pending.TryGetValue(pair.Key, out float previous);
                pending[pair.Key] = previous + pair.Value * scale;
            }
            incoming.Clear();
            erosionTimer = 0f;
        }
        if (batch != null && batch.results.Count == batch.coords.Count)
        {
            bool ready = true;
            foreach (ChunkResult result in batch.results.Values) ready &= result.baked;
            if (ready) CommitBatch();
        }
        if (batch == null && pending.Count > 0) BeginBatch();
    }
    // A sample's one-cell normal halo touches at most two chunks per axis.
    private void SampleOwners(Vector3Int sample, HashSet<Vector3Int> owners)
    {
        Vector3Int min = Vector3Int.FloorToInt(((Vector3)sample - Vector3.one * 2f) / GridChunkCells);
        Vector3Int max = Vector3Int.FloorToInt(((Vector3)sample + Vector3.one) / GridChunkCells);
        min = Vector3Int.Max(min, Vector3Int.zero);
        max = Vector3Int.Min(max, chunkCount - Vector3Int.one);
        for (int y = min.y; y <= max.y; y++) for (int z = min.z; z <= max.z; z++) for (int x = min.x; x <= max.x; x++)
            owners.Add(new Vector3Int(x, y, z));
    }
    private void BeginBatch()
    {
        using var profile = BatchMarker.Auto();
        batch = new EditBatch { started = Time.realtimeSinceStartupAsDouble };
        var owners = new HashSet<Vector3Int>();
        foreach (var pair in pending) { SampleOwners(pair.Key, batch.coords); break; }
        sampleScratch.Clear();
        foreach (var pair in pending)
        {
            owners.Clear();
            SampleOwners(pair.Key, owners);
            bool fits = true;
            foreach (Vector3Int owner in owners) if (!batch.coords.Contains(owner)) { fits = false; break; }
            if (fits) sampleScratch.Add(pair.Key);
        }
        bool first = true;
        foreach (Vector3Int key in sampleScratch)
        {
            float amount = pending[key];
            modifications.TryGetValue(key, out float old);
            modifications[key] = old - amount;
            Vector3 p = transform.TransformPoint(gridMin + (Vector3)key * CellSize);
            if (first) { batch.bounds = new Bounds(p, Vector3.zero); first = false; }
            else batch.bounds.Encapsulate(p);
            batch.amount += amount;
            pending.Remove(key);
        }
        batch.bounds.Expand(CellSize * 4f);
        foreach (Vector3Int coord in batch.coords) { chunks[coord].revision++; editJobs.Enqueue(coord); }
    }
    private void Update()
    {
        workers.RemoveAll(task => task.IsCompleted);
        int prepared = 0, initialPrepared = 0;
        // All results are lightweight data, including explicitly empty chunks.
        while (prepared < erosionMeshesPerFrame && initialPrepared < meshesPerFrame && finished.TryDequeue(out ChunkResult result))
        {
            if (!Current(result)) { StaleResultsRejected++; continue; }
            if (result.error != null) { Retry(result); continue; }
            if (result.mesh == null && result.vertices.Length > 0)
            {
                double start = Time.realtimeSinceStartupAsDouble;
                using (UploadMarker.Auto()) result.mesh = MakeMesh(result);
                LastUploadMilliseconds = (float)((Time.realtimeSinceStartupAsDouble - start) * 1000.0);
            }
            if (batch != null && batch.coords.Contains(result.coord) && result.revision > 0)
            { batch.results[result.coord] = result; prepared++; }
            else initialPrepared++;
            if (result.mesh == null) { result.baked = true; if (result.revision == 0) Apply(result, false, default); }
            else if (result.revision > 0) editBakeQueue.Enqueue(result);
            else bakeQueue.Enqueue(result);
        }
        Schedule();
        generating = !GenerationComplete;
        if (respawnFallingPlayer && playerBody != null && playerBody.position.y < transform.position.y + yMin - 20f)
        { playerBody.position = spawnPosition + Vector3.up; playerBody.linearVelocity = Vector3.zero; }
    }
    private bool Current(ChunkResult result) => result.epoch == epoch && chunks.TryGetValue(result.coord, out ChunkState state) && state.revision == result.revision;
    private void Schedule()
    {
        using var profile = ScheduleMarker.Auto();
        if (cancellation == null || LastUpdateError != null) return;
        while (workers.Count < Mathf.Max(1, workerCount))
        {
            if (editBakeQueue.Count > 0 || (editJobs.Count == 0 && bakeQueue.Count > 0))
            {
                ChunkResult result = editBakeQueue.Count > 0 ? editBakeQueue.Dequeue() : bakeQueue.Dequeue();
                if (!Current(result)) { if (result.mesh != null) DestroySafe(result.mesh); StaleResultsRejected++; continue; }
                var id = result.mesh.GetEntityId();
                workers.Add(Task.Run(() =>
                {
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    try { using (BakeMarker.Auto()) Physics.BakeMesh(id, false, Cooking); }
                    catch (Exception error) { result.error = error; }
                    result.bakeMilliseconds = (float)timer.Elapsed.TotalMilliseconds;
                    result.baked = true;
                }));
                // Initial meshes are published only once baking is complete, on the main thread.
                if (result.revision == 0) initialReady.Add(result);
                continue;
            }
            Vector3Int coord;
            if (editJobs.Count > 0) coord = editJobs.Dequeue();
            else if (initialJobs.Count > 0) coord = initialJobs.Dequeue();
            else break;
            ChunkState state = chunks[coord];
            if (state.revision > 0 && (batch == null || !batch.coords.Contains(coord))) continue;
            // A formerly queued initial job must not produce a second result for an edit batch.
            if (state.revision > 0 && scheduledEdits.Contains((coord, state.revision))) continue;
            if (state.revision > 0) scheduledEdits.Add((coord, state.revision));
            int version = state.revision, generation = epoch;
            var snapshot = CopyModifications(coord);
            var immutableField = field;
            Vector3 origin = gridMin;
            CancellationToken token = cancellation.Token;
            workers.Add(Task.Run(() =>
            {
                try { finished.Enqueue(BuildChunk(immutableField, origin, coord, generation, version, snapshot, token)); }
                catch (OperationCanceledException) { }
                catch (Exception error) { finished.Enqueue(new ChunkResult { coord = coord, epoch = generation, revision = version, error = error }); }
            }));
        }
        for (int i = initialReady.Count - 1; i >= 0; i--)
        {
            ChunkResult result = initialReady[i];
            if (!result.baked) continue;
            initialReady.RemoveAt(i);
            if (Current(result) && result.error != null) { Retry(result); continue; }
            if (Current(result)) Apply(result, false, default);
            else { DestroySafe(result.mesh); StaleResultsRejected++; }
        }
    }
    private void Retry(ChunkResult result)
    {
        if (result.mesh != null) DestroySafe(result.mesh);
        if (batch != null) batch.results.Remove(result.coord);
        scheduledEdits.Remove((result.coord, result.revision));
        ChunkState state = chunks[result.coord];
        if (++state.failures <= 2) { if (result.revision > 0) editJobs.Enqueue(result.coord); else initialJobs.Enqueue(result.coord); }
        else { LastUpdateError = result.error.ToString(); Debug.LogError("Terrain update stopped; Reset Erosion can retry. " + LastUpdateError, this); }
    }
    private readonly List<ChunkResult> initialReady = new List<ChunkResult>();
    private readonly HashSet<(Vector3Int, int)> scheduledEdits = new HashSet<(Vector3Int, int)>();
    private Dictionary<Vector3Int, float> CopyModifications(Vector3Int coord)
    {
        var copy = new Dictionary<Vector3Int, float>();
        Vector3Int lo = coord * GridChunkCells - Vector3Int.one, hi = (coord + Vector3Int.one) * GridChunkCells + Vector3Int.one;
        // Copy a bounded region, rather than scanning every historical terrain edit.
        for (int y = lo.y; y <= hi.y; y++) for (int z = lo.z; z <= hi.z; z++) for (int x = lo.x; x <= hi.x; x++)
        { var key = new Vector3Int(x, y, z); if (modifications.TryGetValue(key, out float value)) copy[key] = value; }
        return copy;
    }
    private Mesh MakeMesh(ChunkResult result)
    {
        var mesh = new Mesh { name = "World Chunk " + result.coord, hideFlags = HideFlags.DontSave,
            indexFormat = result.vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.vertices = result.vertices;
        mesh.normals = result.normals;
        mesh.subMeshCount = 2;
        mesh.SetTriangles(result.surfaceTriangles, 0);
        mesh.SetTriangles(result.innerTriangles, 1);
        mesh.RecalculateBounds();
        return mesh;
    }
    private void Apply(ChunkResult result, bool update, Bounds changedBounds)
    {
        ChunkState state = chunks[result.coord];
        state.failures = 0;
        LastBakeMilliseconds = result.bakeMilliseconds;
        bool created = state.gameObject == null && result.mesh != null;
        if (created)
        {
            state.gameObject = new GameObject("World Chunk " + result.coord, typeof(MeshCollider), typeof(MeshFilter), typeof(MeshRenderer), typeof(ProceduralWorldChunk))
                { hideFlags = HideFlags.DontSave, layer = chunkLayer };
            state.gameObject.transform.SetParent(transform, false);
            state.gameObject.transform.localPosition = gridMin + (Vector3)(result.coord * GridChunkCells) * CellSize;
            state.gameObject.GetComponent<ProceduralWorldChunk>().Initialize(this, result.coord);
            state.gameObject.GetComponent<MeshRenderer>().sharedMaterials = new[] { surfaceMaterial, innerMaterial != null ? innerMaterial : surfaceMaterial };
        }
        Mesh old = state.mesh;
        if (state.gameObject != null)
        {
            var collider = state.gameObject.GetComponent<MeshCollider>();
            collider.cookingOptions = Cooking;
            collider.sharedMesh = result.mesh;
            state.gameObject.GetComponent<MeshFilter>().sharedMesh = result.mesh;
            state.gameObject.GetComponent<MeshRenderer>().enabled = result.mesh != null;
        }
        state.mesh = result.mesh;
        if (!state.complete) { state.complete = true; chunksBuilt++; }
        if (old != null && old != result.mesh) DestroySafe(old);
        if (created) ChunkCreated?.Invoke(state.gameObject, result.mesh);
        else if (update && state.gameObject != null) ChunkUpdated?.Invoke(state.gameObject, result.mesh, changedBounds);
    }
    private void CommitBatch()
    {
        foreach (ChunkResult result in batch.results.Values)
            if (result.error != null) { Retry(result); return; }
        double start = Time.realtimeSinceStartupAsDouble;
        using (CommitMarker.Auto())
        {
            foreach (ChunkResult result in batch.results.Values) Apply(result, true, batch.bounds);
            Physics.SyncTransforms();
            int count = Physics.OverlapBoxNonAlloc(batch.bounds.center, batch.bounds.extents + Vector3.up * 2f,
                wakeBuffer, Quaternion.identity, ~(1 << 4), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) if (wakeBuffer[i].attachedRigidbody != null) wakeBuffer[i].attachedRigidbody.WakeUp();
            TotalDensityRemoved += batch.amount;
            TerrainChanged?.Invoke(batch.bounds);
            AnyTerrainChanged?.Invoke(this, batch.bounds);
            ErosionApplied?.Invoke(batch.bounds, batch.amount);
            foreach (Vector3Int coord in batch.coords) scheduledEdits.Remove((coord, chunks[coord].revision));
            LastUpdateLatencyMilliseconds = (float)((Time.realtimeSinceStartupAsDouble - batch.started) * 1000.0);
            batch = null;
        }
        LastCommitMilliseconds = (float)((Time.realtimeSinceStartupAsDouble - start) * 1000.0);
    }
    public void Generate(bool blockUntilDone)
    {
        Clear();
        PrepareLayout();
        field = new DensityField(this);
        string currentFingerprint = JsonUtility.ToJson(field);
        if (fingerprint != null && fingerprint != currentFingerprint) { modifications.Clear(); TotalDensityRemoved = 0f; }
        fingerprint = currentFingerprint;
        cancellation = new CancellationTokenSource();
        Vector3 spawnLocal = transform.InverseTransformPoint(playerBody != null ? spawnPosition : transform.position);
        var near = new List<Vector3Int>();
        var far = new List<Vector3Int>();
        for (int y = 0; y < chunkCount.y; y++) for (int z = 0; z < chunkCount.z; z++) for (int x = 0; x < chunkCount.x; x++)
        {
            var coord = new Vector3Int(x, y, z);
            chunks.Add(coord, new ChunkState { coord = coord });
            Vector3 centre = gridMin + new Vector3((x + 0.5f) * ChunkWidth, 0f, (z + 0.5f) * ChunkWidth);
            float distance = new Vector2(centre.x - spawnLocal.x, centre.z - spawnLocal.z).magnitude;
            (blockUntilDone || distance <= blockingRadius ? near : far).Add(coord);
        }
        chunksTotal = chunks.Count;
        // Blocking startup also uses frozen data; it is the only unbudgeted loading phase.
        var snapshots = new Dictionary<Vector3Int, Dictionary<Vector3Int, float>>();
        foreach (Vector3Int coord in near) snapshots[coord] = CopyModifications(coord);
        Parallel.ForEach(near, new ParallelOptions { MaxDegreeOfParallelism = Mathf.Max(1, workerCount) },
            coord => finished.Enqueue(BuildChunk(field, gridMin, coord, epoch, 0, snapshots[coord], cancellation.Token)));
        while (finished.TryDequeue(out ChunkResult result))
        {
            if (result.vertices.Length > 0) { result.mesh = MakeMesh(result); Physics.BakeMesh(result.mesh.GetEntityId(), false, Cooking); }
            result.baked = true;
            Apply(result, false, default);
        }
        far.Sort((a, b) => ((Vector3)a * ChunkWidth + gridMin - spawnLocal).sqrMagnitude.CompareTo(((Vector3)b * ChunkWidth + gridMin - spawnLocal).sqrMagnitude));
        foreach (Vector3Int coord in far) initialJobs.Enqueue(coord);
        generating = far.Count > 0;
    }
    public void ResetErosion()
    {
        modifications.Clear(); TotalDensityRemoved = 0f; Generate(false);
        var bounds = new Bounds(transform.position, Vector3.one * 100000f);
        TerrainChanged?.Invoke(bounds); AnyTerrainChanged?.Invoke(this, bounds);
    }
    [ContextMenu("Preview In Editor")] private void PreviewInEditor() => Generate(true);
    [ContextMenu("Reset Erosion")] private void ResetErosionMenu() => ResetErosion();
    [ContextMenu("Clear Preview")]
    private void Clear()
    {
        epoch++;
        cancellation?.Cancel();
        // Baked meshes cannot be destroyed until the native bake completes.
        try { Task.WhenAll(workers).GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        cancellation?.Dispose(); cancellation = null; workers.Clear();
        var dispose = new HashSet<Mesh>();
        foreach (ChunkState state in chunks.Values) { if (state.mesh != null) dispose.Add(state.mesh); if (state.gameObject != null) { state.gameObject.SetActive(false); DestroySafe(state.gameObject); } }
        foreach (ChunkResult result in initialReady) if (result.mesh != null) dispose.Add(result.mesh);
        foreach (ChunkResult result in bakeQueue) if (result.mesh != null) dispose.Add(result.mesh);
        foreach (ChunkResult result in editBakeQueue) if (result.mesh != null) dispose.Add(result.mesh);
        if (batch != null) foreach (ChunkResult result in batch.results.Values) if (result.mesh != null) dispose.Add(result.mesh);
        foreach (Mesh mesh in dispose) DestroySafe(mesh);
        for (int i = transform.childCount - 1; i >= 0; i--)
            if (transform.GetChild(i).name.StartsWith("World Chunk")) DestroySafe(transform.GetChild(i).gameObject);
        chunks.Clear(); initialJobs.Clear(); editJobs.Clear(); initialReady.Clear(); bakeQueue.Clear(); editBakeQueue.Clear(); scheduledEdits.Clear();
        incoming.Clear(); pending.Clear(); batch = null;
        while (finished.TryDequeue(out _)) { }
        field = null; chunksBuilt = 0; chunksTotal = 0; erosionTimer = 0f; generating = false; LastUpdateError = null;
    }
    private void OnDestroy() => Clear();
    private void OnDisable() { if (cancellation != null) Clear(); }
    private void OnEnable() { if (Application.isPlaying && fingerprint != null && !LayoutReady) Generate(false); }
    private static void DestroySafe(UnityEngine.Object target)
    { if (target == null) return; if (Application.isPlaying) Destroy(target); else DestroyImmediate(target); }
}
