using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public sealed partial class ProceduralWorld
{
    private static ChunkResult BuildChunk(DensityField field, Vector3 minimum, Vector3Int coord, int epoch, int revision,
        Dictionary<Vector3Int, float> edits, CancellationToken token, BaseCache cache = null)
    {
        int cells = field.chunkCells, n = cells + 1, halo = n + 2;
        float spacing = field.cellSize;
        Vector3Int first = coord * cells;
        Vector3 origin = minimum + (Vector3)first * spacing;
        var result = new ChunkResult { coord = coord, epoch = epoch, revision = revision,
            vertices = Array.Empty<Vector3>(), normals = Array.Empty<Vector3>(), surfaceTriangles = Array.Empty<int>(), innerTriangles = Array.Empty<int>() };
        var heights = new float[halo * halo];
        var bottoms = new float[halo * halo];
        var undersides = new float[halo * halo];
        float maxTop = float.MinValue, minFloor = float.MaxValue;
        for (int z = -1; z <= n; z++) for (int x = -1; x <= n; x++)
        {
            Vector3 p = minimum + (Vector3)(first + new Vector3Int(x, 0, z)) * spacing;
            float h = field.SurfaceHeight(p.x, p.z), floor = field.InnerGroundHeight(p.x, p.z, h);
            int col = (z + 1) * halo + x + 1;
            heights[col] = h; bottoms[col] = floor; undersides[col] = field.CeilingHeight(h);
            maxTop = Mathf.Max(maxTop, h); minFloor = Mathf.Min(minFloor, floor);
        }
        float band = field.overhangAmplitude * 2.5f + 2f;
        // Preserve the original generation fast paths, but never skip edited solid chunks.
        if (!field.erosionTestCourse && edits.Count == 0 &&
            (origin.y > maxTop + band || origin.y + cells * spacing < minFloor - field.caveDepth - band)) return result;
        var values = new float[halo * halo * halo];
        // Sculpting rebuilds the same chunks many times; the generated part is computed once and reused.
        float[] cached = cache?.values;
        float[] fill = cache != null && cached == null ? new float[values.Length] : null;
        var tops = new float[n * n];
        var ceilings = new float[n * n];
        for (int z = -1; z <= n; z++) for (int x = -1; x <= n; x++)
        {
            token.ThrowIfCancellationRequested();
            Vector3 p = minimum + (Vector3)(first + new Vector3Int(x, 0, z)) * spacing;
            int column = (z + 1) * halo + x + 1;
            float h = heights[column], ceiling = undersides[column], floor = bottoms[column];
            if (x >= 0 && x < n && z >= 0 && z < n) { tops[z * n + x] = h; ceilings[z * n + x] = ceiling; }
            for (int y = -1; y <= n; y++)
            {
                var key = first + new Vector3Int(x, y, z);
                p = minimum + (Vector3)key * spacing;
                edits.TryGetValue(key, out float delta);
                int index = ((y + 1) * halo + z + 1) * halo + x + 1;
                float generated = cached != null ? cached[index] : field.Density(p, h, ceiling, floor);
                if (fill != null) fill[index] = generated;
                values[index] = generated + delta;
            }
        }
        if (fill != null) cache.values = fill;
        var density = new float[n * n * n];
        var gradients = new Vector3[density.Length];
        bool positive = false, negative = false;
        for (int y = 0; y < n; y++) for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = (y * n + z) * n + x, j = ((y + 1) * halo + z + 1) * halo + x + 1;
            float value = density[i] = values[j];
            positive |= value > 0f; negative |= value <= 0f;
            gradients[i] = new Vector3(values[j - 1] - values[j + 1], values[j - halo * halo] - values[j + halo * halo], values[j - halo] - values[j + halo]) / (spacing * 2f);
        }
        if (!positive || !negative) return result;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var surface = new List<int>();
        var inner = new List<int>();
        var lookup = new Dictionary<int, int>();
        var corners = new int[8];
        var edges = new int[12];
        for (int z = 0; z < cells; z++) for (int y = 0; y < cells; y++) for (int x = 0; x < cells; x++)
        {
            if (x == 0) token.ThrowIfCancellationRequested();
            int cube = 0;
            for (int c = 0; c < 8; c++)
            { Vector3Int o = CornerOffsets[c]; int i = ((y + o.y) * n + z + o.z) * n + x + o.x; corners[c] = i; if (density[i] > 0f) cube |= 1 << c; }
            int mask = MarchingCubesTables.EdgeTable[cube];
            if (mask == 0) continue;
            for (int e = 0; e < 12; e++)
            {
                if ((mask & (1 << e)) == 0) continue;
                int a = corners[EdgeConnections[e, 0]], b = corners[EdgeConnections[e, 1]];
                int low = Mathf.Min(a, b), high = Mathf.Max(a, b);
                int axis = high - low == 1 ? 0 : high - low == n ? 1 : 2;
                int key = low * 3 + axis;
                if (!lookup.TryGetValue(key, out int vertex))
                {
                    float da = density[a], db = density[b];
                    float t = Mathf.Abs(da - db) < 1e-6f ? 0.5f : Mathf.Clamp01(da / (da - db));
                    vertex = vertices.Count;
                    // Global coordinates give both sides of a seam identical edge endpoints.
                    Vector3 pa = minimum + ((Vector3)first + SampleLocal(a, n)) * spacing;
                    Vector3 pb = minimum + ((Vector3)first + SampleLocal(b, n)) * spacing;
                    vertices.Add(Vector3.Lerp(pa, pb, t) - origin);
                    Vector3 normal = Vector3.Lerp(gradients[a], gradients[b], t).normalized;
                    normals.Add(normal.sqrMagnitude > 0f ? normal : Vector3.up);
                    lookup.Add(key, vertex);
                }
                edges[e] = vertex;
            }
            for (int i = 0; MarchingCubesTables.TriTable[cube, i] != -1; i += 3)
            {
                int a = edges[MarchingCubesTables.TriTable[cube, i]], b = edges[MarchingCubesTables.TriTable[cube, i + 1]], c = edges[MarchingCubesTables.TriTable[cube, i + 2]];
                Vector3 centre = (vertices[a] + vertices[b] + vertices[c]) / 3f;
                int col = Mathf.Clamp(Mathf.RoundToInt(centre.z / spacing), 0, n - 1) * n + Mathf.Clamp(Mathf.RoundToInt(centre.x / spacing), 0, n - 1);
                List<int> triangles = origin.y + centre.y >= (tops[col] + ceilings[col]) * 0.5f ? surface : inner;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }
        }
        result.vertices = vertices.ToArray(); result.normals = normals.ToArray();
        result.surfaceTriangles = surface.ToArray(); result.innerTriangles = inner.ToArray();
        return result;
    }
    private static Vector3 SampleLocal(int i, int n) => new Vector3(i % n, i / (n * n), (i / n) % n);
}
