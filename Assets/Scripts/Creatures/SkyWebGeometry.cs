using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Seeded, bounded cage geometry. Also supplies the exact strand paths used for trapping.</summary>
public sealed class SkyWebGeometry
{
    public readonly struct Strand
    {
        public readonly Vector3 Start;
        public readonly Vector3 End;
        public readonly float Radius;
        public readonly bool Support;
        public readonly bool Connection;

        public Strand(Vector3 start, Vector3 end, float radius, bool support, bool connection = false)
        {
            Start = start;
            End = end;
            Radius = radius;
            Support = support;
            Connection = connection;
        }
    }

    private sealed class Cell
    {
        public Vector3 center;
        public float patrolRadius;
        public int endStrand;
        public float radius;
        public Quaternion rotation;
        public Vector3[] points;
        public Vector3[] patch;
    }

    private const int TubeSides = 6;
    public const int VerticesPerCell = 20;
    public const int FacesPerCell = 12;
    public const int EdgesPerCell = 30;
    private static readonly Vector3[] DicePoints = CreateDicePoints();
    private static readonly int[][] DiceFaces =
    {
        new[] { 13, 10, 0, 9, 1 }, new[] { 14, 8, 0, 10, 2 },
        new[] { 15, 9, 0, 8, 4 }, new[] { 3, 13, 1, 11, 17 },
        new[] { 5, 11, 1, 9, 15 }, new[] { 3, 12, 2, 10, 13 },
        new[] { 6, 14, 2, 12, 18 }, new[] { 18, 12, 3, 17, 7 },
        new[] { 5, 15, 4, 16, 19 }, new[] { 6, 16, 4, 8, 14 },
        new[] { 17, 11, 5, 19, 7 }, new[] { 19, 16, 6, 18, 7 }
    };
    private static readonly Vector2Int[] DiceEdges = CreateDiceEdges();
    private readonly List<Strand> strands = new List<Strand>(256);
    private readonly List<Cell> cells = new List<Cell>(7);
    private readonly List<Vector3> vertices = new List<Vector3>(3072);
    private readonly List<Vector3> normals = new List<Vector3>(3072);
    private readonly List<Color> colors = new List<Color>(3072);
    private readonly List<int> triangles = new List<int>(9216);
    private readonly List<Vector3> patchVertices = new List<Vector3>(48);
    private readonly List<int> patchTriangles = new List<int>(144);
    private readonly System.Random random;
    private readonly int strandLimit;
    private readonly int patchLimit;
    private readonly float radius;
    private readonly float supportRadius;
    private readonly float silkRadius;
    private readonly float clusterSpacing;
    private readonly float clusterSpread;
    private readonly float cellSize;
    private readonly int looseStrandsPerCell;

    public int StrandCount => strands.Count;
    public float Scale { get; }
    public float MaximumRadius => radius * Scale;
    public int CellCount => cells.Count;
    public Vector3 GetCellCenter(int index) => cells[index].center;
    public int InitialStrandCount => cells[0].endStrand;
    public int RenderedStrandCount { get; private set; }
    public int RenderedPatchCount { get; private set; }
    public int MeshRevision { get; private set; }
    public Strand GetStrand(int index) => strands[index];

    public SkyWebGeometry(int seed, float maximumRadius, int cellCount, int maximumStrands,
        int maximumPatches, float supportThickness, float silkThickness,
        float clusterSpacing = 0.72f, float clusterSpread = 0.18f,
        float cellSize = 0.14f, int looseStrandsPerCell = 3, float webScale = 1f)
    {
        random = new System.Random(seed);
        radius = Mathf.Max(0.2f, maximumRadius);
        strandLimit = Mathf.Clamp(maximumStrands, 32, 1024);
        patchLimit = Mathf.Clamp(maximumPatches, 0, 24);
        supportRadius = Mathf.Clamp(supportThickness, 0.001f, radius * 0.05f);
        silkRadius = Mathf.Clamp(silkThickness, 0.001f, supportRadius);
        this.clusterSpacing = Mathf.Clamp(clusterSpacing, 0.3f, 0.85f);
        this.clusterSpread = Mathf.Clamp(clusterSpread, 0.02f, 0.35f);
        this.cellSize = Mathf.Clamp(cellSize, 0.06f, 0.3f);
        this.looseStrandsPerCell = Mathf.Clamp(looseStrandsPerCell, 0, 6);
        Scale = Mathf.Max(0.05f, webScale);
        // Reserve a full cage plus a connector for each cell. Never deliver a partial D12.
        int count = Mathf.Min(Mathf.Clamp(cellCount, 1, 24), (strandLimit + 1) / (EdgesPerCell + 1));
        for (int i = 0; i < count; i++) BuildCell(i, count);
        // Bake a uniform scale into both rendering and contact paths. Budget and seed stay unchanged.
        for (int i = 0; i < strands.Count; i++)
        {
            Strand strand = strands[i];
            strands[i] = new Strand(strand.Start * Scale, strand.End * Scale,
                strand.Radius * Scale, strand.Support, strand.Connection);
        }
        foreach (Cell cell in cells)
        {
            cell.center *= Scale;
            cell.radius *= Scale;
            cell.patrolRadius *= Scale;
            for (int i = 0; i < cell.points.Length; i++) cell.points[i] *= Scale;
            for (int i = 0; i < cell.patch.Length; i++) cell.patch[i] *= Scale;
        }
    }

    private static Vector3[] CreateDicePoints()
    {
        float phi = (1f + Mathf.Sqrt(5f)) * 0.5f;
        float inversePhi = 1f / phi;
        var points = new Vector3[VerticesPerCell];
        int index = 0;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2) points[index++] = new Vector3(x, y, z);
        for (int a = -1; a <= 1; a += 2)
        for (int b = -1; b <= 1; b += 2)
        {
            points[index++] = new Vector3(0f, a * inversePhi, b * phi);
            points[index++] = new Vector3(a * inversePhi, b * phi, 0f);
            points[index++] = new Vector3(a * phi, 0f, b * inversePhi);
        }
        for (int i = 0; i < points.Length; i++) points[i] /= Mathf.Sqrt(3f);
        return points;
    }

    private static Vector2Int[] CreateDiceEdges()
    {
        var edges = new List<Vector2Int>(EdgesPerCell);
        var used = new HashSet<int>();
        foreach (int[] face in DiceFaces)
        for (int i = 0; i < face.Length; i++)
        {
            int a = Mathf.Min(face[i], face[(i + 1) % face.Length]);
            int b = Mathf.Max(face[i], face[(i + 1) % face.Length]);
            if (used.Add(a * VerticesPerCell + b)) edges.Add(new Vector2Int(a, b));
        }
        return edges.ToArray();
    }

    private float RandomRange(float minimum, float maximum)
    {
        return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
    }

    private void AddStrand(Vector3 start, Vector3 end, bool support, bool connection = false)
    {
        if (strands.Count >= strandLimit || (end - start).sqrMagnitude < 0.000001f) return;
        strands.Add(new Strand(start, end, support ? supportRadius : silkRadius, support, connection));
    }

    private void BuildCell(int index, int count)
    {
        // Two small pairs, then individual cages on different branches of the web.
        int clusterRoot = index < 2 ? 0 : index < 4 ? 2 : index;
        int cluster = clusterRoot == 0 ? 0 : clusterRoot == 2 ? 1 : clusterRoot - 2;
        Vector3 clusterCenter = Vector3.zero;
        if (cluster > 0)
        {
            Vector3 direction;
            if (cluster == 1) direction = new Vector3(-0.65f, 0.55f, 0.55f).normalized;
            else if (cluster == 2) direction = new Vector3(0.7f, -0.35f, 0.6f).normalized;
            else if (cluster == 3) direction = new Vector3(0.45f, 0.65f, -0.61f).normalized;
            else if (cluster == 4) direction = new Vector3(-0.65f, -0.6f, -0.47f).normalized;
            else
            {
                float angle = cluster * 2.39996323f;
                direction = new Vector3(Mathf.Cos(angle), RandomRange(-0.8f, 0.8f), Mathf.Sin(angle)).normalized;
            }
            clusterCenter = direction * radius * clusterSpacing;
        }
        Vector3 center = clusterCenter;
        if (index != clusterRoot)
        {
            Vector3 offset = new Vector3(RandomRange(-1f, 1f), RandomRange(-1f, 1f), RandomRange(-1f, 1f)).normalized;
            center += offset * radius * clusterSpread * RandomRange(0.8f, 1.15f);
        }
        center = Vector3.ClampMagnitude(center, radius - supportRadius - radius * 0.08f);
        float cellRadius = Mathf.Min(radius * cellSize * RandomRange(0.8f, 1.15f),
            radius - supportRadius - center.magnitude);
        var cell = new Cell
        {
            center = center,
            radius = cellRadius,
            patrolRadius = cellRadius * 0.6f,
            rotation = Quaternion.Euler(RandomRange(0f, 180f), RandomRange(0f, 360f), RandomRange(0f, 180f)),
            points = new Vector3[VerticesPerCell],
            patch = new Vector3[5]
        };
        for (int i = 0; i < cell.points.Length; i++) cell.points[i] = center + cell.rotation * DicePoints[i] * cellRadius;
        if (index > 0)
        {
            // Close neighbors share a short connection; separate clusters get a long silk bridge.
            Cell nearest = cells[0];
            for (int i = 1; i < cells.Count; i++)
                if ((cells[i].center - center).sqrMagnitude < (nearest.center - center).sqrMagnitude) nearest = cells[i];
            AddBridge(nearest.points, cell.points);
        }
        foreach (Vector2Int edge in DiceEdges) AddStrand(cell.points[edge.x], cell.points[edge.y], true);
        int[] patchFace = DiceFaces[random.Next(FacesPerCell)];
        for (int i = 0; i < patchFace.Length; i++) cell.patch[i] = cell.points[patchFace[i]];
        int reserved = (count - index - 1) * (EdgesPerCell + 1);
        // Spend spare strands on long individual threads instead of nested face rings.
        for (int i = 0; i < looseStrandsPerCell && strandLimit - strands.Count > reserved; i++)
        {
            Vector3 direction = new Vector3(RandomRange(-1f, 1f), RandomRange(-1f, 1f),
                RandomRange(-1f, 1f)).normalized;
            Vector3 end = direction * (radius - supportRadius) * RandomRange(0.88f, 0.98f);
            Vector3 start = cell.points[0];
            for (int vertex = 1; vertex < cell.points.Length; vertex++)
                if ((end - cell.points[vertex]).sqrMagnitude < (end - start).sqrMagnitude)
                    start = cell.points[vertex];
            AddStrand(start, end, false, true);
        }
        cell.endStrand = strands.Count;
        cells.Add(cell);
    }

    private void AddBridge(Vector3[] first, Vector3[] second)
    {
        float nearest = float.PositiveInfinity;
        Vector3 start = first[0], end = second[0];
        for (int a = 0; a < first.Length; a++)
        for (int b = 0; b < second.Length; b++)
        {
            float distance = (first[a] - second[b]).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance;
            start = first[a];
            end = second[b];
        }
        AddStrand(start, end, true, true);
    }

    public Vector3 SamplePatrolPoint(float selector, Vector3 offset)
    {
        int completed = 1;
        while (completed < cells.Count && cells[completed].endStrand <= RenderedStrandCount) completed++;
        Cell cell = cells[Mathf.Min(completed - 1, Mathf.FloorToInt(selector * completed))];
        return cell.center + Vector3.ClampMagnitude(offset, 1f) * cell.patrolRadius;
    }

    public void Render(Mesh strandMesh, Mesh patchMesh, int visibleStrands)
    {
        visibleStrands = Mathf.Clamp(visibleStrands, 0, strands.Count);
        if (MeshRevision > 0 && visibleStrands == RenderedStrandCount) return;
        vertices.Clear(); normals.Clear(); colors.Clear(); triangles.Clear();
        patchVertices.Clear(); patchTriangles.Clear();
        for (int i = 0; i < visibleStrands; i++) AppendTube(strands[i]);
        strandMesh.Clear();
        strandMesh.SetVertices(vertices);
        strandMesh.SetNormals(normals);
        strandMesh.SetColors(colors);
        strandMesh.SetTriangles(triangles, 0);
        int patches = 0;
        for (int i = 0; i < cells.Count && patches < patchLimit; i++)
        {
            if (cells[i].endStrand > visibleStrands) break;
            AppendPatch(cells[i].patch);
            patches++;
        }
        patchMesh.Clear();
        patchMesh.SetVertices(patchVertices);
        patchMesh.SetTriangles(patchTriangles, 0);
        patchMesh.RecalculateNormals();
        RenderedStrandCount = visibleStrands;
        RenderedPatchCount = patches;
        MeshRevision++;
    }

    private void AppendTube(Strand strand)
    {
        Vector3 direction = (strand.End - strand.Start).normalized;
        Vector3 axis = Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right;
        Vector3 right = Vector3.Cross(direction, axis).normalized;
        Vector3 up = Vector3.Cross(direction, right);
        int first = vertices.Count;
        Color tint = strand.Support ? new Color(0.28f, 0.32f, 0.36f) : new Color(0.85f, 0.91f, 0.97f);
        for (int ring = 0; ring < 2; ring++)
        for (int side = 0; side < TubeSides; side++)
        {
            float angle = side * Mathf.PI * 2f / TubeSides;
            Vector3 normal = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            vertices.Add((ring == 0 ? strand.Start : strand.End) + normal * strand.Radius);
            normals.Add(normal);
            colors.Add(tint);
        }
        for (int side = 0; side < TubeSides; side++)
        {
            int a = first + side, b = first + (side + 1) % TubeSides;
            triangles.Add(a); triangles.Add(b); triangles.Add(a + TubeSides);
            triangles.Add(b); triangles.Add(b + TubeSides); triangles.Add(a + TubeSides);
        }
    }

    private void AppendPatch(Vector3[] polygon)
    {
        int first = patchVertices.Count;
        Vector3 center = Vector3.zero;
        for (int i = 0; i < polygon.Length; i++) center += polygon[i] / polygon.Length;
        patchVertices.Add(center);
        for (int i = 0; i < polygon.Length; i++) patchVertices.Add(Vector3.Lerp(center, polygon[i], 0.87f));
        for (int i = 0; i < polygon.Length; i++)
        {
            patchTriangles.Add(first);
            patchTriangles.Add(first + 1 + i);
            patchTriangles.Add(first + 1 + (i + 1) % polygon.Length);
        }
    }

    /// <summary>Squared closest distance between two finite world-space segments, including point segments.</summary>
    public static float SegmentDistanceSquared(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
        out Vector3 pointOnFirst, out Vector3 pointOnSecond)
    {
        Vector3 first = b - a, second = d - c, difference = a - c;
        float aa = Vector3.Dot(first, first), bb = Vector3.Dot(second, second);
        float alongSecond = Vector3.Dot(second, difference);
        float s, t;
        const float epsilon = 0.000001f;
        if (aa <= epsilon && bb <= epsilon) { s = 0f; t = 0f; }
        else if (aa <= epsilon) { s = 0f; t = Mathf.Clamp01(alongSecond / bb); }
        else
        {
            float alongFirst = Vector3.Dot(first, difference);
            if (bb <= epsilon) { t = 0f; s = Mathf.Clamp01(-alongFirst / aa); }
            else
            {
                float cross = Vector3.Dot(first, second);
                float denominator = aa * bb - cross * cross;
                s = denominator > epsilon ? Mathf.Clamp01((cross * alongSecond - alongFirst * bb) / denominator) : 0f;
                t = (cross * s + alongSecond) / bb;
                if (t < 0f) { t = 0f; s = Mathf.Clamp01(-alongFirst / aa); }
                else if (t > 1f) { t = 1f; s = Mathf.Clamp01((cross - alongFirst) / aa); }
            }
        }
        pointOnFirst = a + first * s;
        pointOnSecond = c + second * t;
        return (pointOnFirst - pointOnSecond).sqrMagnitude;
    }
}
