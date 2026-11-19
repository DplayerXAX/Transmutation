using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An upside-down lake: clear water lying on the cave ceiling or under a rock shelf, following the rock and thinning to
/// nothing at an irregular rim. It sags into a few pendants, and drops form and fall from them (WaterDrips). The mesh
/// stores how deep the water is at each point (shader Capstone/Cave/HangingWater), and the nearest lake mirrors the
/// floor below (MirrorPool). It lights the rock round it through a reveal light. Scenery only, no collider.
/// </summary>
public sealed class HangingLake : MonoBehaviour
{
    private const int Grid = 64;

    /// <summary>On: the older look, flat toon ripple bands travelling over a thin sheet.</summary>
    public static bool ToonRipples = false;

    private Material water;
    private int mask;

    /// <summary>Drapes a lake under the rock above `below`. False if there is too little rock overhead.</summary>
    public static bool Create(Transform parent, Vector3 below, float radius, int mask, float reach = 14f)
    {
        var go = new GameObject("Hanging lake");
        go.transform.SetParent(parent, false);
        go.transform.position = below;
        var lake = go.AddComponent<HangingLake>();
        lake.mask = mask;
        if (lake.Build(radius, reach)) return true;
        Destroy(go);
        return false;
    }

    private bool Build(float radius, float reach)
    {
        float seed = Random.Range(0f, 50f), step = radius * 2f / Grid;
        int n = Grid + 1;
        var ceiling = new float[n, n];
        var rim = new float[n, n];
        var ok = new bool[n, n];
        Vector3 origin = transform.position;
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            float px = -radius + x * step, pz = -radius + z * step;
            float a = Mathf.Atan2(pz, px);
            float edge = radius * (0.72f + 0.34f * Mathf.PerlinNoise(Mathf.Cos(a) * 1.5f + seed, Mathf.Sin(a) * 1.5f + seed));
            float inside = 1f - new Vector2(px, pz).magnitude / edge;
            if (inside <= 0f) continue;
            if (!Physics.Raycast(origin + new Vector3(px, 0f, pz), Vector3.up, out RaycastHit hit, reach, mask, QueryTriggerInteraction.Ignore)) continue;
            ceiling[x, z] = hit.point.y;
            rim[x, z] = Mathf.Clamp01(inside * 3.2f);
            ok[x, z] = true;
        }
        // Rock that steps away sharply (a shaft, a shelf edge) ends the sheet there.
        for (int z = 1; z < n - 1; z++)
        for (int x = 1; x < n - 1; x++)
            if (ok[x, z] && (Step(ceiling, ok, x, z, 1, 0, step) || Step(ceiling, ok, x, z, 0, 1, step))) rim[x, z] = 0f;

        // The water thins out before it ends: nothing at the last row, then a gradual rise, so the rim is never a cut edge.
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            if (!ok[x, z]) continue;
            bool edge = x == 0 || z == 0 || x == n - 1 || z == n - 1;
            for (int dz = -1; !edge && dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++) edge |= !ok[x + dx, z + dz];
            if (edge) rim[x, z] = 0f;
        }
        for (int pass = 0; pass < 4; pass++)
            for (int z = 1; z < n - 1; z++)
            for (int x = 1; x < n - 1; x++)
                if (ok[x, z]) rim[x, z] = Mathf.Min(rim[x, z], (rim[x - 1, z] + rim[x + 1, z] + rim[x, z - 1] + rim[x, z + 1]) * 0.25f + 0.04f);

        // Pendants: well inside the sheet and apart from one another.
        var tips = new List<Vector3>();
        for (int tries = 0; tries < 200 && tips.Count < Mathf.Clamp(Mathf.RoundToInt(radius * 0.7f), 3, 9); tries++)
        {
            int x = Random.Range(2, n - 2), z = Random.Range(2, n - 2);
            if (!ok[x, z] || rim[x, z] < 0.9f) continue;
            var p = new Vector3(-radius + x * step, 0f, -radius + z * step);
            bool near = false;
            foreach (Vector3 t in tips) near |= new Vector2(t.x - p.x, t.z - p.z).magnitude < radius * 0.32f;
            if (!near) tips.Add(p);
        }
        if (tips.Count == 0) return false;

        // The water's underside is calmer than the rock it lies on.
        var smooth = (float[,])ceiling.Clone();
        for (int pass = 0; pass < 4; pass++)
        {
            var next = (float[,])smooth.Clone();
            for (int z = 1; z < n - 1; z++)
            for (int x = 1; x < n - 1; x++)
            {
                if (!ok[x, z]) continue;
                float sum = smooth[x, z] * 2f, weight = 2f;
                if (ok[x - 1, z]) { sum += smooth[x - 1, z]; weight++; }
                if (ok[x + 1, z]) { sum += smooth[x + 1, z]; weight++; }
                if (ok[x, z - 1]) { sum += smooth[x, z - 1]; weight++; }
                if (ok[x, z + 1]) { sum += smooth[x, z + 1]; weight++; }
                next[x, z] = sum / weight;
            }
            smooth = next;
        }

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var depth = new List<Vector2>();
        var triangles = new List<int>();
        var index = new int[n, n];
        var low = new float[tips.Count];
        for (int i = 0; i < low.Length; i++) low[i] = float.MaxValue;
        float baseY = origin.y;
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            index[x, z] = -1;
            if (!ok[x, z]) continue;
            float px = -radius + x * step, pz = -radius + z * step;
            float nearest = float.MaxValue, sag = 0f;
            for (int i = 0; i < tips.Count; i++)
            {
                float d = new Vector2(px - tips[i].x, pz - tips[i].z).magnitude;
                nearest = Mathf.Min(nearest, d);
                sag += 0.55f * Mathf.Exp(-d * d / 0.5f) + 0.1f * Mathf.Exp(-d * d / 6f);
            }
            float e = 1f - (1f - rim[x, z]) * (1f - rim[x, z]); // a rounded edge, like water held by its skin
            float slow = 0.16f + 0.3f * Mathf.PerlinNoise(px * 0.14f + seed, pz * 0.14f);
            // Always just under the rock, never through it.
            float y = Mathf.Min(Mathf.Lerp(ceiling[x, z], smooth[x, z], e) - e * (slow + sag), ceiling[x, z] - e * 0.12f) + 0.03f;
            index[x, z] = vertices.Count;
            vertices.Add(new Vector3(px, y - baseY, pz));
            uvs.Add(new Vector2(nearest, e));
            depth.Add(new Vector2(Mathf.Max(0f, ceiling[x, z] - y), 0f));
            for (int i = 0; i < tips.Count; i++)
                if (new Vector2(px - tips[i].x, pz - tips[i].z).sqrMagnitude < step * step && y < low[i]) low[i] = y;
        }
        for (int z = 0; z < n - 1; z++)
        for (int x = 0; x < n - 1; x++)
        {
            int a = index[x, z], b = index[x + 1, z], c = index[x, z + 1], d = index[x + 1, z + 1];
            if (a < 0 || b < 0 || c < 0 || d < 0) continue;
            triangles.Add(a); triangles.Add(c); triangles.Add(b);
            triangles.Add(b); triangles.Add(c); triangles.Add(d);
        }
        if (triangles.Count < 60) return false;
        var mesh = new Mesh { name = "Hanging lake", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, depth);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = gameObject.AddComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        water = new Material(Shader.Find(ToonRipples ? "Capstone/Cave/HangingLake" : "Capstone/Cave/HangingWater")) { name = "Hanging lake" };
        r.sharedMaterial = water;

        var points = new List<Vector3>();
        for (int i = 0; i < tips.Count; i++)
            if (low[i] != float.MaxValue) points.Add(new Vector3(origin.x + tips[i].x, low[i], origin.z + tips[i].z));
        var drips = gameObject.AddComponent<WaterDrips>();
        drips.Setup(points, mask);

        float level = 0f; int wet = 0;
        for (int i = 0; i < vertices.Count; i++)
            if (uvs[i].y > 0.5f) { level += vertices[i].y; wet++; }
        level = baseY + (wet > 0 ? level / wet : vertices[vertices.Count / 2].y);
        Vector3 centre = origin + mesh.bounds.center;
        if (!ToonRipples) MirrorPool.Attach(gameObject, r, water, level, centre, radius, drips);

        // Its light hangs in the air below: the rock round the water and the floor it mirrors come out of the dark.
        float gap = Physics.Raycast(new Vector3(centre.x, level - 0.3f, centre.z), Vector3.down, out RaycastHit floor, 20f, mask, QueryTriggerInteraction.Ignore) ? level - floor.point.y : 6f;
        var glowAt = new GameObject("Lake light").transform;
        glowAt.SetParent(transform, false);
        glowAt.position = new Vector3(centre.x, level - Mathf.Min(gap * 0.4f, 3f), centre.z);
        CaveReveal.Add(glowAt, Mathf.Max(radius * 0.9f, gap * 0.9f), new Color(0.45f, 0.82f, 1f), () => 0.55f);
        return true;
    }

    private static bool Step(float[,] h, bool[,] ok, int x, int z, int dx, int dz, float step) =>
        ok[x + dx, z + dz] && Mathf.Abs(h[x + dx, z + dz] - h[x, z]) > step * 2.2f ||
        ok[x - dx, z - dz] && Mathf.Abs(h[x - dx, z - dz] - h[x, z]) > step * 2.2f;
}
