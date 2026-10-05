using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds a rocky hill with a walk-through cave as a Marching Cubes mesh.
/// Unity Terrain is only a heightmap and cannot have overhangs, so caves live in this separate mesh,
/// which sits on the Terrain (it samples terrain height per column) and carries its own MeshCollider.
///
/// Density (positive = rock) = hill height above the local ground − height + 3D noise,
/// then a tunnel and chamber are carved out by taking the minimum with their distance fields.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public sealed class CaveFormation : MonoBehaviour
{
    [System.Serializable]
    public struct TunnelPoint
    {
        [Tooltip("Horizontal position in local space (x, z).")]
        public Vector2 position;
        [Tooltip("Height of the tunnel centre above the ground below it.")]
        public float heightAboveGround;
        public float radius;
    }

    [System.Serializable]
    public struct HillBump
    {
        public Vector2 position;
        public float height;
        public float radius;
    }

    [Header("Volume")]
    [Tooltip("Horizontal size of the generated area in metres (x, z).")]
    [SerializeField] private Vector2 footprint = new Vector2(48f, 48f);
    [Tooltip("Metres from the lowest ground to the top of the volume.")]
    [Min(4f)] [SerializeField] private float volumeHeight = 26f;
    [Range(0.25f, 2f)] [SerializeField] private float cellSize = 0.6f;
    [Tooltip("Rock is buried this far below the terrain outside the hill, so no slab shows.")]
    [Min(0.5f)] [SerializeField] private float buryDepth = 3f;

    [Header("Hill Shape")]
    [SerializeField] private HillBump[] bumps =
    {
        new HillBump { position = new Vector2(0f, 0f), height = 15f, radius = 18f },
        new HillBump { position = new Vector2(-11f, 9f), height = 10f, radius = 11f },
        new HillBump { position = new Vector2(10f, -6f), height = 8f, radius = 10f },
        new HillBump { position = new Vector2(6f, 13f), height = 6f, radius = 8f },
    };
    [SerializeField] private int seed = 7;
    [Tooltip("Strength of the 3D noise that makes overhangs, ledges and lumps.")]
    [Min(0f)] [SerializeField] private float noiseAmplitude = 2.6f;
    [Min(0.001f)] [SerializeField] private float noiseFrequency = 0.11f;
    [Tooltip("Rounds the rock into flat terraces like the posterized terrain. 0 = smooth.")]
    [Range(0f, 1f)] [SerializeField] private float terracing = 0.35f;
    [Min(0.1f)] [SerializeField] private float terraceHeight = 1.6f;

    [Header("Cave")]
    [SerializeField] private TunnelPoint[] tunnel =
    {
        new TunnelPoint { position = new Vector2(0f, -25f), heightAboveGround = 1.6f, radius = 2.6f },
        new TunnelPoint { position = new Vector2(-1f, -16f), heightAboveGround = 1.9f, radius = 2.4f },
        new TunnelPoint { position = new Vector2(-5f, -8f), heightAboveGround = 2.2f, radius = 2.2f },
        new TunnelPoint { position = new Vector2(0f, 0f), heightAboveGround = 3.2f, radius = 2.6f },
        new TunnelPoint { position = new Vector2(6f, 7f), heightAboveGround = 3.6f, radius = 2.2f },
        new TunnelPoint { position = new Vector2(4f, 16f), heightAboveGround = 2.4f, radius = 2.4f },
        new TunnelPoint { position = new Vector2(3f, 25f), heightAboveGround = 1.6f, radius = 2.6f },
    };
    [Tooltip("Index of the tunnel point that opens into a large chamber.")]
    [SerializeField] private int chamberPoint = 3;
    [Min(0f)] [SerializeField] private float chamberRadius = 6.5f;
    [Tooltip("Vertical squash of the chamber; below 1 gives a flatter, wider room.")]
    [Range(0.3f, 1.5f)] [SerializeField] private float chamberFlatten = 0.65f;
    [Tooltip("Radius of a shaft from the chamber to the sky. 0 = no skylight.")]
    [Min(0f)] [SerializeField] private float skylightRadius = 1.4f;
    [Tooltip("Roughness of the cave walls.")]
    [Min(0f)] [SerializeField] private float caveNoise = 0.7f;

    [Header("Debug")]
    [SerializeField] private bool drawBounds = true;
    [SerializeField] private int lastVertexCount;

    private static readonly Vector3Int[] CornerOffsets =
    {
        new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(1, 0, 1), new Vector3Int(0, 0, 1),
        new Vector3Int(0, 1, 0), new Vector3Int(1, 1, 0), new Vector3Int(1, 1, 1), new Vector3Int(0, 1, 1),
    };

    private static readonly int[,] EdgeConnections =
    {
        { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
        { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
        { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
    };

    private Mesh mesh;
    private bool rebuildRequested = true;

    // Grid layout, rebuilt per generation.
    private int nx, ny, nz;
    private Vector3 gridOrigin;           // Local position of sample (0,0,0).
    private float[] groundHeights;        // Local ground height per (x, z) column.
    private Vector3[] tunnelLocal;        // Tunnel centres in local space, resolved against the ground.

    private void OnEnable() => rebuildRequested = true;
    private void OnValidate() => rebuildRequested = true;

    private void Update()
    {
        if (!rebuildRequested) return;
        rebuildRequested = false;
        Generate();
    }

    private void OnDestroy()
    {
        if (mesh == null) return;
        if (Application.isPlaying) Destroy(mesh);
        else DestroyImmediate(mesh);
    }

    [ContextMenu("Regenerate")]
    public void Generate()
    {
        PrepareGrid();
        float[] density = SampleDensity();
        BuildMesh(density);
    }

    // ---------------- Density field ----------------

    private void PrepareGrid()
    {
        nx = Mathf.Max(2, Mathf.CeilToInt(footprint.x / cellSize) + 1);
        nz = Mathf.Max(2, Mathf.CeilToInt(footprint.y / cellSize) + 1);

        groundHeights = new float[nx * nz];
        float minGround = float.MaxValue;
        for (int z = 0; z < nz; z++)
        for (int x = 0; x < nx; x++)
        {
            float lx = -footprint.x * 0.5f + x * cellSize;
            float lz = -footprint.y * 0.5f + z * cellSize;
            float ground = LocalGroundHeight(new Vector2(lx, lz));
            groundHeights[z * nx + x] = ground;
            minGround = Mathf.Min(minGround, ground);
        }

        float bottom = minGround - buryDepth - cellSize * 2f;
        ny = Mathf.Max(2, Mathf.CeilToInt((volumeHeight + buryDepth + cellSize * 2f) / cellSize) + 1);
        gridOrigin = new Vector3(-footprint.x * 0.5f, bottom, -footprint.y * 0.5f);

        tunnelLocal = new Vector3[tunnel.Length];
        for (int i = 0; i < tunnel.Length; i++)
        {
            Vector2 p = tunnel[i].position;
            tunnelLocal[i] = new Vector3(p.x, LocalGroundHeight(p) + tunnel[i].heightAboveGround, p.y);
        }
    }

    private float[] SampleDensity()
    {
        var density = new float[nx * ny * nz];
        Vector3 noiseOffset = new Vector3(seed * 17.13f, seed * 3.71f, seed * 29.9f);

        for (int z = 0; z < nz; z++)
        for (int x = 0; x < nx; x++)
        {
            float ground = groundHeights[z * nx + x];
            float lx = gridOrigin.x + x * cellSize;
            float lz = gridOrigin.z + z * cellSize;
            float hill = HillHeight(new Vector2(lx, lz));
            bool border = x == 0 || z == 0 || x == nx - 1 || z == nz - 1;

            for (int y = 0; y < ny; y++)
            {
                int index = (y * nz + z) * nx + x;
                if (border || y == 0 || y == ny - 1)
                {
                    density[index] = -1f; // Close the mesh at the volume edge.
                    continue;
                }

                var p = new Vector3(lx, gridOrigin.y + y * cellSize, lz);
                float heightAboveGround = p.y - ground;
                float surface = hill;
                if (terracing > 0f && surface > 0f)
                {
                    float stepped = Mathf.Floor(surface / terraceHeight) * terraceHeight;
                    surface = Mathf.Lerp(surface, stepped + terraceHeight * 0.5f, terracing * 0.6f);
                }

                float rock = surface - heightAboveGround;
                // Noise matters most on the hill, so the buried skirt stays buried.
                float noiseWeight = Mathf.Clamp01((hill + buryDepth) / (buryDepth + 2f));
                rock += Fbm(p * noiseFrequency + noiseOffset) * noiseAmplitude * noiseWeight;

                float cave = CaveDistance(p, noiseOffset);
                density[index] = Mathf.Min(rock, cave);
            }
        }
        return density;
    }

    /// <summary>Hill height above local ground; negative outside the hill so rock stays buried.</summary>
    private float HillHeight(Vector2 p)
    {
        float height = -buryDepth;
        foreach (HillBump bump in bumps)
        {
            float d = Vector2.Distance(p, bump.position) / Mathf.Max(bump.radius, 0.01f);
            if (d >= 1f) continue;
            float falloff = (1f - d * d) * (1f - d * d);
            // Smooth union of bumps so the hills merge into one range.
            height = Mathf.Max(height, bump.height * falloff - buryDepth * (1f - falloff));
        }
        return height;
    }

    /// <summary>Signed distance to the carved tunnel, chamber and skylight (negative = air).</summary>
    private float CaveDistance(Vector3 p, Vector3 noiseOffset)
    {
        float distance = float.MaxValue;
        for (int i = 0; i + 1 < tunnelLocal.Length; i++)
        {
            Vector3 a = tunnelLocal[i];
            Vector3 b = tunnelLocal[i + 1];
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-5f));
            Vector3 closest = a + ab * t;
            Vector3 offset = p - closest;
            // Slightly flattened floor: squash below the centre line so the tunnel is easy to walk.
            if (offset.y < 0f) offset.y *= 1.35f;
            float radius = Mathf.Lerp(tunnel[i].radius, tunnel[i + 1].radius, t);
            distance = Mathf.Min(distance, offset.magnitude - radius);
        }

        if (chamberPoint >= 0 && chamberPoint < tunnelLocal.Length && chamberRadius > 0f)
        {
            Vector3 c = tunnelLocal[chamberPoint];
            Vector3 offset = p - c;
            offset.y /= Mathf.Max(chamberFlatten, 0.01f);
            distance = Mathf.Min(distance, offset.magnitude - chamberRadius);

            if (skylightRadius > 0f && p.y > c.y)
            {
                float shaft = new Vector2(p.x - c.x, p.z - c.z).magnitude - skylightRadius;
                distance = Mathf.Min(distance, shaft);
            }
        }

        if (tunnelLocal.Length == 0) return float.MaxValue;
        return distance + Fbm(p * 0.35f + noiseOffset * 1.7f) * caveNoise;
    }

    private float LocalGroundHeight(Vector2 localXZ)
    {
        Vector3 world = transform.TransformPoint(new Vector3(localXZ.x, 0f, localXZ.y));
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            if (terrain == null || terrain.terrainData == null) continue;
            Vector3 origin = terrain.GetPosition();
            Vector3 size = terrain.terrainData.size;
            if (world.x < origin.x || world.z < origin.z || world.x > origin.x + size.x || world.z > origin.z + size.z)
                continue;
            float worldY = terrain.SampleHeight(world) + origin.y;
            return transform.InverseTransformPoint(new Vector3(world.x, worldY, world.z)).y;
        }
        return 0f;
    }

    // ---------------- Noise ----------------

    private static float Fbm(Vector3 p)
    {
        float sum = 0f;
        float amplitude = 0.5f;
        for (int octave = 0; octave < 3; octave++)
        {
            sum += amplitude * (ValueNoise(p) * 2f - 1f);
            p = p * 2.03f + new Vector3(5.2f, 1.3f, 7.7f);
            amplitude *= 0.5f;
        }
        return sum;
    }

    private static float ValueNoise(Vector3 p)
    {
        Vector3Int i = Vector3Int.FloorToInt(p);
        Vector3 f = p - i;
        Vector3 u = new Vector3(f.x * f.x * (3f - 2f * f.x), f.y * f.y * (3f - 2f * f.y), f.z * f.z * (3f - 2f * f.z));

        float n000 = Hash(i.x, i.y, i.z), n100 = Hash(i.x + 1, i.y, i.z);
        float n010 = Hash(i.x, i.y + 1, i.z), n110 = Hash(i.x + 1, i.y + 1, i.z);
        float n001 = Hash(i.x, i.y, i.z + 1), n101 = Hash(i.x + 1, i.y, i.z + 1);
        float n011 = Hash(i.x, i.y + 1, i.z + 1), n111 = Hash(i.x + 1, i.y + 1, i.z + 1);

        float x00 = Mathf.Lerp(n000, n100, u.x), x10 = Mathf.Lerp(n010, n110, u.x);
        float x01 = Mathf.Lerp(n001, n101, u.x), x11 = Mathf.Lerp(n011, n111, u.x);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, u.y), Mathf.Lerp(x01, x11, u.y), u.z);
    }

    private static float Hash(int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
    }

    // ---------------- Marching Cubes ----------------

    private void BuildMesh(float[] density)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var edgeLookup = new Dictionary<long, int>();
        var cornerDensities = new float[8];
        var cornerIndices = new int[8];
        var edgeVertices = new int[12];

        for (int z = 0; z < nz - 1; z++)
        for (int y = 0; y < ny - 1; y++)
        for (int x = 0; x < nx - 1; x++)
        {
            int cubeIndex = 0;
            for (int c = 0; c < 8; c++)
            {
                Vector3Int o = CornerOffsets[c];
                int sample = ((y + o.y) * nz + (z + o.z)) * nx + (x + o.x);
                cornerIndices[c] = sample;
                cornerDensities[c] = density[sample];
                if (cornerDensities[c] > 0f) cubeIndex |= 1 << c;
            }

            int edgeMask = MarchingCubesTables.EdgeTable[cubeIndex];
            if (edgeMask == 0) continue;

            for (int e = 0; e < 12; e++)
            {
                if ((edgeMask & (1 << e)) == 0) continue;
                int a = cornerIndices[EdgeConnections[e, 0]];
                int b = cornerIndices[EdgeConnections[e, 1]];
                // Shared edges between neighbouring cubes reuse one vertex, giving smooth normals.
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (!edgeLookup.TryGetValue(key, out int vertexIndex))
                {
                    float da = density[a];
                    float db = density[b];
                    float t = Mathf.Abs(da - db) < 1e-6f ? 0.5f : Mathf.Clamp01(da / (da - db));
                    vertexIndex = vertices.Count;
                    vertices.Add(Vector3.Lerp(SamplePosition(a), SamplePosition(b), t));
                    edgeLookup.Add(key, vertexIndex);
                }
                edgeVertices[e] = vertexIndex;
            }

            for (int i = 0; MarchingCubesTables.TriTable[cubeIndex, i] != -1; i += 3)
            {
                triangles.Add(edgeVertices[MarchingCubesTables.TriTable[cubeIndex, i]]);
                triangles.Add(edgeVertices[MarchingCubesTables.TriTable[cubeIndex, i + 1]]);
                triangles.Add(edgeVertices[MarchingCubesTables.TriTable[cubeIndex, i + 2]]);
            }
        }

        if (mesh == null)
        {
            mesh = new Mesh { name = "Cave Formation", hideFlags = HideFlags.DontSave };
        }
        mesh.Clear();
        mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();

        // The highest vertex must face the sky; if not, the winding is inside out for this table.
        if (vertices.Count > 0)
        {
            int top = 0;
            for (int i = 1; i < vertices.Count; i++)
                if (vertices[i].y > vertices[top].y) top = i;
            if (mesh.normals[top].y < 0f)
            {
                for (int i = 0; i < triangles.Count; i += 3)
                    (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
            }
        }

        mesh.RecalculateBounds();
        lastVertexCount = vertices.Count;

        GetComponent<MeshFilter>().sharedMesh = mesh;
        var meshCollider = GetComponent<MeshCollider>();
        meshCollider.sharedMesh = null; // Force the collider to re-cook the new mesh.
        meshCollider.sharedMesh = mesh;
    }

    private Vector3 SamplePosition(int sample)
    {
        int x = sample % nx;
        int z = (sample / nx) % nz;
        int y = sample / (nx * nz);
        return gridOrigin + new Vector3(x, y, z) * cellSize;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawBounds) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
        Gizmos.DrawWireCube(new Vector3(0f, volumeHeight * 0.5f, 0f), new Vector3(footprint.x, volumeHeight, footprint.y));
        Gizmos.color = new Color(1f, 0.78f, 0.015f, 0.8f);
        for (int i = 0; i + 1 < tunnel.Length; i++)
        {
            Vector3 a = new Vector3(tunnel[i].position.x, tunnel[i].heightAboveGround, tunnel[i].position.y);
            Vector3 b = new Vector3(tunnel[i + 1].position.x, tunnel[i + 1].heightAboveGround, tunnel[i + 1].position.y);
            Gizmos.DrawLine(a, b);
        }
    }
}
