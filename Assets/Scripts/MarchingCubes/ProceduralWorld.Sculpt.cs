using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public enum TerrainSculptTool
{
    Raise,
    Lower,
    Smooth,
    Flatten,
    Add,
    Carve,
}

/// <summary>
/// Saved, hand-sculpted terrain. With a TerrainSculptData asset assigned, the world is built from the asset's
/// frozen layout plus its brush edits instead of from the inspector settings. In edit mode the brushes change the
/// density on the sample grid and rebuild only the touched chunks. At runtime the edits are added to the chunks
/// that contain them; chunks without edits are built exactly as before.
/// </summary>
public sealed partial class ProceduralWorld
{
    [Header("Sculpted Terrain")]
    [Tooltip("Saved, hand-sculpted terrain. When set, the world is built from it and the generation settings above are ignored.")]
    [SerializeField] private TerrainSculptData sculptData;

    private sealed class BaseCache { public float[] values; }

    private readonly Dictionary<Vector3Int, float> sculpt = new Dictionary<Vector3Int, float>();
    private readonly HashSet<Vector3Int> sculptChunks = new HashSet<Vector3Int>();
    // Grid column (x, z) -> lowest and highest edited sample y in it.
    private readonly Dictionary<Vector2Int, Vector2Int> sculptColumns = new Dictionary<Vector2Int, Vector2Int>();
    private readonly Dictionary<Vector3Int, BaseCache> baseCaches = new Dictionary<Vector3Int, BaseCache>();
    private readonly HashSet<Vector3Int> createdWhileMuted = new HashSet<Vector3Int>();
    private readonly HashSet<Vector3Int> strokeChunks = new HashSet<Vector3Int>();
    private Bounds strokeBounds;
    private bool muteChunkEvents;
    private int appliedRevision;

    public TerrainSculptData SculptData => sculptData;
    public bool UsingSculptedTerrain => sculptData != null && sculptData.HasLayout;
    public bool CanSculpt => LayoutReady && UsingSculptedTerrain && !Application.isPlaying;
    public int SculptedSampleCount => sculpt.Count;
    private float SculptHeadroom => UsingSculptedTerrain ? sculptData.Headroom : 0f;

    /// <summary>The current inspector settings as a frozen layout, for a new sculpted terrain.</summary>
    public string CaptureLayout(out float[] checks)
    {
        PrepareLayout();
        var captured = new DensityField(this);
        checks = CheckValues(captured);
        return JsonUtility.ToJson(captured);
    }

    /// <summary>
    /// Moves one sinkhole of the saved terrain so its shaft starts at worldPoint (x, z), then rebuilds the world.
    /// Sculpt edits are kept. Edit mode only.
    /// </summary>
    public bool MoveSinkhole(int index, Vector3 worldPoint)
    {
        if (Application.isPlaying || !UsingSculptedTerrain) return false;
        DensityField saved = JsonUtility.FromJson<DensityField>(sculptData.Layout);
        if (saved == null || saved.sinkholes == null || index < 0 || index >= saved.sinkholes.Length) return false;
        Vector3 local = worldPoint - transform.position;
        saved.sinkholes[index].position = new Vector2(local.x, local.z);
        sculptData.SetLayout(JsonUtility.ToJson(saved), CheckValues(saved));
        Generate(true);
        return true;
    }

    private bool UseSavedLayout()
    {
        if (!UsingSculptedTerrain) return false;
        DensityField saved = JsonUtility.FromJson<DensityField>(sculptData.Layout);
        if (saved == null || saved.chunkCells <= 0 || saved.cellSize <= 0f)
        {
            Debug.LogError("Sculpted terrain " + sculptData.name + " has no readable layout; using the inspector settings.", this);
            return false;
        }
        if (saved.sinkholes == null) saved.sinkholes = new Sinkhole[0];
        field = saved;
        noiseOffset = saved.noiseOffset;
        sinkholes = saved.sinkholes;

        float[] expected = sculptData.Checks;
        if (expected != null && expected.Length > 0)
        {
            float[] now = CheckValues(saved);
            for (int i = 0; i < expected.Length && i < now.Length; i++)
                if (Mathf.Abs(now[i] - expected[i]) > 1e-3f)
                {
                    Debug.LogWarning("The terrain generator code changed since " + sculptData.name +
                        " was baked, so the unsculpted parts may look different. Sculpted edits are still applied.", this);
                    break;
                }
        }
        return true;
    }

    /// <summary>Densities at fixed points, to notice later changes to the generator code.</summary>
    private static float[] CheckValues(DensityField f)
    {
        var values = new float[16];
        var random = new System.Random(12345);
        for (int i = 0; i < values.Length; i++)
        {
            float x = ((float)random.NextDouble() - 0.5f) * f.worldSize.x * 0.9f;
            float z = ((float)random.NextDouble() - 0.5f) * f.worldSize.y * 0.9f;
            float h = f.SurfaceHeight(x, z);
            float y = h + 5f - (float)random.NextDouble() * 40f;
            values[i] = f.Density(new Vector3(x, y, z), h, f.CeilingHeight(h), f.InnerGroundHeight(x, z, h));
        }
        return values;
    }

    private void LoadSculpt()
    {
        sculpt.Clear();
        sculptChunks.Clear();
        sculptColumns.Clear();
        if (!UsingSculptedTerrain || field == null) return;
        appliedRevision = sculptData.Revision;
        foreach (var pair in sculptData.Edits)
        {
            sculpt[pair.Key] = pair.Value;
            IndexSculptSample(pair.Key);
        }
    }

    private void IndexSculptSample(Vector3Int sample)
    {
        SampleOwners(sample, sculptChunks);
        var column = new Vector2Int(sample.x, sample.z);
        sculptColumns[column] = sculptColumns.TryGetValue(column, out Vector2Int range)
            ? new Vector2Int(Mathf.Min(range.x, sample.y), Mathf.Max(range.y, sample.y))
            : new Vector2Int(sample.y, sample.y);
    }

    // ---------------- Heights in sculpted columns ----------------

    /// <summary>Moves the generated column heights to the real ground where the column was sculpted.</summary>
    private void SculptedColumn(Vector3 local, ref float surface, ref float ceiling, ref float ground)
    {
        if (!LayoutReady) return;
        float cell = CellSize;
        int gx = Mathf.FloorToInt((local.x - gridMin.x) / cell), gz = Mathf.FloorToInt((local.z - gridMin.z) / cell);
        int low = int.MaxValue, high = int.MinValue;
        for (int dz = 0; dz < 2; dz++) for (int dx = 0; dx < 2; dx++)
            if (sculptColumns.TryGetValue(new Vector2Int(gx + dx, gz + dz), out Vector2Int range))
            { low = Mathf.Min(low, range.x); high = Mathf.Max(high, range.y); }
        if (high == int.MinValue) return;

        float bottom = gridMin.y + cell;
        float top = gridMin.y + (high + 1) * cell;
        float lowest = gridMin.y + (low - 1) * cell;
        // Surface: the first rock going down from above the highest edit.
        if (!Crossing(local, Mathf.Max(surface, top) + cell, bottom, true, out float found)) return;
        surface = found;
        // The crust underside and inner ground only move when edits reach down to them.
        if (lowest > ceiling + cell * 2f) return;
        if (!Crossing(local, surface - cell * 0.5f, bottom, false, out float air)) return;
        ceiling = air;
        if (Crossing(local, air - cell * 0.5f, bottom, true, out float floor)) ground = floor;
    }

    /// <summary>Walks down a column to the first point that is rock (solid) or air (!solid).</summary>
    private bool Crossing(Vector3 local, float from, float to, bool solid, out float y)
    {
        float step = CellSize * 0.5f;
        for (float at = from; at >= to; at -= step)
        {
            if (FieldDensity(new Vector3(local.x, at, local.z)) > 0f != solid) continue;
            if (at == from) { y = at; return true; }
            float above = at + step, below = at;
            for (int i = 0; i < 6; i++)
            {
                float middle = (above + below) * 0.5f;
                if (FieldDensity(new Vector3(local.x, middle, local.z)) > 0f == solid) below = middle;
                else above = middle;
            }
            y = (above + below) * 0.5f;
            return true;
        }
        y = 0f;
        return false;
    }

    /// <summary>Generated plus sculpted density at a local point (no erosion), blended from the grid.</summary>
    private float FieldDensity(Vector3 local)
    {
        float cell = CellSize;
        Vector3 g = (local - gridMin) / cell;
        Vector3Int lo = Vector3Int.FloorToInt(g);
        Vector3 f = g - lo;
        float value = 0f;
        for (int z = 0; z < 2; z++) for (int x = 0; x < 2; x++)
        {
            Vector3 column = gridMin + new Vector3(lo.x + x, 0f, lo.z + z) * cell;
            float h = field.SurfaceHeight(column.x, column.z);
            float ceiling = field.CeilingHeight(h), floor = field.InnerGroundHeight(column.x, column.z, h);
            float across = (x == 0 ? 1f - f.x : f.x) * (z == 0 ? 1f - f.z : f.z);
            for (int y = 0; y < 2; y++)
            {
                var key = new Vector3Int(lo.x + x, lo.y + y, lo.z + z);
                float d = field.Density(gridMin + (Vector3)key * cell, h, ceiling, floor);
                if (sculpt.TryGetValue(key, out float carved)) d += carved;
                value += d * across * (y == 0 ? 1f - f.y : f.y);
            }
        }
        return value;
    }

    // ---------------- Edit mode brushes ----------------

    /// <summary>Starts a brush stroke: decorations and listeners are told about the changes once, at the end.</summary>
    public void BeginSculptStroke()
    {
        muteChunkEvents = true;
        strokeChunks.Clear();
        createdWhileMuted.Clear();
        strokeBounds = new Bounds(transform.position, Vector3.zero);
    }

    public void EndSculptStroke()
    {
        muteChunkEvents = false;
        foreach (Vector3Int coord in strokeChunks)
        {
            if (!chunks.TryGetValue(coord, out ChunkState state) || state.gameObject == null) continue;
            if (createdWhileMuted.Contains(coord)) ChunkCreated?.Invoke(state.gameObject, state.mesh);
            else ChunkUpdated?.Invoke(state.gameObject, state.mesh, strokeBounds);
        }
        if (strokeChunks.Count > 0)
        {
            TerrainChanged?.Invoke(strokeBounds);
            AnyTerrainChanged?.Invoke(this, strokeBounds);
        }
        createdWhileMuted.Clear();
        strokeChunks.Clear();
    }

    /// <summary>
    /// One dab of a brush at a world point (edit mode only). Raise / Lower move the ground up or down inside a
    /// vertical cylinder, Add / Carve add or remove rock in a ball (overhangs, tunnels), Smooth evens out bumps and
    /// Flatten pulls the ground towards flattenHeight. amount is metres for Raise, Lower, Add and Carve,
    /// and a 0..1 blend for Smooth and Flatten. Returns false when nothing changed.
    /// </summary>
    public bool Sculpt(TerrainSculptTool tool, Vector3 worldPoint, float radius, float amount, float flattenHeight)
    {
        if (!CanSculpt || radius <= 0f || amount <= 0f) return false;
        float cell = CellSize;
        Vector3 centre = transform.InverseTransformPoint(worldPoint);
        bool cylinder = tool == TerrainSculptTool.Raise || tool == TerrainSculptTool.Lower || tool == TerrainSculptTool.Flatten;
        float middleY = tool == TerrainSculptTool.Flatten ? flattenHeight - transform.position.y : centre.y;
        var middle = new Vector3(centre.x, middleY, centre.z);
        Vector3 extent = Vector3.one * radius;
        Vector3Int min = Vector3Int.Max(Vector3Int.FloorToInt((middle - extent - gridMin) / cell), Vector3Int.zero);
        Vector3Int max = Vector3Int.Min(Vector3Int.CeilToInt((middle + extent - gridMin) / cell), chunkCount * GridChunkCells);
        if (min.x > max.x || min.y > max.y || min.z > max.z) return false;

        // Current densities with a one-sample border, so Smooth can read neighbours.
        Vector3Int size = max - min + Vector3Int.one * 3;
        var current = new float[size.x * size.y * size.z];
        int Index(int x, int y, int z) => ((y - min.y + 1) * size.z + (z - min.z + 1)) * size.x + (x - min.x + 1);
        for (int z = min.z - 1; z <= max.z + 1; z++) for (int x = min.x - 1; x <= max.x + 1; x++)
        {
            Vector3 column = gridMin + new Vector3(x, 0f, z) * cell;
            float h = field.SurfaceHeight(column.x, column.z);
            float ceiling = field.CeilingHeight(h), floor = field.InnerGroundHeight(column.x, column.z, h);
            for (int y = min.y - 1; y <= max.y + 1; y++)
            {
                var key = new Vector3Int(x, y, z);
                float d = field.Density(gridMin + (Vector3)key * cell, h, ceiling, floor);
                if (sculpt.TryGetValue(key, out float carved)) d += carved;
                current[Index(x, y, z)] = d;
            }
        }

        // Brushes never push a sample further than this from zero, so repeated dabs do not pile up hidden rock.
        float limit = Mathf.Max(radius, cell * 4f);
        var changed = new List<Vector3Int>();
        for (int y = min.y; y <= max.y; y++) for (int z = min.z; z <= max.z; z++) for (int x = min.x; x <= max.x; x++)
        {
            var key = new Vector3Int(x, y, z);
            Vector3 p = gridMin + (Vector3)key * cell;
            Vector3 offset = p - middle;
            float t = (cylinder ? new Vector2(offset.x, offset.z).magnitude : offset.magnitude) / radius;
            if (t >= 1f) continue;
            float weight = (1f - t * t) * (1f - t * t);
            if (cylinder) weight *= 1f - Mathf.Pow(Mathf.Abs(offset.y) / radius, 4f);
            if (weight <= 0f) continue;

            float now = current[Index(x, y, z)], next = now;
            switch (tool)
            {
                case TerrainSculptTool.Raise:
                case TerrainSculptTool.Add:
                    next = now + amount * weight;
                    break;
                case TerrainSculptTool.Lower:
                case TerrainSculptTool.Carve:
                    next = now - amount * weight;
                    break;
                case TerrainSculptTool.Smooth:
                    float average = (now * 2f + current[Index(x - 1, y, z)] + current[Index(x + 1, y, z)] + current[Index(x, y - 1, z)] +
                        current[Index(x, y + 1, z)] + current[Index(x, y, z - 1)] + current[Index(x, y, z + 1)]) / 8f;
                    next = Mathf.Lerp(now, average, Mathf.Clamp01(amount * weight));
                    break;
                case TerrainSculptTool.Flatten:
                    next = Mathf.Lerp(now, middleY - p.y, Mathf.Clamp01(amount * weight));
                    break;
            }
            if (next > limit && next > now) next = Mathf.Max(now, limit);
            if (next < -limit && next < now) next = Mathf.Min(now, -limit);
            if (Mathf.Abs(next - now) < 1e-5f) continue;

            sculpt.TryGetValue(key, out float old);
            float delta = old + next - now;
            if (Mathf.Abs(delta) < 1e-4f) sculpt.Remove(key);
            else sculpt[key] = delta;
            sculptData.SetEdit(key, delta);
            IndexSculptSample(key);
            changed.Add(key);
        }
        appliedRevision = sculptData.Revision;
        if (changed.Count == 0) return false;

        var coords = new HashSet<Vector3Int>();
        foreach (Vector3Int key in changed) SampleOwners(key, coords);
        RebuildNow(coords, new Bounds(transform.TransformPoint(middle), extent * 2f + Vector3.one * cell * 4f));
        return true;
    }

    /// <summary>
    /// Authors a bounded density shape into the saved terrain. The modifier receives a world point and
    /// its current density (positive is rock), and returns the desired density. Existing edits outside
    /// the bounds are preserved. Call Generate(true) before editor authoring after a domain reload.
    /// </summary>
    public bool SculptDensity(Bounds worldBounds, System.Func<Vector3, float, float> modifier)
    {
        if (!CanSculpt || modifier == null || worldBounds.size.sqrMagnitude <= 0f) return false;
        Bounds localBounds = new Bounds(transform.InverseTransformPoint(worldBounds.center), Vector3.zero);
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 point = worldBounds.center + Vector3.Scale(worldBounds.extents,
                new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
            localBounds.Encapsulate(transform.InverseTransformPoint(point));
        }
        float cell = CellSize;
        Vector3Int min = Vector3Int.Max(Vector3Int.FloorToInt((localBounds.min - gridMin) / cell), Vector3Int.zero);
        Vector3Int max = Vector3Int.Min(Vector3Int.CeilToInt((localBounds.max - gridMin) / cell), chunkCount * GridChunkCells);
        var affected = new HashSet<Vector3Int>();
        bool changed = false;
        for (int z = min.z; z <= max.z; z++) for (int x = min.x; x <= max.x; x++)
        {
            Vector3 column = gridMin + new Vector3(x, 0, z) * cell;
            float height = field.SurfaceHeight(column.x, column.z);
            float ceiling = field.CeilingHeight(height), floor = field.InnerGroundHeight(column.x, column.z, height);
            for (int y = min.y; y <= max.y; y++)
            {
                var key = new Vector3Int(x, y, z);
                Vector3 point = gridMin + (Vector3)key * cell;
                Vector3 worldPoint = transform.TransformPoint(point);
                if (!worldBounds.Contains(worldPoint)) continue;
                float baseline = field.Density(point, height, ceiling, floor);
                sculpt.TryGetValue(key, out float previous);
                float next = modifier(worldPoint, baseline + previous);
                if (float.IsNaN(next) || float.IsInfinity(next)) continue;
                float delta = next - baseline;
                if (Mathf.Abs(delta - previous) < 1e-4f) continue;
                if (Mathf.Abs(delta) < 1e-4f) sculpt.Remove(key); else sculpt[key] = delta;
                sculptData.SetEdit(key, delta);
                IndexSculptSample(key);
                SampleOwners(key, affected);
                changed = true;
            }
        }
        appliedRevision = sculptData.Revision;
        if (changed) RebuildNow(affected, worldBounds);
        return changed;
    }

    /// <summary>Re-reads the sculpt asset (after undo, redo or a clear) and rebuilds only the chunks that differ.</summary>
    public void SyncSculptFromData()
    {
        if (!CanSculpt || sculptData.Revision == appliedRevision) return;
        appliedRevision = sculptData.Revision;
        IReadOnlyDictionary<Vector3Int, float> saved = sculptData.Edits;
        var changed = new List<Vector3Int>();
        foreach (var pair in sculpt)
            if (!saved.TryGetValue(pair.Key, out float value) || value != pair.Value) changed.Add(pair.Key);
        foreach (var pair in saved)
            if (!sculpt.ContainsKey(pair.Key)) changed.Add(pair.Key);
        if (changed.Count == 0) return;

        // sculptChunks is kept as is: chunks that lost their edits simply copy nothing.
        sculpt.Clear();
        sculptColumns.Clear();
        foreach (var pair in saved)
        {
            sculpt[pair.Key] = pair.Value;
            IndexSculptSample(pair.Key);
        }
        var coords = new HashSet<Vector3Int>();
        Bounds bounds = new Bounds(transform.TransformPoint(gridMin + (Vector3)changed[0] * CellSize), Vector3.zero);
        foreach (Vector3Int key in changed)
        {
            SampleOwners(key, coords);
            bounds.Encapsulate(transform.TransformPoint(gridMin + (Vector3)key * CellSize));
        }
        bounds.Expand(CellSize * 4f);
        RebuildNow(coords, bounds);
    }

    /// <summary>Rebuilds chunks right away on the calling thread plus workers (edit mode).</summary>
    private void RebuildNow(HashSet<Vector3Int> coords, Bounds changedBounds)
    {
        bool ownStroke = !muteChunkEvents;
        if (ownStroke) BeginSculptStroke();
        var list = new List<Vector3Int>(coords);
        var snapshots = new Dictionary<Vector3Int, float>[list.Count];
        var caches = new BaseCache[list.Count];
        var revisions = new int[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            snapshots[i] = CopyModifications(list[i]);
            if (!baseCaches.TryGetValue(list[i], out caches[i])) baseCaches[list[i]] = caches[i] = new BaseCache();
            revisions[i] = chunks[list[i]].revision;
        }
        var results = new ChunkResult[list.Count];
        DensityField frozen = field;
        Vector3 origin = gridMin;
        int generation = epoch;
        Parallel.For(0, list.Count, i =>
            results[i] = BuildChunk(frozen, origin, list[i], generation, revisions[i], snapshots[i], CancellationToken.None, caches[i]));

        foreach (ChunkResult result in results)
        {
            if (result.vertices.Length > 0)
            {
                result.mesh = MakeMesh(result);
                Physics.BakeMesh(result.mesh.GetEntityId(), false, Cooking);
            }
            result.baked = true;
            Apply(result, true, changedBounds);
        }
        Physics.SyncTransforms();

        strokeChunks.UnionWith(list);
        if (strokeBounds.size == Vector3.zero) strokeBounds = changedBounds;
        else strokeBounds.Encapsulate(changedBounds);
        if (ownStroke) EndSculptStroke();
    }
}
