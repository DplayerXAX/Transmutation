using System.Collections.Generic;
using UnityEngine;

/// <summary>Clips a river footprint against the actual terrain faces, rather than bridging them with a height grid.</summary>
public sealed class RealmStreamSurface
{
    public const float Clearance = 0.018f;
    private const float TileSize = 4f;
    private readonly Vector3 origin;
    private readonly List<Face> faces = new List<Face>();
    private readonly Dictionary<Vector2Int, List<int>> tiles = new Dictionary<Vector2Int, List<int>>();
    private readonly HashSet<int> candidates = new HashSet<int>();
    private readonly List<Point> polygon = new List<Point>(8);
    private readonly List<Point> scratch = new List<Point>(8);
    private struct Point
    {
        public Vector3 position, normal;
        public Point(Vector3 position, Vector3 normal) { this.position = position; this.normal = normal; }
    }
    private struct Face { public Point a, b, c; }

    public RealmStreamSurface(Vector3 origin) => this.origin = origin;

    public void Add(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
        if (normal.y < 0.2f) return; // No cave ceilings or near-vertical walls.
        a -= origin; b -= origin; c -= origin;
        int index = faces.Count;
        faces.Add(new Face { a = new Point(a, na), b = new Point(b, nb), c = new Point(c, nc) });
        Vector2Int min = Tile(Vector3.Min(a, Vector3.Min(b, c)));
        Vector2Int max = Tile(Vector3.Max(a, Vector3.Max(b, c)));
        for (int z = min.y; z <= max.y; z++)
        for (int x = min.x; x <= max.x; x++)
        {
            var key = new Vector2Int(x, z);
            if (!tiles.TryGetValue(key, out List<int> list)) tiles.Add(key, list = new List<int>());
            list.Add(index);
        }
    }

    public void Stamp(RealmMeshes mesh, Vector3 left0, Vector3 right0, Vector3 left1, Vector3 right1,
        float distance0, float distance1)
    {
        left0 -= origin; right0 -= origin; left1 -= origin; right1 -= origin;
        Vector2Int min = Tile(Vector3.Min(Vector3.Min(left0, right0), Vector3.Min(left1, right1)));
        Vector2Int max = Tile(Vector3.Max(Vector3.Max(left0, right0), Vector3.Max(left1, right1)));
        candidates.Clear();
        for (int z = min.y; z <= max.y; z++)
        for (int x = min.x; x <= max.x; x++)
            if (tiles.TryGetValue(new Vector2Int(x, z), out List<int> list))
                foreach (int index in list) candidates.Add(index);
        foreach (int index in candidates)
        {
            Face face = faces[index];
            Project(mesh, face, left0, right0, left1,
                new Vector2(-1f, distance0), new Vector2(1f, distance0), new Vector2(-1f, distance1));
            Project(mesh, face, right0, right1, left1,
                new Vector2(1f, distance0), new Vector2(1f, distance1), new Vector2(-1f, distance1));
        }
    }

    private static Vector2Int Tile(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / TileSize), Mathf.FloorToInt(p.z / TileSize));
    private static float Cross(Vector3 a, Vector3 b) => a.x * b.z - a.z * b.x;

    private void Project(RealmMeshes mesh, Face face, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
    {
        float area = Cross(b - a, c - a);
        if (Mathf.Abs(area) < 0.000001f) return;
        polygon.Clear(); polygon.Add(face.a); polygon.Add(face.b); polygon.Add(face.c);
        Clip(a, b, Mathf.Sign(area));
        Clip(b, c, Mathf.Sign(area));
        Clip(c, a, Mathf.Sign(area));
        if (polygon.Count < 3) return;
        int first = mesh.VertexCount;
        foreach (Point point in polygon)
        {
            float wb = Cross(point.position - a, c - a) / area;
            float wc = Cross(b - a, point.position - a) / area;
            Vector2 uv = ua + (ub - ua) * wb + (uc - ua) * wc;
            mesh.Vertex(point.position + origin + Vector3.up * Clearance, point.normal.normalized, Vector2.zero, uv);
        }
        for (int i = 1; i + 1 < polygon.Count; i++)
            if (Mathf.Abs(Cross(polygon[i].position - polygon[0].position, polygon[i + 1].position - polygon[0].position)) > 0.0000001f)
                mesh.Triangle(first, first + i, first + i + 1);
    }

    private void Clip(Vector3 a, Vector3 b, float sign)
    {
        scratch.Clear();
        if (polygon.Count == 0) return;
        Point previous = polygon[polygon.Count - 1];
        float previousDistance = Cross(b - a, previous.position - a) * sign;
        foreach (Point current in polygon)
        {
            float distance = Cross(b - a, current.position - a) * sign;
            if ((distance >= 0f) != (previousDistance >= 0f))
            {
                float t = previousDistance / (previousDistance - distance);
                scratch.Add(new Point(Vector3.Lerp(previous.position, current.position, t), Vector3.Lerp(previous.normal, current.normal, t)));
            }
            if (distance >= 0f) scratch.Add(current);
            previous = current; previousDistance = distance;
        }
        polygon.Clear(); polygon.AddRange(scratch);
    }
}
