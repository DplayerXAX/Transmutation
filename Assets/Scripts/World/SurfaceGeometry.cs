using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>A point on a particular revision of a collision surface. Coordinates follow moving supports.</summary>
public struct SurfaceAnchor
{
    internal SurfaceGeometry.Face face;
    public Vector3 barycentric;
    public Collider Support => face?.cache.support;
    public int Triangle => face == null ? -1 : face.index;
    public bool Resolve(out Vector3 point, out Vector3 normal)
    {
        point = normal = default;
        if (face == null || !face.cache.Valid) return false;
        point = face.A * barycentric.x + face.B * barycentric.y + face.C * barycentric.z;
        normal = face.Normal;
        return true;
    }
}

/// <summary>
/// Collision geometry is the source of truth. Paths cross shared triangle edges, never jump to the
/// nearest raycast hit. Terrain chunks share an edge namespace; unrelated nearby objects do not.
/// </summary>
public static class SurfaceGeometry
{
    internal sealed class Cache
    {
        internal Collider support;
        internal Mesh mesh;
        internal Vector3[] vertices;
        internal Face[] faces;
        internal int group;
        internal Transform space;
        internal bool invalidated;
        internal bool Valid => !invalidated && support != null && support.enabled && support.gameObject.activeInHierarchy &&
            (!(support is MeshCollider mc) || mc.sharedMesh == mesh);
    }
    internal sealed class Face
    {
        internal Cache cache;
        internal int index, a, b, c;
        internal bool collapsed;
        internal readonly EdgeKey[] edges = new EdgeKey[3];
        internal Vector3 A => cache.support.transform.TransformPoint(cache.vertices[a]);
        internal Vector3 B => cache.support.transform.TransformPoint(cache.vertices[b]);
        internal Vector3 C => cache.support.transform.TransformPoint(cache.vertices[c]);
        internal Vector3 Normal => Vector3.Cross(B - A, C - A).normalized;
        internal Vector3 Centre => (A + B + C) / 3f;
        internal Vector3 Vertex(int i) => i == 0 ? A : i == 1 ? B : C;
    }
    internal readonly struct EdgeKey : IEquatable<EdgeKey>
    {
        readonly Vector3Int a, b;
        readonly int group;
        internal EdgeKey(int group, Vector3 p, Vector3 q)
        {
            this.group = group;
            Vector3Int x = Vector3Int.RoundToInt(p * 10000f), y = Vector3Int.RoundToInt(q * 10000f);
            bool first = x.x < y.x || (x.x == y.x && (x.y < y.y || (x.y == y.y && x.z <= y.z)));
            a = first ? x : y; b = first ? y : x;
        }
        public bool Equals(EdgeKey other) => group == other.group && a == other.a && b == other.b;
        public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);
        public override int GetHashCode() => unchecked(group * 397 ^ a.GetHashCode() * 31 ^ b.GetHashCode());
    }
    readonly struct Link { internal readonly Face face; internal Link(Face f) { face = f; } }
    static readonly Dictionary<int, Cache> caches = new Dictionary<int, Cache>();
    static readonly Dictionary<EdgeKey, List<Link>> edges = new Dictionary<EdgeKey, List<Link>>();
    static Collider[] overlaps = new Collider[64];
    static readonly RaycastHit[] hits = new RaycastHit[64];
    public const int GroundMask = (1 << 7) | (1 << 8);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        caches.Clear(); edges.Clear();
        SceneManager.sceneUnloaded -= SceneUnloaded;
        SceneManager.sceneUnloaded += SceneUnloaded;
    }
    static void SceneUnloaded(Scene scene)
    {
        var removed=new List<int>();
        foreach(var item in caches)if(item.Value.support==null || item.Value.support.gameObject.scene==scene)removed.Add(item.Key);
        foreach(int key in removed)Remove(key);
    }

    public static bool IsTerrain(Collider support) => support != null &&
        support.TryGetComponent<ProceduralWorldChunk>(out var chunk) && chunk.Owner != null && chunk.Owner.OwnsCollider(support);

    public static void Invalidate(ProceduralWorld world, Bounds bounds)
    {
        var remove = new List<int>();
        foreach (var entry in caches)
            if (!entry.Value.Valid || (entry.Value.space == world.transform && entry.Value.support.bounds.Intersects(bounds)))
                remove.Add(entry.Key);
        foreach (int key in remove) Remove(key);
    }

    static void Remove(int key)
    {
        if (!caches.TryGetValue(key, out Cache cache)) return;
        cache.invalidated = true;
        foreach (Face face in cache.faces)
        if (!face.collapsed)
        foreach (EdgeKey edge in face.edges)
            if (edges.TryGetValue(edge, out var list))
            { list.RemoveAll(l => l.face.cache == cache); if (list.Count == 0) edges.Remove(edge); }
        caches.Remove(key);
    }

    static Cache Get(Collider support)
    {
        if (support == null || !support.enabled || support.isTrigger || support.GetComponentInParent<Creature>() != null ||
            support.GetComponentInParent<SmoothFirstPersonController>() != null) return null;
        int key = support.GetInstanceID();
        if (caches.TryGetValue(key, out Cache old))
        { if (old.Valid) return old; Remove(key); }
        var cache = new Cache { support = support, space = support.transform, group = key };
        if (support.TryGetComponent<ProceduralWorldChunk>(out var chunk) && chunk.Owner != null)
        { cache.space = chunk.Owner.transform; cache.group = chunk.Owner.GetInstanceID(); }
        int[] triangles;
        if (support is MeshCollider mc)
        {
            cache.mesh = mc.sharedMesh;
            if (cache.mesh == null) return null;
            if (cache.mesh.isReadable) { cache.vertices = cache.mesh.vertices; triangles = cache.mesh.triangles; }
            else
            {
                var baked = support.GetComponent<SurfaceMeshProxy>();
                if (baked == null || baked.source != mc.sharedMesh) return null;
                cache.vertices = baked.vertices; triangles = baked.triangles;
            }
        }
        else if (!Primitive(support, out cache.vertices, out triangles)) return null;
        cache.faces = new Face[triangles.Length / 3];
        for (int i = 0; i < cache.faces.Length; i++)
        {
            Face face = cache.faces[i] = new Face { cache = cache, index = i,
                a = triangles[i * 3], b = triangles[i * 3 + 1], c = triangles[i * 3 + 2] };
            // Marching cubes can emit collapsed triangles at exact isolevel vertices.
            // They have no surface area and must not make a valid shared edge look non-manifold.
            face.collapsed = face.Normal.sqrMagnitude < 0.5f;
            if (face.collapsed) continue;
            for (int e = 0; e < 3; e++)
            {
                Vector3 p = cache.space.InverseTransformPoint(face.Vertex(e));
                Vector3 q = cache.space.InverseTransformPoint(face.Vertex((e + 1) % 3));
                EdgeKey edge = face.edges[e] = new EdgeKey(cache.group, p, q);
                if (!edges.TryGetValue(edge, out var list)) edges.Add(edge, list = new List<Link>(2));
                list.Add(new Link(face));
            }
        }
        caches[key] = cache;
        return cache;
    }

    public static void Prepare(Collider support, Vector3 point, float radius)
    {
        Get(support);
        if (!IsTerrain(support)) return;
        var marker = support.GetComponent<ProceduralWorldChunk>();
        int count;
        var physics = support.gameObject.scene.GetPhysicsScene();
        do
        {
            count = physics.OverlapBox(point, Vector3.one * radius, overlaps, Quaternion.identity,
                1 << support.gameObject.layer, QueryTriggerInteraction.Ignore);
            if (count < overlaps.Length) break;
            Array.Resize(ref overlaps, overlaps.Length * 2);
        } while (overlaps.Length < 4096);
        for (int i = 0; i < count; i++)
            if (overlaps[i].TryGetComponent<ProceduralWorldChunk>(out var other) && other.Owner == marker.Owner) Get(overlaps[i]);
    }

    public static bool Acquire(Collider support, Vector3 point, Vector3 normal, out SurfaceAnchor anchor)
    {
        anchor = default;
        Cache cache = Get(support);
        if (cache == null) return false;
        float best = 0.16f; Face chosen = null; Vector3 bary = default;
        foreach (Face face in cache.faces)
        {
            if (Vector3.Dot(face.Normal, normal) < 0.2f) continue;
            Vector3 p = Closest(point, face.A, face.B, face.C);
            float distance = (p - point).sqrMagnitude;
            if (distance >= best) continue;
            chosen = face; best = distance; bary = Barycentric(p, face.A, face.B, face.C);
        }
        if (chosen == null) return false;
        anchor = new SurfaceAnchor { face = chosen, barycentric = bary };
        return true;
    }

    public static bool Probe(Scene scene, Vector3 origin, Vector3 direction, float distance, bool terrainOnly,
        out SurfaceAnchor anchor)
    {
        anchor = default;
        int count = scene.GetPhysicsScene().Raycast(origin, direction, hits, distance, GroundMask, QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity; RaycastHit selected = default;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.distance >= best || hit.collider.GetComponentInParent<Creature>() != null || (terrainOnly && !IsTerrain(hit.collider))) continue;
            best = hit.distance; selected = hit;
        }
        return best < float.PositiveInfinity && Acquire(selected.collider, selected.point, selected.normal, out anchor);
    }

    static Face Neighbour(Face face, int edge)
    {
        if (!edges.TryGetValue(face.edges[edge], out var list)) return null;
        Face result = null;
        foreach (Link link in list)
        {
            if (link.face == face || !link.face.cache.Valid) continue;
            if (result != null) return null; // Ambiguous/non-manifold support is a barrier.
            result = link.face;
        }
        return result;
    }

    /// <summary>Walk through faces, rotating the tangent around each shared edge. A boundary returns false.</summary>
    public static bool Trace(SurfaceAnchor start, Vector3 direction, float length, out SurfaceAnchor end,
        List<SurfaceAnchor> path = null)
    {
        end = start;
        if (!start.Resolve(out Vector3 point, out Vector3 normal)) return false;
        Prepare(start.Support, point, length + 0.1f);
        Vector3 tangent = Vector3.ProjectOnPlane(direction, normal).normalized;
        if (tangent.sqrMagnitude < 0.5f) return false;
        Face face = start.face;
        path?.Add(start);
        for (int iteration = 0; iteration < 256; iteration++)
        {
            Vector3 bary = Barycentric(point, face.A, face.B, face.C);
            Vector3 delta = Barycentric(point + tangent, face.A, face.B, face.C) - bary;
            float travel = length; int edge = -1;
            for (int axis = 0; axis < 3; axis++)
                if (delta[axis] < -0.000001f)
                {
                    float t = -bary[axis] / delta[axis];
                    if (t >= -0.0001f && t < travel) { travel = Mathf.Max(0f, t); edge = (axis + 1) % 3; }
                }
            point += tangent * travel;
            end = new SurfaceAnchor { face = face, barycentric = Barycentric(point, face.A, face.B, face.C) };
            path?.Add(end);
            length -= travel;
            if (length <= 0.0001f || edge < 0) return true;
            Face next = Neighbour(face, edge);
            if (next == null) return false;
            Vector3 along = (face.Vertex((edge + 1) % 3) - face.Vertex(edge)).normalized;
            float angle = Vector3.SignedAngle(face.Normal, next.Normal, along);
            tangent = Quaternion.AngleAxis(angle, along) * tangent;
            face = next;
            // Move a numerical epsilon into the new face; never use a distant closest-surface snap.
            Vector3 towardsCentre = (face.Centre - point).normalized;
            point += towardsCentre * 0.00002f;
        }
        return false;
    }

    public readonly struct PatchTriangle
    {
        public readonly SurfaceAnchor a, b, c;
        public PatchTriangle(SurfaceAnchor a, SurfaceAnchor b, SurfaceAnchor c) { this.a = a; this.b = b; this.c = c; }
    }
    sealed class Visit { internal Face face; internal Vector3 entry; internal float distance; }

    /// <summary>
    /// Bounded surface flood. Distances are lengths of valid paths through faces (an upper bound on
    /// geodesic distance). Adaptive border subdivision clips the contour without a flat spanning fan.
    /// </summary>
    public static List<PatchTriangle> BuildPatch(SurfaceAnchor origin, float radius, int seed)
    {
        var output = new List<PatchTriangle>();
        if (!origin.Resolve(out Vector3 centre, out _)) return output;
        Prepare(origin.Support, centre, radius + 0.2f);
        var best = new Dictionary<Face, Visit>();
        var queue = new List<Visit> { new Visit { face = origin.face, entry = centre } };
        best.Add(origin.face, queue[0]);
        int cursor = 0;
        while (cursor < queue.Count && best.Count < 24000)
        {
            Visit visit = queue[cursor++];
            if (best[visit.face] != visit || visit.distance > radius * 1.12f) continue;
            for (int edge = 0; edge < 3; edge++)
            {
                Face next = Neighbour(visit.face, edge);
                if (next == null) continue;
                Vector3 entry = SegmentClosest(visit.entry, visit.face.Vertex(edge), visit.face.Vertex((edge + 1) % 3));
                float distance = visit.distance + Vector3.Distance(visit.entry, entry);
                if (distance > radius * 1.12f || (best.TryGetValue(next, out Visit known) && known.distance <= distance + 0.0001f)) continue;
                var added = new Visit { face = next, entry = entry, distance = distance };
                best[next] = added; queue.Add(added);
            }
        }
        foreach (Visit visit in best.Values)
            Cut(visit, visit.face.A, visit.face.B, visit.face.C, 0, radius, seed, output);
        if(radius>0.5f && origin.Resolve(out _,out var normal) && Mathf.Abs(normal.y)<0.65f)
            AppendDrips(origin,radius,output);
        return output;
    }

    static void AppendDrips(SurfaceAnchor seed,float radius,List<PatchTriangle> output)
    {
        seed.Resolve(out _,out var normal);
        Vector3 down=Vector3.ProjectOnPlane(Vector3.down,normal).normalized,across=Vector3.Cross(normal,down).normalized;
        var path=new List<SurfaceAnchor>();
        for(int drip=-1;drip<=1;drip++)
        {
            SurfaceAnchor current=seed;
            if(drip!=0 && !Trace(seed,across*drip,radius*0.25f,out current))continue;
            int steps=Mathf.CeilToInt(radius/0.15f);
            for(int step=0;step<steps;step++)
            {
                if(!current.Resolve(out _,out normal))break;
                down=Vector3.ProjectOnPlane(Vector3.down,normal).normalized;
                if(down.sqrMagnitude<0.1f)break;
                path.Clear();bool supported=Trace(current,down,0.15f,out var next,path);
                float width=Mathf.Lerp(0.10f,0.025f,step/(float)steps);
                for(int i=1;i<path.Count;i++)
                {
                    if(!path[i-1].Resolve(out var a,out _) || !path[i].Resolve(out var b,out _))continue;
                    Face face=path[i].face;Vector3 direction=b-a;if(direction.sqrMagnitude<0.000001f)continue;
                    Vector3 side=Vector3.Cross(face.Normal,direction.normalized)*width;
                    var polygon=new List<Vector3>{a-side,b-side,b+side,a+side};
                    // Clip every stroke segment to its source face: even the sides adhere at sharp corners.
                    for(int edge=0;edge<3 && polygon.Count>0;edge++)
                    {
                        Vector3 first=face.Vertex(edge),span=face.Vertex((edge+1)%3)-first;
                        var clipped=new List<Vector3>(6);
                        for(int v=0;v<polygon.Count;v++)
                        {
                            Vector3 p=polygon[v],q=polygon[(v+1)%polygon.Count];
                            float dp=Vector3.Dot(Vector3.Cross(span,p-first),face.Normal),dq=Vector3.Dot(Vector3.Cross(span,q-first),face.Normal);
                            if(dp>=0f)clipped.Add(p);
                            if((dp<0f)!=(dq<0f))clipped.Add(Vector3.Lerp(p,q,dp/(dp-dq)));
                        }
                        polygon=clipped;
                    }
                    for(int v=1;v+1<polygon.Count;v++)output.Add(new PatchTriangle(At(face,polygon[0]),At(face,polygon[v]),At(face,polygon[v+1])));
                }
                current=next;if(!supported)break;
            }
        }
    }

    static float Contour(Visit visit, Vector3 p, float radius, int seed)
    {
        float distance = visit.distance + Vector3.Distance(visit.entry, p);
        // Low-frequency position noise is continuous across source triangles and chunk seams.
        float noise = Mathf.PerlinNoise(p.x * 1.35f + seed * 0.013f + p.y * 0.7f, p.z * 1.35f + p.y * 0.4f);
        return distance - radius * Mathf.Lerp(0.84f, 1.06f, noise);
    }

    static void Cut(Visit visit, Vector3 a, Vector3 b, Vector3 c, int depth, float radius, int seed, List<PatchTriangle> output)
    {
        float da = Contour(visit, a, radius, seed), db = Contour(visit, b, radius, seed), dc = Contour(visit, c, radius, seed);
        float size = Mathf.Max(Vector3.Distance(a, b), Mathf.Max(Vector3.Distance(b, c), Vector3.Distance(c, a)));
        if (Mathf.Min(da, Mathf.Min(db, dc)) > size * 1.5f) return;
        bool inside = da <= 0f && db <= 0f && dc <= 0f;
        if (!inside && depth < 10 && size > 0.13f)
        {
            Vector3 ab = (a + b) * 0.5f, bc = (b + c) * 0.5f, ca = (c + a) * 0.5f;
            Cut(visit, a, ab, ca, depth + 1, radius, seed, output);
            Cut(visit, ab, b, bc, depth + 1, radius, seed, output);
            Cut(visit, ca, bc, c, depth + 1, radius, seed, output);
            Cut(visit, ab, bc, ca, depth + 1, radius, seed, output);
            return;
        }
        var polygon = new List<Vector3>(4);
        Vector3[] points = { a, b, c }; float[] distances = { da, db, dc };
        for (int i = 0; i < 3; i++)
        {
            int j = (i + 1) % 3;
            if (distances[i] <= 0f) polygon.Add(points[i]);
            if ((distances[i] < 0f) != (distances[j] < 0f)) polygon.Add(Vector3.Lerp(points[i], points[j], distances[i] / (distances[i] - distances[j])));
        }
        for (int i = 1; i + 1 < polygon.Count; i++)
            output.Add(new PatchTriangle(At(visit.face, polygon[0]), At(visit.face, polygon[i]), At(visit.face, polygon[i + 1])));
    }

    static SurfaceAnchor At(Face face, Vector3 p) => new SurfaceAnchor { face = face, barycentric = Barycentric(p, face.A, face.B, face.C) };

    public static Vector3 SegmentClosest(Vector3 p, Vector3 a, Vector3 b)
    { Vector3 d = b - a; return a + d * Mathf.Clamp01(Vector3.Dot(p - a, d) / Mathf.Max(0.0000001f, d.sqrMagnitude)); }

    public static Vector3 Barycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 v = b - a, w = c - a, q = p - a;
        float vv = Vector3.Dot(v, v), vw = Vector3.Dot(v, w), ww = Vector3.Dot(w, w);
        float denominator = vv * ww - vw * vw;
        if (Mathf.Abs(denominator) < 1e-12f) return Vector3.right;
        float y = (ww * Vector3.Dot(q, v) - vw * Vector3.Dot(q, w)) / denominator;
        float z = (vv * Vector3.Dot(q, w) - vw * Vector3.Dot(q, v)) / denominator;
        return new Vector3(1f - y - z, y, z);
    }

    public static Vector3 Closest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 bary = Barycentric(p, a, b, c);
        if (bary.x >= 0f && bary.y >= 0f && bary.z >= 0f) return a * bary.x + b * bary.y + c * bary.z;
        Vector3 ab = SegmentClosest(p, a, b), bc = SegmentClosest(p, b, c), ca = SegmentClosest(p, c, a);
        return (p - ab).sqrMagnitude < (p - bc).sqrMagnitude ? ((p - ab).sqrMagnitude < (p - ca).sqrMagnitude ? ab : ca) :
            ((p - bc).sqrMagnitude < (p - ca).sqrMagnitude ? bc : ca);
    }

    static bool Primitive(Collider support, out Vector3[] vertices, out int[] triangles)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        if (support is BoxCollider box)
        {
            for (int i = 0; i < 8; i++) v.Add(box.center + Vector3.Scale(box.size * 0.5f,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
            t.AddRange(new[] { 0,2,3,0,3,1, 4,5,7,4,7,6, 0,1,5,0,5,4, 2,6,7,2,7,3, 0,4,6,0,6,2, 1,3,7,1,7,5 });
        }
        else if (support is SphereCollider || support is CapsuleCollider)
        {
            SphereCollider sphere = support as SphereCollider; CapsuleCollider capsule = support as CapsuleCollider;
            float r = sphere != null ? sphere.radius : capsule.radius;
            Vector3 centre = sphere != null ? sphere.center : capsule.center;
            float extension = capsule == null ? 0f : Mathf.Max(0f, capsule.height * 0.5f - r);
            Quaternion rotation = capsule == null || capsule.direction == 1 ? Quaternion.identity :
                Quaternion.FromToRotation(Vector3.up, capsule.direction == 0 ? Vector3.right : Vector3.forward);
            int sides = Mathf.Clamp(Mathf.CeilToInt(Mathf.PI * Mathf.Sqrt(r * support.transform.lossyScale.magnitude / 0.004f)), 24, 128);
            int rings = sides / 2;
            for (int row = 0; row <= rings; row++)
            for (int col = 0; col < sides; col++)
            {
                float latitude = row * Mathf.PI / rings, angle = col * Mathf.PI * 2f / sides;
                Vector3 p = new Vector3(Mathf.Sin(latitude) * Mathf.Cos(angle), Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(angle)) * r;
                p.y += p.y >= 0f ? extension : -extension;
                v.Add(centre + rotation * p);
                if (row == rings) continue;
                int a = row * sides + col, b = row * sides + (col + 1) % sides, c = a + sides, d = b + sides;
                if(row>0){t.Add(a);t.Add(b);t.Add(c);}
                if(row<rings-1){t.Add(b);t.Add(d);t.Add(c);}
            }
        }
        else { vertices = null; triangles = null; return false; }
        vertices = v.ToArray(); triangles = t.ToArray(); return true;
    }
}
