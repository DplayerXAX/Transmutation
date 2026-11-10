using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Floating stones over the flower field that climb in a slow spiral, each a little higher than the last (one jump
/// apart), up to a wider island at the top with a meditation altar (a resting place: sit there, then meditate).
/// Each stone is a flat-topped shard of the ground with a crystal-dark underside tapering to a point, a few growths
/// on top and veils hanging below. The positions are set by Tools > Capstone > World > Add Sky Islands Over Flower Field
/// (Editor/SkyIslandsSetup.cs); the stones are built from them in the Scene view and in Play mode (never saved).
/// </summary>
[ExecuteAlways]
public sealed class SkyIslands : MonoBehaviour
{
    [Tooltip("Stone centres (top surface), lowest first. The last one is the altar island.")]
    public Vector3[] stones = new Vector3[0];
    public float[] radii = new float[0];
    [Tooltip("True for drifting scenery rocks that are not on the way up (fewer growths).")]
    public bool[] scenery = new bool[0];
    public int seed = 3;
    [Tooltip("Ground material for the tops and sides; inner-world material for the undersides.")]
    public Material topMaterial, underMaterial, altarGlow;
    public GameObject[] topDecor = new GameObject[0];
    public GameObject[] hangingDecor = new GameObject[0];
    public GameObject[] altarStones = new GameObject[0];
    [Tooltip("Slow bob of the small stones (metres). 0 keeps them still, which is easiest to land on.")]
    [Range(0f, 0.5f)] public float bob = 0f;
    [Tooltip("Kind of resting place made at the altar (meditation is allowed once per kind).")]
    public string altarKind = "Lookout";

    private Transform built;
    private readonly List<Mesh> meshes = new List<Mesh>();
    private bool altarAdded;

    public Vector3 AltarPoint => stones.Length > 0 ? stones[stones.Length - 1] : transform.position;
    public Transform Built => built;

    private void OnEnable() { if (!ReflectionPhysics.SwitchingWorldRoots) Rebuild(); }

    private void OnDisable() { if (!ReflectionPhysics.SwitchingWorldRoots) Clear(); }

    private void OnValidate()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.EditorApplication.delayCall += () => { if (this != null && isActiveAndEnabled) Rebuild(); };
#endif
    }

    public void Rebuild()
    {
        Clear();
        if (stones == null || stones.Length == 0) return;
        built = new GameObject("Sky Islands (built)").transform;
        built.gameObject.hideFlags = HideFlags.DontSave;
        built.SetParent(transform, false);
        var random = new System.Random(seed);
        for (int i = 0; i < stones.Length; i++)
        {
            bool altar = i == stones.Length - 1;
            float r = i < radii.Length ? radii[i] : 2f;
            Transform stone = Stone(i, stones[i], r, altar, random);
            if (altar) Altar(stone, r, random);
        }
        foreach (Transform t in built.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
    }

    private void Clear()
    {
        if (built != null)
        {
            if (Application.isPlaying) Destroy(built.gameObject); else DestroyImmediate(built.gameObject);
        }
        built = null;
        foreach (Mesh m in meshes) if (m != null) { if (Application.isPlaying) Destroy(m); else DestroyImmediate(m); }
        meshes.Clear();
    }

    private void Update()
    {
        if (!Application.isPlaying || built == null) return;
        if (!altarAdded && RestSpotSystem.Instance != null)
        {
            // The altar: a resting place on the top island. Sit there (right click) and meditate.
            altarAdded = true;
            Vector3 view = AltarPoint - new Vector3(transform.position.x, AltarPoint.y, transform.position.z);
            float yaw = view.sqrMagnitude > 0.01f ? Mathf.Atan2(view.x, view.z) * Mathf.Rad2Deg : 0f;
            RestSpotSystem.Instance.AddSpot(altarKind, AltarPoint + Vector3.up * 0.25f, yaw, snap: true);
        }
        if (bob > 0f)
            for (int i = 0; i < built.childCount - 1; i++)
            {
                Transform t = built.GetChild(i);
                t.position = stones[i] + Vector3.up * (Mathf.Sin(Time.time * 0.5f + i * 1.3f) * bob);
            }
    }

    // ---------------- Stones ----------------

    private Transform Stone(int index, Vector3 top, float r, bool altar, System.Random random)
    {
        var go = new GameObject((altar ? "Altar Island" : "Sky Stone " + index)) { layer = 7 };
        go.transform.SetParent(built, false);
        go.transform.position = top;
        Mesh mesh = StoneMesh(r, altar ? 1.1f : 1.6f + (float)random.NextDouble() * 0.6f, random);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { topMaterial, underMaterial != null ? underMaterial : topMaterial };
        go.AddComponent<MeshCollider>().sharedMesh = mesh;

        // A few growths on top (not on the altar's middle), veils hanging from the point below.
        bool drifting = index < scenery.Length && scenery[index];
        int growths = altar ? 6 : drifting ? random.Next(0, 2) : random.Next(2, 5);
        for (int g = 0; g < growths && topDecor.Length > 0; g++)
        {
            float a = (float)random.NextDouble() * Mathf.PI * 2f;
            float d = r * (altar ? Mathf.Lerp(0.7f, 0.9f, (float)random.NextDouble()) : Mathf.Lerp(0.35f, 0.8f, (float)random.NextDouble()));
            Decor(topDecor[random.Next(topDecor.Length)], go.transform, top + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d,
                Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f), Mathf.Lerp(0.5f, 1f, (float)random.NextDouble()));
        }
        int veils = altar ? 4 : random.Next(1, 3);
        for (int v = 0; v < veils && hangingDecor.Length > 0; v++)
        {
            float a = (float)random.NextDouble() * Mathf.PI * 2f;
            Vector3 under = top + new Vector3(Mathf.Cos(a) * r * 0.35f, -r * 0.55f, Mathf.Sin(a) * r * 0.35f);
            Decor(hangingDecor[random.Next(hangingDecor.Length)], go.transform, under, Quaternion.Euler(180f, (float)random.NextDouble() * 360f, 0f),
                Mathf.Lerp(0.7f, 1.3f, (float)random.NextDouble()));
        }
        return go.transform;
    }

    private void Decor(GameObject prefab, Transform parent, Vector3 at, Quaternion rotation, float scale)
    {
        if (prefab == null) return;
        GameObject made = Instantiate(prefab, at, rotation, parent);
        made.transform.localScale = prefab.transform.localScale * scale;
        made.hideFlags = HideFlags.DontSave;
    }

    /// <summary>A flat top (submesh 0) on a jagged rim, and an underside tapering to a point (submesh 1).</summary>
    private Mesh StoneMesh(float r, float depthScale, System.Random random)
    {
        int sides = 11;
        var v = new List<Vector3>();
        var topTris = new List<int>();
        var underTris = new List<int>();
        float[] rim = new float[sides];
        for (int i = 0; i < sides; i++) rim[i] = r * Mathf.Lerp(0.82f, 1.08f, (float)random.NextDouble());
        Vector3 Ring(int i, float scale, float y) { float a = i * Mathf.PI * 2f / sides; return new Vector3(Mathf.Cos(a) * rim[i] * scale, y, Mathf.Sin(a) * rim[i] * scale); }
        // Top: centre + rim (slightly domed, so it drains the eye to the middle).
        v.Add(new Vector3(0f, 0.08f, 0f));
        for (int i = 0; i < sides; i++) v.Add(Ring(i, 1f, 0f));
        for (int i = 0; i < sides; i++) topTris.AddRange(new[] { 0, 1 + (i + 1) % sides, 1 + i });
        // Sides: the rim, a lip just below it, then two shrinking rings and a point.
        int lip = v.Count;
        for (int i = 0; i < sides; i++) v.Add(Ring(i, 1f, 0f));
        int lip2 = v.Count;
        for (int i = 0; i < sides; i++) v.Add(Ring(i, 0.96f, -0.35f));
        int mid = v.Count;
        float depth = r * depthScale;
        for (int i = 0; i < sides; i++) v.Add(Ring(i, Mathf.Lerp(0.55f, 0.75f, (float)random.NextDouble()), -depth * 0.45f) + new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f) * r * 0.15f);
        int low = v.Count;
        for (int i = 0; i < sides; i++) v.Add(Ring(i, Mathf.Lerp(0.2f, 0.35f, (float)random.NextDouble()), -depth * 0.8f));
        int tip = v.Count;
        v.Add(new Vector3(((float)random.NextDouble() - 0.5f) * r * 0.3f, -depth, ((float)random.NextDouble() - 0.5f) * r * 0.3f));
        void Band(int a, int b, List<int> into)
        {
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                into.AddRange(new[] { a + i, a + j, b + i, a + j, b + j, b + i });
            }
        }
        Band(lip, lip2, topTris);       // the rock edge reads as ground
        Band(lip2, mid, underTris);
        Band(mid, low, underTris);
        for (int i = 0; i < sides; i++) underTris.AddRange(new[] { low + i, low + (i + 1) % sides, tip });
        var mesh = new Mesh { name = "Sky stone", subMeshCount = 2 };
        mesh.SetVertices(v);
        mesh.SetTriangles(topTris, 0);
        mesh.SetTriangles(underTris, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        meshes.Add(mesh);
        return mesh;
    }

    // ---------------- The altar ----------------

    private void Altar(Transform island, float r, System.Random random)
    {
        Vector3 top = island.position;
        // A low round dais in the middle with a glowing ring, and tall stones standing round the edge.
        var dais = new GameObject("Altar Dais") { layer = 7 };
        dais.transform.SetParent(island, false);
        dais.transform.position = top;
        Mesh disc = Disc(r * 0.32f, 0.22f, 20);
        dais.AddComponent<MeshFilter>().sharedMesh = disc;
        dais.AddComponent<MeshRenderer>().sharedMaterial = topMaterial;
        dais.AddComponent<MeshCollider>().sharedMesh = disc;
        var ring = new GameObject("Altar Ring").AddComponent<LineRenderer>();
        ring.transform.SetParent(island, false);
        ring.useWorldSpace = true;
        ring.loop = true;
        ring.positionCount = 48;
        for (int k = 0; k < 48; k++)
        {
            float a = k / 48f * Mathf.PI * 2f;
            ring.SetPosition(k, top + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r * 0.45f + Vector3.up * 0.05f);
        }
        ring.widthMultiplier = 0.06f;
        ring.sharedMaterial = altarGlow;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        for (int k = 0; k < 5 && altarStones.Length > 0; k++)
        {
            float a = (k / 5f + 0.1f) * Mathf.PI * 2f;
            Decor(altarStones[random.Next(altarStones.Length)], island, top + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r * 0.82f,
                Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), k % 2 == 0 ? 0.6f : 0.5f);
        }
    }

    private Mesh Disc(float radius, float height, int sides)
    {
        var v = new List<Vector3> { new Vector3(0f, height, 0f) };
        var t = new List<int>();
        for (int i = 0; i < sides; i++)
        {
            float a = i * Mathf.PI * 2f / sides;
            v.Add(new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius));
            v.Add(new Vector3(Mathf.Cos(a) * radius * 1.08f, -0.05f, Mathf.Sin(a) * radius * 1.08f));
        }
        for (int i = 0; i < sides; i++)
        {
            int a = 1 + i * 2, b = 1 + ((i + 1) % sides) * 2;
            t.AddRange(new[] { 0, b, a, a, b, a + 1, b, b + 1, a + 1 });
        }
        var mesh = new Mesh { name = "Altar dais" };
        mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        meshes.Add(mesh);
        return mesh;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.8f, 1f, 0.8f);
        for (int i = 0; i < stones.Length; i++)
        {
            Gizmos.DrawWireSphere(stones[i], i < radii.Length ? radii[i] : 2f);
            if (i > 0) Gizmos.DrawLine(stones[i - 1], stones[i]);
        }
    }
}
