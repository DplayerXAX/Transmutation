using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Seeded, mesh-based slime-mould silk forest. All dimensions are metres.</summary>
[DisallowMultipleComponent]
public sealed class SilkForestManager : MonoBehaviour
{
    [Header("Forest / 森林")]
    public int seed = 73421;
    public Vector2 forestSize = new Vector2(32, 28);
    [Tooltip("Trees per 100 square metres / 每100平方米的树数")]
    [Range(0, 12)] public float treeDensity = 3.2f;
    [Min(1)] public int maximumTrees = 180;
    [Min(0.1f)] public float minimumSpacing = 3.2f;
    [Tooltip("Keep a walkable corridor through the centre / 中央通道宽度")]
    [Min(0)] public float clearingWidth = 2.4f;
    public bool generateOnStart = true;
    public bool trunkColliders = true;
    [Tooltip("Optional terrain projection. Only these layers are raycast before generation.")]
    public bool projectToGround;
    public LayerMask groundLayers = 1;
    [Min(1)] public float groundProbeHeight = 100;

    [Header("Tree ranges / 树的随机范围")]
    [Tooltip("Min/max stalk height to the upper crown attachment. Overall height also includes the crown. / X最小、Y最大；树冠另计高度")]
    public Vector2 heightRange = new Vector2(9, 16);
    public Vector2Int crownCountRange = new Vector2Int(1, 3);
    [Tooltip("Crown radius, not diameter / 树冠半径（米）")]
    public Vector2 crownRadiusRange = new Vector2(1.15f, 2.05f);
    public Vector2Int trunkCrownCountRange = new Vector2Int(0, 2);
    public Vector2 trunkCrownRadiusRange = new Vector2(0.35f, 0.8f);
    [Tooltip("Radius of the entire strand bundle / 整束树干半径")]
    public Vector2 trunkRadiusRange = new Vector2(0.065f, 0.13f);
    [Range(0, 0.6f)] public float crownAsymmetry = 0.22f;

    [Header("Silk / 蛛丝与黏膜")]
    [Range(0, 1)] public float tornWebAmount = 0.32f;
    [Range(0, 1)] public float membraneCoverage = 0.2f;
    [Range(0.02f, 0.4f)] public float crownWebThickness = 0.12f;
    [Range(4, 14)] public int trunkStrands = 8;
    public Material silkMaterial;
    public Material membraneMaterial;

    [Header("Canopy connections / 树冠间的蛛丝藤蔓")]
    [Range(0, 1)] public float vineDensity = 0.48f;
    [Min(0)] public float maximumVineDistance = 11;
    [Range(1, 4)] public int maximumVinesPerTree = 2;
    public Vector2 vineSagRange = new Vector2(0.8f, 2.6f);
    [Range(0.02f, 0.65f)] public float vineWebWidth = 0.24f;

    [SerializeField, HideInInspector] private Transform generatedRoot;
    [SerializeField, HideInInspector] private int generatedTreeCount;
    [SerializeField, HideInInspector] private int generatedVineCount;
    [SerializeField, HideInInspector] private int generatedCrownCount;
    public int GeneratedTreeCount => generatedTreeCount;
    public int GeneratedVineCount => generatedVineCount;
    public int GeneratedCrownCount => generatedCrownCount;
    public Transform GeneratedRoot => generatedRoot;

    private struct Crown { public Vector3 centre; public float radius; }
    private sealed class Tree { public Vector3 foot; public float height; public readonly List<Crown> crowns = new List<Crown>(); }
    private System.Random random;
    private readonly List<Mesh> ownedMeshes = new List<Mesh>();
    private readonly List<Material> ownedMaterials = new List<Material>();
    private float R(float lo, float hi) => Mathf.Lerp(lo, hi, (float)random.NextDouble());
    private float R(Vector2 range) => R(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
    private int Count(Vector2Int range, int minimum, int maximum)
    {
        int lo = Mathf.Clamp(Mathf.Min(range.x, range.y), minimum, maximum);
        return random.Next(lo, Mathf.Clamp(Mathf.Max(range.x, range.y), lo, maximum) + 1);
    }
    private Vector3 Direction()
    {
        float y = R(-1, 1), a = R(0, Mathf.PI * 2), s = Mathf.Sqrt(1 - y * y);
        return new Vector3(Mathf.Cos(a) * s, y, Mathf.Sin(a) * s);
    }

    private void Start() { if (generateOnStart && generatedRoot == null) Regenerate(); }

    [ContextMenu("Generate forest / 生成树林")]
    public void Regenerate()
    {
        ClearGenerated();
        random = new System.Random(seed);
        var root = new GameObject("Silk forest · generated");
        root.transform.SetParent(transform, false);
        generatedRoot = root.transform;
        EnsureMaterials();
        var trees = new List<Tree>();
        float sx = Mathf.Clamp(Mathf.Abs(forestSize.x), 1, 500);
        float sz = Mathf.Clamp(Mathf.Abs(forestSize.y), 1, 500);
        int target = Mathf.Clamp(Mathf.RoundToInt(sx * sz * Mathf.Clamp(treeDensity, 0, 12) / 100), 0, Mathf.Clamp(maximumTrees, 1, 500));
        // Bounded rejection sampling; density never overrides the minimum spacing.
        for (int attempt = 0; trees.Count < target && attempt < target * 100; attempt++)
        {
            var foot = new Vector3(R(-sx / 2, sx / 2), 0, R(-sz / 2, sz / 2));
            if (Mathf.Abs(foot.x) < Mathf.Max(0, clearingWidth) * 0.5f) continue;
            bool crowded = false;
            foreach (var existing in trees)
                if (new Vector2(existing.foot.x - foot.x, existing.foot.z - foot.z).sqrMagnitude < Mathf.Pow(Mathf.Max(0.1f, minimumSpacing), 2)) { crowded = true; break; }
            if (crowded) continue;
            if (projectToGround)
            {
                Vector3 origin = transform.TransformPoint(foot) + Vector3.up * Mathf.Max(1, groundProbeHeight);
                if (!Physics.Raycast(origin, Vector3.down, out var hit, Mathf.Max(1, groundProbeHeight) * 2, groundLayers, QueryTriggerInteraction.Ignore)) continue;
                foot = transform.InverseTransformPoint(hit.point);
            }
            var tree = new Tree { foot = foot, height = Mathf.Clamp(R(heightRange), 2, 60) };
            BuildTree(tree, trees.Count);
            trees.Add(tree);
        }
        generatedTreeCount = trees.Count;
        BuildVines(trees);
    }

    private void EnsureMaterials()
    {
        if (silkMaterial == null)
        {
            var shader = Shader.Find("Capstone/Silk Forest/Pearlescent Silk");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            silkMaterial = new Material(shader) { name = "Silk forest · transient silk" };
            ownedMaterials.Add(silkMaterial);
        }
        if (membraneMaterial == null)
        {
            var shader = Shader.Find("Capstone/Silk Forest/Thin Film");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            membraneMaterial = new Material(shader) { name = "Silk forest · transient membrane" };
            ownedMaterials.Add(membraneMaterial);
        }
    }

    private Vector3 Spine(Tree tree, float t)
    {
        return tree.foot + new Vector3(Mathf.Sin(t * 3.1f) * tree.height * 0.022f, tree.height * t, Mathf.Sin(t * 4.3f) * tree.height * 0.012f);
    }

    private void BuildTree(Tree tree, int index)
    {
        var silk = new SilkForestGeometry.MeshBuffer();
        var film = new SilkForestGeometry.MeshBuffer();
        float trunkRadius = Mathf.Clamp(R(trunkRadiusRange), 0.02f, 0.4f);
        int strands = Mathf.Clamp(trunkStrands, 4, 14);
        // Separate tapering threads, with wide buttress webs at ground level.
        for (int s = 0; s < strands; s++)
        {
            float phase = s * Mathf.PI * 2 / strands;
            var path = new Vector3[29];
            for (int k = 0; k < path.Length; k++)
            {
                float t = k / (float)(path.Length - 1);
                float spread = trunkRadius * (0.75f + 0.3f * Mathf.Sin(t * 13 + phase)) + Mathf.Pow(1 - t, 12) * R(0.45f, 0.95f);
                float a = phase + t * 3.2f;
                path[k] = Spine(tree, t) + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * spread;
            }
            silk.Tube(path, trunkRadius * R(0.12f, 0.24f), 5, new Color(0.9f, 0.96f, 1));
            var second = new Vector3[path.Length];
            for (int k = 0; k < second.Length; k++) second[k] = Spine(tree, k / (float)(second.Length - 1));
            WebStrip(silk, film, path, second, 0.58f);
        }
        silk.Tube(Curve(Spine(tree, 0), Spine(tree, 1), tree.height * 0.018f, 25), trunkRadius * 0.26f, 6, Color.white);
        int count = Count(crownCountRange, 1, 5);
        float mainRadius = Mathf.Clamp(R(crownRadiusRange), 0.2f, 5);
        for (int c = 0; c < count; c++)
        {
            float radius = c == 0 ? mainRadius : Mathf.Clamp(R(crownRadiusRange), 0.2f, 5) * R(0.55f, 0.88f);
            float a = c * 2.4f + R(-0.35f, 0.35f);
            Vector3 centre = Spine(tree, 1) + (c == 0 ? Vector3.up * radius * 0.55f : new Vector3(Mathf.Cos(a) * mainRadius * 1.02f, R(-0.55f, 0.5f) * mainRadius, Mathf.Sin(a) * mainRadius * 1.02f));
            AddCrown(silk, film, tree, centre, radius);
            AddBranch(silk, film, Spine(tree, c == 0 ? 0.84f : R(0.72f, 0.9f)), centre - Vector3.up * radius * 0.65f, trunkRadius);
        }
        int buds = Count(trunkCrownCountRange, 0, 6);
        for (int b = 0; b < buds; b++)
        {
            float t = R(0.28f, 0.74f), radius = Mathf.Clamp(R(trunkCrownRadiusRange), 0.12f, 2);
            float a = R(0, Mathf.PI * 2);
            Vector3 centre = Spine(tree, t) + new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a)) * radius * 0.9f;
            AddCrown(silk, film, tree, centre, radius);
            AddBranch(silk, film, Spine(tree, t - 0.09f), centre, trunkRadius * 0.65f);
        }
        var holder = new GameObject($"Silk tree {index + 1:00} · {count} crowns + {buds} buds");
        holder.transform.SetParent(generatedRoot, false);
        MakeMesh(holder.transform, "Silk skeleton", silk, silkMaterial);
        MakeMesh(holder.transform, "Sticky thin films", film, membraneMaterial);
        if (trunkColliders)
        {
            var collider = holder.AddComponent<CapsuleCollider>();
            collider.center = tree.foot + Vector3.up * tree.height * 0.5f;
            collider.height = tree.height;
            collider.radius = trunkRadius * 1.5f;
        }
    }

    private void AddBranch(SilkForestGeometry.MeshBuffer silk, SilkForestGeometry.MeshBuffer film, Vector3 start, Vector3 end, float width)
    {
        for (int s = 0; s < 5; s++)
        {
            float a = s * Mathf.PI * 2 / 5;
            Vector3 fan = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * width * 1.8f;
            var path = Curve(start + fan * 0.3f, end + fan, -0.12f, 13);
            silk.Tube(path, width * R(0.12f, 0.2f), 5, Color.white);
            if (s % 2 == 0) WebStrip(silk, film, path, Curve(start, end, -0.07f, 13), 0.45f);
        }
    }

    private void AddCrown(SilkForestGeometry.MeshBuffer silk, SilkForestGeometry.MeshBuffer film, Tree tree, Vector3 centre, float radius)
    {
        tree.crowns.Add(new Crown { centre = centre, radius = radius });
        generatedCrownCount++;
        SilkForestGeometry.Crown(silk, film, centre, radius, crownAsymmetry, crownWebThickness,
            tornWebAmount, membraneCoverage, random);
    }

    private Vector3[] Curve(Vector3 start, Vector3 end, float sag, int samples)
    {
        var points = new Vector3[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)(samples - 1);
            points[i] = Vector3.Lerp(start, end, t) - Vector3.up * (Mathf.Sin(t * Mathf.PI) * sag);
        }
        return points;
    }

    private void WebStrip(SilkForestGeometry.MeshBuffer silk, SilkForestGeometry.MeshBuffer film, Vector3[] a, Vector3[] b, float keep)
    {
        for (int i = 1; i < a.Length; i++)
        {
            if (R(0, 1) > keep) continue;
            // Irregular transverse threads; the missing quads are actual open holes.
            silk.Tube(new[] { a[i], b[Mathf.Max(0, i - 1)] }, R(0.003f, 0.008f), 4, Color.white);
            if (R(0, 1) < membraneCoverage)
                film.Quad(a[i - 1], a[i], b[i], b[i - 1], new Color(1, 1, 1, R(0.25f, 0.75f)));
        }
    }

    private void BuildVines(List<Tree> trees)
    {
        var silk = new SilkForestGeometry.MeshBuffer();
        var film = new SilkForestGeometry.MeshBuffer();
        var degrees = new int[trees.Count];
        // One sample per nearby pair, stable under a fixed seed, with a strict degree cap.
        for (int a = 0; a < trees.Count; a++)
        for (int b = a + 1; b < trees.Count; b++)
        {
            if (degrees[a] >= Mathf.Clamp(maximumVinesPerTree, 1, 4) || degrees[b] >= Mathf.Clamp(maximumVinesPerTree, 1, 4)) continue;
            if (Vector3.Distance(trees[a].foot, trees[b].foot) > maximumVineDistance || R(0, 1) > vineDensity) continue;
            // Prefer top crowns; occasional connection to a stem bud.
            Crown ca = trees[a].crowns[random.Next(trees[a].crowns.Count)];
            Crown cb = trees[b].crowns[random.Next(trees[b].crowns.Count)];
            Vector3 direction = (cb.centre - ca.centre).normalized;
            Vector3 start = ca.centre + direction * ca.radius * 0.85f;
            Vector3 end = cb.centre - direction * cb.radius * 0.85f;
            float distance = Vector3.Distance(start, end);
            if (distance < 0.7f) continue;
            float sag = Mathf.Min(Mathf.Max(0, R(vineSagRange)), distance * 0.32f);
            // Never sag below the local ground at either endpoint.
            sag = Mathf.Min(sag, Mathf.Max(0, Mathf.Min(start.y - trees[a].foot.y, end.y - trees[b].foot.y) - 0.9f));
            Vector3 side = Vector3.Cross(direction, Vector3.up).normalized;
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            var upper = Curve(start, end, sag, 25);
            var lower = Curve(start, end, sag + Mathf.Max(0.02f, vineWebWidth) * R(1.3f, 2.5f), 25);
            for (int s = 0; s < 4; s++)
            {
                var strand = Curve(start, end, sag + s * vineWebWidth * 0.4f, 25);
                for (int p = 1; p < strand.Length - 1; p++) strand[p] += side * Mathf.Sin(p * 0.45f + s) * vineWebWidth * 0.2f;
                silk.Tube(strand, R(0.008f, 0.018f), 5, Color.white);
            }
            WebStrip(silk, film, upper, lower, 1 - tornWebAmount);
            degrees[a]++; degrees[b]++; generatedVineCount++;
        }
        var holder = new GameObject("Canopy silk · sparse hanging connections");
        holder.transform.SetParent(generatedRoot, false);
        MakeMesh(holder.transform, "Tension strands", silk, silkMaterial);
        MakeMesh(holder.transform, "Torn hanging membranes", film, membraneMaterial);
    }

    private void MakeMesh(Transform parent, string label, SilkForestGeometry.MeshBuffer data, Material material)
    {
        if (data.vertices.Count == 0) return;
        var mesh = data.CreateMesh(label);
        ownedMeshes.Add(mesh);
        var obj = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
        obj.transform.SetParent(parent, false);
        obj.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = obj.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = material == membraneMaterial ? ShadowCastingMode.Off : ShadowCastingMode.TwoSided;
        renderer.receiveShadows = true;
    }

    [ContextMenu("Clear generated forest / 清除生成的树林")]
    public void ClearGenerated()
    {
        var meshes = new HashSet<Mesh>(ownedMeshes);
        if (generatedRoot != null)
        {
#if UNITY_EDITOR
            // Recover transient previews after a domain reload. In a player build
            // only ownedMeshes may be released; baked prefab meshes can be shared
            // by other forest instances.
            foreach (var filter in generatedRoot.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && !UnityEditor.EditorUtility.IsPersistent(filter.sharedMesh)) meshes.Add(filter.sharedMesh);
#endif
            generatedRoot.gameObject.SetActive(false);
            Release(generatedRoot.gameObject);
        }
        generatedRoot = null;
        foreach (var mesh in meshes) if (mesh != null) Release(mesh);
        ownedMeshes.Clear();
        generatedTreeCount = generatedVineCount = generatedCrownCount = 0;
    }

    private static void Release(UnityEngine.Object obj)
    {
#if UNITY_EDITOR
        // Baked meshes/materials belong to the asset database, not this instance.
        if (UnityEditor.EditorUtility.IsPersistent(obj)) return;
#endif
        if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
    }
    private void OnDestroy()
    {
        foreach (var mesh in ownedMeshes) if (mesh != null) Release(mesh);
        foreach (var mat in ownedMaterials) if (mat != null) Release(mat);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var old = Gizmos.matrix; Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.4f, 0.95f, 0.8f, 0.65f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(Mathf.Abs(forestSize.x), 0.05f, Mathf.Abs(forestSize.y)));
        Gizmos.color = new Color(0.9f, 0.8f, 0.5f, 0.5f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(clearingWidth, 0.1f, Mathf.Abs(forestSize.y)));
        Gizmos.matrix = old;
    }
#endif
}
