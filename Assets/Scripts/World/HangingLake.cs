using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// An upside-down lake: one sheet of water draped under the cave ceiling or under a rock shelf, following the rock and
/// meeting it at an irregular rim. Water creeps across it towards a few pendants where it sags into hanging points
/// (shader Capstone/Cave/HangingLake). At each pendant a drop swells, necks, lets go and falls; where it hits rock a
/// thin ring opens. The lake lights the rock round it through a reveal light. Scenery only, no collider.
/// </summary>
public sealed class HangingLake : MonoBehaviour
{
    private const int Grid = 40;

    private sealed class Pendant
    {
        public Vector3 tip;
        public float timer, period;
        public Transform forming;
    }

    private struct Falling { public Transform t; public Vector3 velocity; }
    private struct Ring { public LineRenderer line; public float age; }

    private readonly List<Pendant> pendants = new List<Pendant>();
    private readonly List<Falling> falling = new List<Falling>();
    private readonly List<Ring> rings = new List<Ring>();
    private readonly Stack<Transform> spare = new Stack<Transform>();
    private Material water, dropMaterial, ringMaterial;
    private Mesh sphere;
    private int mask;
    private static readonly Color RingColor = new Color(0.45f, 1.25f, 1.5f);

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

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
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
                sag += 0.85f * Mathf.Exp(-d * d / 0.7f) + 0.16f * Mathf.Exp(-d * d / 8f);
            }
            float e = Mathf.SmoothStep(0f, 1f, rim[x, z]);
            float slow = 0.3f + 0.55f * Mathf.PerlinNoise(px * 0.14f + seed, pz * 0.14f);
            float y = ceiling[x, z] + 0.04f - e * (slow + sag);
            index[x, z] = vertices.Count;
            vertices.Add(new Vector3(px, y - baseY, pz));
            uvs.Add(new Vector2(nearest, e));
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
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = gameObject.AddComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        water = new Material(Shader.Find("Capstone/Cave/HangingLake")) { name = "Hanging lake" };
        r.sharedMaterial = water;

        var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere = tmp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmp);
        dropMaterial = new Material(Shader.Find("Capstone/Cave/Glow")) { name = "Hanging lake drop" };
        dropMaterial.SetColor("_Color", new Color(0.18f, 0.72f, 0.95f));
        ringMaterial = new Material(Shader.Find("Capstone/Cave/GlowLine")) { name = "Drop ring" };
        for (int i = 0; i < tips.Count; i++)
        {
            if (low[i] == float.MaxValue) continue;
            float period = Random.Range(1.6f, 3.6f);
            pendants.Add(new Pendant { tip = new Vector3(origin.x + tips[i].x, low[i], origin.z + tips[i].z), period = period, timer = Random.Range(0f, period) });
        }
        var glowAt = new GameObject("Lake light").transform;
        glowAt.SetParent(transform, false);
        glowAt.localPosition = new Vector3(0f, vertices[vertices.Count / 2].y - 0.5f, 0f);
        CaveReveal.Add(glowAt, radius * 0.8f, new Color(0.3f, 0.8f, 1f), () => 0.55f);
        return true;
    }

    private static bool Step(float[,] h, bool[,] ok, int x, int z, int dx, int dz, float step) =>
        ok[x + dx, z + dz] && Mathf.Abs(h[x + dx, z + dz] - h[x, z]) > step * 2.2f ||
        ok[x - dx, z - dz] && Mathf.Abs(h[x - dx, z - dz] - h[x, z]) > step * 2.2f;

    private Transform NewDrop()
    {
        if (spare.Count > 0) { Transform s = spare.Pop(); s.gameObject.SetActive(true); return s; }
        var go = new GameObject("Drop");
        go.transform.SetParent(transform, true);
        go.AddComponent<MeshFilter>().sharedMesh = sphere;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = dropMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        foreach (Pendant p in pendants)
        {
            p.timer += dt;
            if (p.forming == null) p.forming = NewDrop();
            float t = Mathf.Clamp01(p.timer / p.period);
            // It swells at the tip, then necks and lengthens just before it lets go.
            float neck = Mathf.SmoothStep(0f, 1f, (t - 0.72f) / 0.28f);
            float wide = Mathf.Lerp(0.03f, 0.12f, Mathf.Sqrt(t)) * (1f - neck * 0.2f);
            float tall = wide * (1.15f + neck * 1.5f);
            p.forming.localScale = new Vector3(wide, tall, wide);
            p.forming.position = p.tip + Vector3.down * (tall * 0.42f + neck * 0.08f);
            if (t < 1f) continue;
            falling.Add(new Falling { t = p.forming, velocity = Vector3.down * 0.25f });
            p.forming = null;
            p.timer = 0f;
        }
        for (int i = falling.Count - 1; i >= 0; i--)
        {
            Falling d = falling[i];
            d.velocity += Vector3.down * 9.8f * dt;
            float travel = -d.velocity.y * dt;
            d.t.localScale = new Vector3(0.1f, 0.13f + Mathf.Min(0.22f, -d.velocity.y * 0.018f), 0.1f);
            bool hit = Physics.Raycast(d.t.position, Vector3.down, out RaycastHit floor, travel + 0.05f, mask, QueryTriggerInteraction.Ignore);
            if (hit || d.t.position.y < -90f)
            {
                if (hit) AddRing(floor.point + floor.normal * 0.03f, floor.normal);
                d.t.gameObject.SetActive(false);
                spare.Push(d.t);
                falling.RemoveAt(i);
                continue;
            }
            d.t.position += d.velocity * dt;
            falling[i] = d;
        }
        for (int i = rings.Count - 1; i >= 0; i--)
        {
            Ring r = rings[i];
            r.age += dt;
            if (r.age > 1.1f) { Destroy(r.line.gameObject); rings.RemoveAt(i); continue; }
            float radius = 0.08f + r.age * 0.75f;
            for (int k = 0; k < 20; k++)
            {
                float a = k / 20f * Mathf.PI * 2f;
                r.line.SetPosition(k, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
            r.line.startColor = r.line.endColor = RingColor * (1f - r.age / 1.1f);
            rings[i] = r;
        }
    }

    private void AddRing(Vector3 at, Vector3 normal)
    {
        if (rings.Count >= 8) return;
        var go = new GameObject("Drop ring");
        go.transform.SetParent(transform, true);
        go.transform.SetPositionAndRotation(at, Quaternion.FromToRotation(Vector3.up, normal));
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = ringMaterial;
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 20;
        line.widthMultiplier = 0.02f;
        line.alignment = LineAlignment.TransformZ;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rings.Add(new Ring { line = line });
    }
}
