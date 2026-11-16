using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws a group of droplets as one liquid surface (metaballs), so beads that come close first
/// reach out to each other with a thin neck, then flow together into one body, and stretch and
/// tear apart again as they part. Rebuilt every frame with Marching Cubes in Burst jobs, only in
/// small bricks around the droplets (never across empty air), one brick per worker in parallel.
///
/// Field: each droplet adds (1 - d^2/R^2)^3, divided by its value at the droplet's radius, with
/// R = radius * Influence; the surface is where the sum reaches 1. So a lone droplet's surface sits
/// at its radius, and two droplets bridge once closer than about 1.15 R. A moving droplet's field is
/// stretched along its velocity (a teardrop). Normals are the field's gradient, so the surface is
/// smooth even on a coarse grid. Vertex colour red carries the droplets' glyph glow, blended.
/// </summary>
public sealed class DropletSurface : System.IDisposable
{
    private const int BrickCells = 6;
    private readonly Mesh mesh;
    private NativeArray<int> edgeTable, triTable;
    private NativeList<float3> vertices, normals;
    private NativeList<Color32> colours;
    private NativeList<int> indices;
    private NativeArray<float4> drops;
    private NativeArray<float4> stretches;
    private NativeArray<float> glows;
    private NativeParallelHashMap<int3, int> brickIndex;
    private NativeList<int3> bricks;
    private NativeParallelMultiHashMap<int, int> members;

    public Mesh Mesh => mesh;
    public int VertexCount => vertices.IsCreated ? vertices.Length : 0;

    private struct Vertex
    {
        public float3 position, normal;
        public float glow;
    }

    public DropletSurface()
    {
        mesh = new Mesh { name = "Droplet surface", indexFormat = IndexFormat.UInt32 };
        mesh.MarkDynamic();
        edgeTable = new NativeArray<int>(MarchingCubesTables.EdgeTable, Allocator.Persistent);
        triTable = new NativeArray<int>(256 * 16, Allocator.Persistent);
        for (int c = 0; c < 256; c++)
            for (int i = 0; i < 16; i++) triTable[c * 16 + i] = MarchingCubesTables.TriTable[c, i];
        vertices = new NativeList<float3>(8192, Allocator.Persistent);
        normals = new NativeList<float3>(8192, Allocator.Persistent);
        colours = new NativeList<Color32>(8192, Allocator.Persistent);
        indices = new NativeList<int>(8192, Allocator.Persistent);
        brickIndex = new NativeParallelHashMap<int3, int>(4096, Allocator.Persistent);
        bricks = new NativeList<int3>(4096, Allocator.Persistent);
        members = new NativeParallelMultiHashMap<int, int>(16384, Allocator.Persistent);
    }

    /// <summary>
    /// Rebuilds the surface. positions/radii/velocities/glow are per droplet; influence is R / radius
    /// (larger = stickier, bridges from further apart); cellSize is the grid step in metres.
    /// </summary>
    public void Rebuild(Vector3[] positions, float[] radii, Vector3[] velocities, float[] glow, int count,
        float influence, float cellSize, float stretchPerSpeed)
    {
        if (!drops.IsCreated || drops.Length < count)
        {
            if (drops.IsCreated) { drops.Dispose(); stretches.Dispose(); glows.Dispose(); }
            int size = Mathf.Max(16, count);
            drops = new NativeArray<float4>(size, Allocator.Persistent);
            stretches = new NativeArray<float4>(size, Allocator.Persistent);
            glows = new NativeArray<float>(size, Allocator.Persistent);
        }
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        for (int i = 0; i < count; i++)
        {
            Vector3 v = velocities[i];
            float speed = v.magnitude;
            float s = 1f + Mathf.Min(0.6f, speed * stretchPerSpeed);
            Vector3 axis = speed > 1e-4f ? v / speed : Vector3.up;
            drops[i] = new float4(positions[i].x, positions[i].y, positions[i].z, radii[i]);
            // w: how much of the along-axis distance is forgiven (0 = round).
            stretches[i] = new float4(axis.x, axis.y, axis.z, 1f - 1f / (s * s));
            glows[i] = glow[i];
            float reach = radii[i] * influence * s;
            min = Vector3.Min(min, positions[i] - Vector3.one * reach);
            max = Vector3.Max(max, positions[i] + Vector3.one * reach);
        }

        new BrickJob
        {
            drops = drops, stretches = stretches, count = count, influence = influence, brickSize = cellSize * BrickCells,
            brickIndex = brickIndex, bricks = bricks, members = members,
        }.Run();

        var stream = new NativeStream(math.max(1, bricks.Length), Allocator.TempJob);
        new MarchJob
        {
            drops = drops, stretches = stretches, glows = glows, influence = influence, cell = cellSize,
            edgeTable = edgeTable, triTable = triTable, bricks = bricks.AsArray(), members = members,
            output = stream.AsWriter(),
        }.Schedule(bricks.Length, 1).Complete();
        new GatherJob
        {
            input = stream.AsReader(), brickCount = bricks.Length,
            vertices = vertices, normals = normals, colours = colours, indices = indices,
        }.Run();
        stream.Dispose();

        mesh.Clear();
        if (count == 0 || vertices.Length == 0) return;
        mesh.SetVertices(vertices.AsArray());
        mesh.SetNormals(normals.AsArray());
        mesh.SetColors(colours.AsArray());
        mesh.SetIndices(indices.AsArray(), MeshTopology.Triangles, 0, false);
        var bounds = new Bounds();
        bounds.SetMinMax(min, max);
        mesh.bounds = bounds;
    }

    public void Dispose()
    {
        if (edgeTable.IsCreated) edgeTable.Dispose();
        if (triTable.IsCreated) triTable.Dispose();
        if (vertices.IsCreated) vertices.Dispose();
        if (normals.IsCreated) normals.Dispose();
        if (colours.IsCreated) colours.Dispose();
        if (indices.IsCreated) indices.Dispose();
        if (brickIndex.IsCreated) brickIndex.Dispose();
        if (bricks.IsCreated) bricks.Dispose();
        if (members.IsCreated) members.Dispose();
        if (drops.IsCreated) { drops.Dispose(); stretches.Dispose(); glows.Dispose(); }
        if (mesh != null) Object.Destroy(mesh);
    }

    /// <summary>Lists the bricks any droplet reaches into, and which droplets reach each brick.</summary>
    [BurstCompile(CompileSynchronously = true)]
    private struct BrickJob : IJob
    {
        [ReadOnly] public NativeArray<float4> drops;
        [ReadOnly] public NativeArray<float4> stretches;
        public int count;
        public float influence, brickSize;
        public NativeParallelHashMap<int3, int> brickIndex;
        public NativeList<int3> bricks;
        public NativeParallelMultiHashMap<int, int> members;

        public void Execute()
        {
            brickIndex.Clear();
            bricks.Clear();
            members.Clear();
            for (int i = 0; i < count; i++)
            {
                float4 d = drops[i];
                if (d.w < 1e-4f || !math.all(math.isfinite(d.xyz))) continue;
                float reach = d.w * influence / math.sqrt(math.max(1e-4f, 1f - stretches[i].w));
                int3 lo = (int3)math.floor((d.xyz - reach) / brickSize);
                int3 hi = (int3)math.floor((d.xyz + reach) / brickSize);
                // A broken position must never turn into millions of bricks.
                if (math.any(hi - lo > 8)) continue;
                for (int x = lo.x; x <= hi.x; x++)
                for (int y = lo.y; y <= hi.y; y++)
                for (int z = lo.z; z <= hi.z; z++)
                {
                    var key = new int3(x, y, z);
                    if (!brickIndex.TryGetValue(key, out int index))
                    {
                        index = bricks.Length;
                        brickIndex.Add(key, index);
                        bricks.Add(key);
                    }
                    members.Add(index, i);
                }
            }
        }
    }

    /// <summary>Samples one brick's field and marches its cubes; each brick writes its own vertex run.</summary>
    [BurstCompile(CompileSynchronously = true)]
    private struct MarchJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float4> drops;
        [ReadOnly] public NativeArray<float4> stretches;
        [ReadOnly] public NativeArray<float> glows;
        [ReadOnly] public NativeArray<int> edgeTable;
        [ReadOnly] public NativeArray<int> triTable;
        [ReadOnly] public NativeArray<int3> bricks;
        [ReadOnly] public NativeParallelMultiHashMap<int, int> members;
        public float influence, cell;
        public NativeStream.Writer output;

        public void Execute(int b)
        {
            output.BeginForEachIndex(b);
            const int P = BrickCells + 1;
            var local = new NativeList<int>(32, Allocator.Temp);
            if (members.TryGetFirstValue(b, out int member, out var it))
                do local.Add(member); while (members.TryGetNextValue(out member, ref it));

            float3 origin = (float3)bricks[b] * (cell * BrickCells);
            float k0 = 1f - 1f / (influence * influence);
            float lone = k0 * k0 * k0;
            // Most bricks round a lone droplet are empty air: skip them when even the closest corner of the
            // brick to every droplet could not add up to the surface.
            float3 far = origin + cell * BrickCells;
            float bound = 0f;
            for (int j = 0; j < local.Length; j++)
            {
                float4 d = drops[local[j]];
                float3 nearest = math.clamp(d.xyz, origin, far);
                float R = d.w * influence;
                // A stretched droplet reaches at most s = 1/sqrt(1 - w) times further.
                float r2 = math.lengthsq(nearest - d.xyz) * (1f - stretches[local[j]].w) / (R * R);
                if (r2 < 1f) { float u = 1f - r2; bound += u * u * u; }
            }
            if (bound < lone) { output.EndForEachIndex(); return; }
            var field = new NativeArray<float>(P * P * P, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            bool inside = false, outside = false;
            for (int z = 0; z < P; z++)
            for (int y = 0; y < P; y++)
            for (int x = 0; x < P; x++)
            {
                float v = Field(origin + new float3(x, y, z) * cell, local) / lone - 1f;
                field[(z * P + y) * P + x] = v;
                if (v > 0f) inside = true; else outside = true;
            }
            if (inside && outside) March(origin, field, local);
            output.EndForEachIndex();
        }

        private void March(float3 origin, NativeArray<float> field, NativeList<int> local)
        {
            const int P = BrickCells + 1;
            var cornerValue = new NativeArray<float>(8, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var cornerPos = new NativeArray<float3>(8, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var edgePoint = new NativeArray<float3>(12, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int z = 0; z < BrickCells; z++)
            for (int y = 0; y < BrickCells; y++)
            for (int x = 0; x < BrickCells; x++)
            {
                int cube = 0;
                for (int c = 0; c < 8; c++)
                {
                    int3 o = Corner(c);
                    float v = field[((z + o.z) * P + y + o.y) * P + x + o.x];
                    cornerValue[c] = v;
                    cornerPos[c] = origin + new float3(x + o.x, y + o.y, z + o.z) * cell;
                    if (v > 0f) cube |= 1 << c;
                }
                int edges = edgeTable[cube];
                if (edges == 0) continue;
                for (int e = 0; e < 12; e++)
                {
                    if ((edges & (1 << e)) == 0) continue;
                    int2 ends = Edge(e);
                    float a = cornerValue[ends.x], c2 = cornerValue[ends.y];
                    float t = math.abs(a - c2) < 1e-6f ? 0.5f : math.saturate(a / (a - c2));
                    edgePoint[e] = math.lerp(cornerPos[ends.x], cornerPos[ends.y], t);
                }
                for (int i = 0; i < 16 && triTable[cube * 16 + i] != -1; i += 3)
                {
                    Vertex v0 = Shade(edgePoint[triTable[cube * 16 + i]], local);
                    Vertex v1 = Shade(edgePoint[triTable[cube * 16 + i + 1]], local);
                    Vertex v2 = Shade(edgePoint[triTable[cube * 16 + i + 2]], local);
                    // Wind every triangle to face out of the liquid, whatever the table's convention.
                    if (math.dot(math.cross(v1.position - v0.position, v2.position - v0.position), v0.normal + v1.normal + v2.normal) < 0f)
                    {
                        Vertex swap = v1; v1 = v2; v2 = swap;
                    }
                    output.Write(v0); output.Write(v1); output.Write(v2);
                }
            }
        }

        private float Field(float3 p, NativeList<int> local)
        {
            float sum = 0f;
            for (int j = 0; j < local.Length; j++)
            {
                int i = local[j];
                float4 d = drops[i];
                float R = d.w * influence;
                float3 q = p - d.xyz;
                float along = math.dot(q, stretches[i].xyz);
                float r2 = (math.lengthsq(q) - stretches[i].w * along * along) / (R * R);
                if (r2 >= 1f) continue;
                float u = 1f - r2;
                sum += u * u * u;
            }
            return sum;
        }

        /// <summary>Outward normal (minus the field gradient) and the glow blended by each droplet's share.</summary>
        private Vertex Shade(float3 p, NativeList<int> local)
        {
            float3 gradient = float3.zero;
            float weight = 0f, glowSum = 0f;
            for (int j = 0; j < local.Length; j++)
            {
                int i = local[j];
                float4 d = drops[i];
                float R = d.w * influence;
                float3 q = p - d.xyz;
                float3 axis = stretches[i].xyz;
                float along = math.dot(q, axis);
                float r2 = (math.lengthsq(q) - stretches[i].w * along * along) / (R * R);
                if (r2 >= 1f) continue;
                float u = 1f - r2;
                gradient += -6f * u * u * (q - stretches[i].w * along * axis) / (R * R);
                float share = u * u * u;
                weight += share;
                glowSum += share * glows[i];
            }
            float length = math.length(gradient);
            return new Vertex
            {
                position = p,
                normal = length > 1e-6f ? -gradient / length : new float3(0f, 1f, 0f),
                glow = weight > 0f ? glowSum / weight : 0f,
            };
        }

        private static int3 Corner(int c)
        {
            switch (c)
            {
                case 0: return new int3(0, 0, 0);
                case 1: return new int3(1, 0, 0);
                case 2: return new int3(1, 0, 1);
                case 3: return new int3(0, 0, 1);
                case 4: return new int3(0, 1, 0);
                case 5: return new int3(1, 1, 0);
                case 6: return new int3(1, 1, 1);
                default: return new int3(0, 1, 1);
            }
        }

        private static int2 Edge(int e)
        {
            switch (e)
            {
                case 0: return new int2(0, 1);
                case 1: return new int2(1, 2);
                case 2: return new int2(2, 3);
                case 3: return new int2(3, 0);
                case 4: return new int2(4, 5);
                case 5: return new int2(5, 6);
                case 6: return new int2(6, 7);
                case 7: return new int2(7, 4);
                case 8: return new int2(0, 4);
                case 9: return new int2(1, 5);
                case 10: return new int2(2, 6);
                default: return new int2(3, 7);
            }
        }
    }

    /// <summary>Joins the bricks' vertex runs into the mesh buffers.</summary>
    [BurstCompile(CompileSynchronously = true)]
    private struct GatherJob : IJob
    {
        public NativeStream.Reader input;
        public int brickCount;
        public NativeList<float3> vertices, normals;
        public NativeList<Color32> colours;
        public NativeList<int> indices;

        public void Execute()
        {
            vertices.Clear(); normals.Clear(); colours.Clear(); indices.Clear();
            for (int b = 0; b < brickCount; b++)
            {
                int n = input.BeginForEachIndex(b);
                for (int k = 0; k < n; k++)
                {
                    Vertex v = input.Read<Vertex>();
                    indices.Add(vertices.Length);
                    vertices.Add(v.position);
                    normals.Add(v.normal);
                    byte g = (byte)math.clamp(v.glow * 255f, 0f, 255f);
                    colours.Add(new Color32(g, g, g, 255));
                }
                input.EndForEachIndex();
            }
        }
    }
}
