using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drops for ceiling water (HangingLake, MirrorPool). At each tip a drop swells, necks, lets go and falls; where it hits
/// rock a thin ring opens. Drops are small bodies of water (shader Capstone/Cave/HangingWater).
/// </summary>
public sealed class WaterDrips : MonoBehaviour
{
    private sealed class Tip
    {
        public Vector3 point;
        public float timer, period, sinceDrop = 100f;
        public Transform forming;
    }

    private struct Falling { public Transform t; public Vector3 velocity; }
    private struct Ring { public LineRenderer line; public float age; }

    private static readonly Color RingColor = new Color(0.45f, 1.25f, 1.5f);
    private readonly List<Tip> tips = new List<Tip>();
    private readonly List<Falling> falling = new List<Falling>();
    private readonly List<Ring> rings = new List<Ring>();
    private readonly Stack<Transform> spare = new Stack<Transform>();
    private Material dropMaterial, ringMaterial;
    private Mesh sphere;
    private int mask;

    public int TipCount => tips.Count;
    public Vector3 TipPoint(int i) => tips[i].point;
    /// <summary>Seconds since this tip last let a drop go.</summary>
    public float SinceDrop(int i) => tips[i].sinceDrop;

    public void Setup(IEnumerable<Vector3> points, int terrainMask)
    {
        mask = terrainMask;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere = tmp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmp);
        dropMaterial = new Material(Shader.Find("Capstone/Cave/HangingWater")) { name = "Water drop" };
        dropMaterial.SetFloat("_Solid", 1f);
        ringMaterial = new Material(Shader.Find("Capstone/Cave/GlowLine")) { name = "Drop ring" };
        foreach (Vector3 p in points)
        {
            float period = Random.Range(1.6f, 3.6f);
            tips.Add(new Tip { point = p, period = period, timer = Random.Range(0f, period) });
        }
    }

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
        foreach (Tip p in tips)
        {
            p.timer += dt;
            p.sinceDrop += dt;
            if (p.forming == null) p.forming = NewDrop();
            float t = Mathf.Clamp01(p.timer / p.period);
            // It swells at the tip, then necks and lengthens just before it lets go.
            float neck = Mathf.SmoothStep(0f, 1f, (t - 0.72f) / 0.28f);
            float wide = Mathf.Lerp(0.03f, 0.12f, Mathf.Sqrt(t)) * (1f - neck * 0.2f);
            float tall = wide * (1.15f + neck * 1.5f);
            p.forming.localScale = new Vector3(wide, tall, wide);
            p.forming.position = p.point + Vector3.down * (tall * 0.42f + neck * 0.08f);
            if (t < 1f) continue;
            falling.Add(new Falling { t = p.forming, velocity = Vector3.down * 0.25f });
            p.forming = null;
            p.timer = 0f;
            p.sinceDrop = 0f;
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
