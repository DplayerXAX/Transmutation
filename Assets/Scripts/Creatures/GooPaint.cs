using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Surface-conforming paint. Rendering and grip coverage share the clipped triangles.</summary>
public sealed class GooPaint : MonoBehaviour
{
    static readonly List<GooPaint> All = new List<GooPaint>();
    const int MaxSplats = 24;
    public const float DefaultRadius = 4f;
    [Min(0.3f)] public float radius = DefaultRadius;
    SurfaceAnchor origin;
    Vector3 impactPoint, impactNormal;
    Collider support;
    Mesh paintMesh;
    Material paintMaterial;
    Vector3[] vertices, normals;
    int[] indices;
    float[] radial;
    float spread;
    bool dirty;
    float rebuildAfter;
    Bounds localBounds;
    readonly Dictionary<Vector3Int,List<int>> cells = new Dictionary<Vector3Int,List<int>>();
    readonly HashSet<int> candidates = new HashSet<int>();
    Vector3 gridScale;
    public Vector3 Normal => support != null ? support.transform.worldToLocalMatrix.transpose.MultiplyVector(impactNormal).normalized : transform.up;
    public int TriangleCount => indices == null ? 0 : indices.Length / 3;
    public static int SupportLayerMask
    { get { int mask = 0; foreach (var p in All) if (p != null && p.support != null) mask |= 1 << p.support.gameObject.layer; return mask; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    public static GooPaint Splat(Vector3 point, Vector3 surfaceNormal, Material material,
        float size = DefaultRadius, Transform surface = null)
    {
        if (surface == null) return null;
        Collider solid = surface.GetComponent<Collider>();
        if (!SurfaceGeometry.Acquire(solid, point, surfaceNormal, out SurfaceAnchor anchor)) return null;
        var go = new GameObject("Goo Paint") { layer = 7 };
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, surface.gameObject.scene);
        Transform parent = SurfaceGeometry.IsTerrain(solid) ? solid.GetComponent<ProceduralWorldChunk>().Owner.transform : surface;
        go.transform.SetParent(parent, false);
        var paint = go.AddComponent<GooPaint>();
        paint.origin = anchor; paint.support = solid; paint.radius = size;
        paint.impactPoint = surface.InverseTransformPoint(point);
        paint.impactNormal = surface.localToWorldMatrix.transpose.MultiplyVector(surfaceNormal).normalized;
        if (!paint.Build(material)) { Destroy(go); return null; }
        All.Add(paint);
        while (All.Count > MaxSplats)
        { var oldest = All[0]; All.RemoveAt(0); if (oldest != null) Destroy(oldest.gameObject); }
        return paint;
    }

    bool Build(Material source)
    {
        var triangles = SurfaceGeometry.BuildPatch(origin, radius, GetInstanceID() & 1023);
        if (triangles.Count == 0) return false;
        var points = new List<Vector3>(triangles.Count * 3);
        var directions = new List<Vector3>(triangles.Count * 3);
        var uv = new List<Vector2>(triangles.Count * 3);
        var faces = new List<int>(triangles.Count * 3);
        origin.Resolve(out Vector3 centre, out _);
        foreach (var triangle in triangles) { Add(triangle.a); Add(triangle.b); Add(triangle.c); }
        void Add(SurfaceAnchor anchor)
        {
            anchor.Resolve(out Vector3 p, out Vector3 n);
            faces.Add(points.Count);
            points.Add(transform.InverseTransformPoint(p + n * 0.006f));
            directions.Add(transform.localToWorldMatrix.transpose.MultiplyVector(n).normalized);
            uv.Add(new Vector2(Vector3.Distance(p, centre) / radius, 0f));
        }
        if (paintMesh == null)
        {
            paintMesh = new Mesh { name = "Surface Goo", indexFormat = IndexFormat.UInt32 };
            gameObject.AddComponent<MeshFilter>().sharedMesh = paintMesh;
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            Shader shader = Shader.Find("Transmutation/Surface Goo");
            Material template = Resources.Load<Material>("SurfaceGoo");
            paintMaterial = template != null ? new Material(template) : new Material(shader != null ? shader : source != null ? source.shader : Shader.Find("Universal Render Pipeline/Lit"));
            Color colour = source != null && source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : new Color(0.14f, 0.36f, 0.92f);
            paintMaterial.SetColor("_BaseColor", colour);
            renderer.sharedMaterial = paintMaterial;
        }
        vertices = points.ToArray(); normals = directions.ToArray(); indices = faces.ToArray();
        radial=new float[uv.Count];for(int i=0;i<uv.Count;i++)radial[i]=uv[i].x;
        paintMesh.Clear(); paintMesh.SetVertices(points); paintMesh.SetNormals(directions);
        paintMesh.SetUVs(0, uv); paintMesh.SetTriangles(faces, 0); paintMesh.RecalculateBounds();
        localBounds = paintMesh.bounds;
        IndexTriangles();
        return true;
    }

    void IndexTriangles()
    {
        cells.Clear(); Vector3 s=transform.lossyScale;
        gridScale=new Vector3(Mathf.Abs(s.x),Mathf.Abs(s.y),Mathf.Abs(s.z));
        for(int i=0;i<vertices.Length;i+=3)
        {
            Vector3 a=Vector3.Scale(vertices[i],gridScale), b=Vector3.Scale(vertices[i+1],gridScale), c=Vector3.Scale(vertices[i+2],gridScale);
            Vector3Int lo=Vector3Int.FloorToInt(Vector3.Min(a,Vector3.Min(b,c))*2f), hi=Vector3Int.FloorToInt(Vector3.Max(a,Vector3.Max(b,c))*2f);
            for(int x=lo.x;x<=hi.x;x++)for(int y=lo.y;y<=hi.y;y++)for(int z=lo.z;z<=hi.z;z++)
            {
                var key=new Vector3Int(x,y,z);
                if(!cells.TryGetValue(key,out var list))cells.Add(key,list=new List<int>());
                list.Add(i);
            }
        }
    }

    void OnEnable() => ProceduralWorld.AnyTerrainChanged += TerrainChanged;
    void OnDisable() => ProceduralWorld.AnyTerrainChanged -= TerrainChanged;
    void TerrainChanged(ProceduralWorld world, Bounds bounds)
    {
        if (world.gameObject.scene != gameObject.scene || vertices == null || !SurfaceGeometry.IsTerrain(support) ||
            support.GetComponent<ProceduralWorldChunk>().Owner != world) return;
        Bounds expanded = bounds; expanded.Expand(radius * 2f);
        if (!expanded.Contains(transform.TransformPoint(localBounds.center))) return;
        SurfaceGeometry.Invalidate(world, bounds);
        dirty = true; rebuildAfter = Time.time + 0.1f;
    }
    void Update()
    {
        if (support == null || !support.enabled || !support.gameObject.activeInHierarchy) { Destroy(gameObject); return; }
        Vector3 currentScale=transform.lossyScale;
        if(new Vector3(Mathf.Abs(currentScale.x),Mathf.Abs(currentScale.y),Mathf.Abs(currentScale.z))!=gridScale)IndexTriangles();
        spread = Mathf.Min(1f, spread + Time.deltaTime / 0.22f);
        if (paintMaterial != null) paintMaterial.SetFloat("_Spread", spread);
        if (!dirty || Time.time < rebuildAfter) return;
        dirty = false;
        if (support == null) { Destroy(gameObject); return; }
        Vector3 p = support.transform.TransformPoint(impactPoint), n = Normal;
        if (!SurfaceGeometry.Probe(gameObject.scene, p + n * 0.08f, -n, 0.16f, SurfaceGeometry.IsTerrain(support), out origin) || !Build(null))
            Destroy(gameObject);
    }

    public static bool Covers(Vector3 point, float margin = 0.025f) => Covers(point, Vector3.zero, margin);
    public static bool Covers(Vector3 point, Vector3 normal, float margin = 0.025f)
    {
        foreach (GooPaint paint in All)
            if (paint != null && paint.isActiveAndEnabled && !paint.dirty && paint.Distance(point, normal, margin + 0.012f) <= margin + 0.012f) return true;
        return false;
    }
    public static bool Near(Vector3 point, float reach)
    {
        foreach (GooPaint paint in All)
            if (paint != null && paint.isActiveAndEnabled && !paint.dirty && paint.Distance(point, Vector3.zero, reach) <= reach) return true;
        return false;
    }
    float Distance(Vector3 point, Vector3 expectedNormal, float range)
    {
        if (vertices == null || support == null) return float.PositiveInfinity;
        Vector3 local = transform.InverseTransformPoint(point);
        Vector3 scale = transform.lossyScale;
        if(new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z))!=gridScale)IndexTriangles();
        float minScale = Mathf.Max(0.0001f, Mathf.Min(Mathf.Abs(scale.x), Mathf.Min(Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
        if (localBounds.SqrDistance(local) * minScale * minScale > range * range) return float.PositiveInfinity;
        float best = float.PositiveInfinity;
        Vector3 scaled=Vector3.Scale(local,gridScale);
        Vector3Int lo=Vector3Int.FloorToInt((scaled-Vector3.one*range)*2f), hi=Vector3Int.FloorToInt((scaled+Vector3.one*range)*2f);
        candidates.Clear();
        for(int x=lo.x;x<=hi.x;x++)for(int y=lo.y;y<=hi.y;y++)for(int z=lo.z;z<=hi.z;z++)
            if(cells.TryGetValue(new Vector3Int(x,y,z),out var list))foreach(int index in list)candidates.Add(index);
        foreach(int i in candidates)
        {
            Vector3 n = transform.worldToLocalMatrix.transpose.MultiplyVector(normals[i]).normalized;
            if (expectedNormal.sqrMagnitude > 0.1f && Vector3.Dot(n, expectedNormal) < 0.5f) continue;
            Vector3 a = transform.TransformPoint(vertices[i]), b = transform.TransformPoint(vertices[i + 1]), c = transform.TransformPoint(vertices[i + 2]);
            Vector3 candidate = SurfaceGeometry.Closest(point, a, b, c);
            Vector3 bary=SurfaceGeometry.Barycentric(candidate,a,b,c);
            if(radial[i]*bary.x+radial[i+1]*bary.y+radial[i+2]*bary.z > spread*1.12f+0.02f)continue;
            float distance = (candidate - point).sqrMagnitude;
            if (distance < best) best = distance;
        }
        return Mathf.Sqrt(best);
    }
    void OnDestroy()
    {
        All.Remove(this);
        if (paintMesh != null) Destroy(paintMesh);
        if (paintMaterial != null) Destroy(paintMaterial);
    }
}
