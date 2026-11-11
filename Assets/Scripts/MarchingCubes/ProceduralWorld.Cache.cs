using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Optional saved chunk meshes. A big authored world (Scene_World) takes over a minute to sample its density field,
/// so its finished meshes are saved to a gzipped .bytes file; Generate then only uploads meshes and cooks colliders.
/// The cache is used only while it matches the layout and the sculpt edits it was made from (and before any erosion),
/// otherwise the world is generated as before. Erosion and sculpting keep working: they re-mesh from the density field.
/// </summary>
public sealed partial class ProceduralWorld
{
    [Header("Saved Chunk Meshes")]
    [Tooltip("Pre-generated chunk meshes (made by the scene's build step). Ignored when they no longer match the terrain.")]
    [SerializeField] private TextAsset chunkCache;

    public TextAsset ChunkCache { get => chunkCache; set => chunkCache = value; }
    /// <summary>True when the last Generate used the saved chunk meshes.</summary>
    public bool UsedChunkCache { get; private set; }

    private string CacheKey => JsonUtility.ToJson(field) + "|" + (UsingSculptedTerrain ? sculptData.Revision + "/" + sculptData.Edits.Count : "none");

    /// <summary>Builds every chunk of the current layout (with sculpt edits) and returns them packed for chunkCache.</summary>
    public byte[] BuildChunkCache()
    {
        Clear();
        if (!UseSavedLayout()) { PrepareLayout(); field = new DensityField(this); ApplyLeafScale(); }
        PrepareGrid(SculptHeadroom);
        LoadSculpt();
        var coords = new List<Vector3Int>();
        for (int y = 0; y < chunkCount.y; y++) for (int z = 0; z < chunkCount.z; z++) for (int x = 0; x < chunkCount.x; x++) coords.Add(new Vector3Int(x, y, z));
        var results = new ChunkResult[coords.Count];
        var snapshots = new Dictionary<Vector3Int, float>[coords.Count];
        for (int i = 0; i < coords.Count; i++) snapshots[i] = CopyModifications(coords[i]);
        var token = CancellationToken.None;
        var f = field; Vector3 origin = gridMin;
        Parallel.For(0, coords.Count, new ParallelOptions { MaxDegreeOfParallelism = Mathf.Max(1, SystemInfo.processorCount - 1) },
            i => results[i] = BuildChunk(f, origin, coords[i], 0, 0, snapshots[i], token));
        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, System.IO.Compression.CompressionLevel.Optimal, true))
        using (var w = new BinaryWriter(gzip))
        {
            w.Write(CacheKey);
            int count = 0; foreach (var r in results) if (r.vertices.Length > 0) count++;
            w.Write(count);
            foreach (var r in results)
            {
                if (r.vertices.Length == 0) continue;
                w.Write(r.coord.x); w.Write(r.coord.y); w.Write(r.coord.z);
                w.Write(r.vertices.Length);
                foreach (var v in r.vertices) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
                foreach (var n in r.normals) { w.Write(n.x); w.Write(n.y); w.Write(n.z); }
                w.Write(r.surfaceTriangles.Length); foreach (int t in r.surfaceTriangles) w.Write(t);
                w.Write(r.innerTriangles.Length); foreach (int t in r.innerTriangles) w.Write(t);
            }
        }
        Clear();
        return memory.ToArray();
    }

    /// <summary>Called by Generate after the chunk table is laid out: applies every chunk from the cache if it matches.</summary>
    private bool LoadChunkCache()
    {
        UsedChunkCache = false;
        if (chunkCache == null || modifications.Count > 0) return false;
        var cached = new Dictionary<Vector3Int, ChunkResult>();
        try
        {
            using var gzip = new GZipStream(new MemoryStream(chunkCache.bytes), CompressionMode.Decompress);
            using var r = new BinaryReader(gzip);
            if (r.ReadString() != CacheKey) { Debug.Log("Saved chunk meshes do not match this terrain any more; generating it.", this); return false; }
            int count = r.ReadInt32();
            for (int c = 0; c < count; c++)
            {
                var result = new ChunkResult { coord = new Vector3Int(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()), epoch = epoch };
                int n = r.ReadInt32();
                result.vertices = new Vector3[n]; result.normals = new Vector3[n];
                for (int i = 0; i < n; i++) result.vertices[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                for (int i = 0; i < n; i++) result.normals[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                result.surfaceTriangles = new int[r.ReadInt32()]; for (int i = 0; i < result.surfaceTriangles.Length; i++) result.surfaceTriangles[i] = r.ReadInt32();
                result.innerTriangles = new int[r.ReadInt32()]; for (int i = 0; i < result.innerTriangles.Length; i++) result.innerTriangles[i] = r.ReadInt32();
                cached[result.coord] = result;
            }
        }
        catch (System.Exception e) { Debug.LogWarning("Could not read the saved chunk meshes; generating the terrain. " + e.Message, this); return false; }

        var meshes = new List<Mesh>();
        var all = new List<ChunkResult>();
        foreach (Vector3Int coord in chunks.Keys)
        {
            if (!cached.TryGetValue(coord, out ChunkResult result))
                result = new ChunkResult { coord = coord, epoch = epoch, vertices = new Vector3[0], normals = new Vector3[0], surfaceTriangles = new int[0], innerTriangles = new int[0] };
            if (result.vertices.Length > 0) { result.mesh = MakeMesh(result); meshes.Add(result.mesh); }
            all.Add(result);
        }
        var ids = meshes.ConvertAll(mesh => mesh.GetEntityId());
        Parallel.For(0, ids.Count, new ParallelOptions { MaxDegreeOfParallelism = Mathf.Max(1, SystemInfo.processorCount - 1) },
            i => Physics.BakeMesh(ids[i], false, Cooking));
        foreach (ChunkResult result in all) { result.baked = true; Apply(result, false, default); }
        generating = false;
        UsedChunkCache = true;
        return true;
    }
}
