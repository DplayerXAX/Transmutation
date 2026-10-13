using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Fully procedural replacement for the painted Unity Terrain, built from a density field with
/// chunked Marching Cubes so it can hold overhangs, caves and a second world underneath.
///
/// Layout (heights relative to this transform, h = surface height at a column):
///   Surface         y = h                         walked on from above
///   Crust underside y = -crust - h * mirror       the surface hanging upside down: its ceiling
///   Inner world     y = -crust - gap - h * invert heights reversed: hills above become pits below
/// Sinkholes and worm tunnels connect the two. Gravity is normal everywhere.
///
/// Chunks are generated on worker threads; meshes and colliders are created on the main thread.
/// </summary>
[DisallowMultipleComponent]
public sealed partial class ProceduralWorld : MonoBehaviour
{
    [Header("Size")]
    [Tooltip("Horizontal size of the world in metres (x, z), centred on this transform.")]
    [SerializeField] private Vector2 worldSize = new Vector2(288f, 288f);
    [Range(0.5f, 3f)] [SerializeField] private float cellSize = 1.25f;
    [Range(8, 48)] [SerializeField] private int chunkCells = 24;
    [SerializeField] private int seed = 1;

    [Header("Surface")]
    [Tooltip("Height range of hills and valleys.")]
    [Min(0f)] [SerializeField] private float surfaceAmplitude = 32f;
    [Min(0.0001f)] [SerializeField] private float surfaceFrequency = 0.013f;
    [Tooltip("Sharp ridge lines on top of the rolling hills.")]
    [Range(0f, 1f)] [SerializeField] private float ridges = 0.7f;
    [Tooltip("3D noise for overhangs, ledges and lumps.")]
    [Min(0f)] [SerializeField] private float overhangAmplitude = 5f;
    [Min(0.001f)] [SerializeField] private float overhangFrequency = 0.06f;
    [Tooltip("Flat, safe ground around the spawn point (this transform).")]
    [Min(0f)] [SerializeField] private float spawnFlatRadius = 18f;
    [Min(0f)] [SerializeField] private float spawnBlend = 30f;
    [Tooltip("Cliffs that wall in the edge of the world.")]
    [Min(0f)] [SerializeField] private float rimHeight = 30f;

    [Header("Inner World")]
    [Min(1f)] [SerializeField] private float crustThickness = 9f;
    [Tooltip("Open space between the crust underside and the inner ground.")]
    [Min(2f)] [SerializeField] private float innerGap = 20f;
    [Tooltip("How strongly surface hills hang down from the inner ceiling.")]
    [Range(0f, 1f)] [SerializeField] private float ceilingMirror = 0.8f;
    [Tooltip("How strongly inner ground heights are the reverse of the surface.")]
    [Range(0f, 1.5f)] [SerializeField] private float innerInvert = 1f;
    [Min(0f)] [SerializeField] private float innerDetail = 5f;

    [Header("Caves")]
    [Min(0.001f)] [SerializeField] private float caveFrequency = 0.025f;
    [Tooltip("Tunnel thickness. 0 disables worm caves.")]
    [Range(0f, 0.2f)] [SerializeField] private float caveWidth = 0.12f;
    [Tooltip("Caves only form within this depth of a rock surface, so they open onto the world.")]
    [Min(0f)] [SerializeField] private float caveDepth = 14f;
    [Range(0, 16)] [SerializeField] private int sinkholeCount = 6;
    [SerializeField] private Vector2 sinkholeRadius = new Vector2(4f, 7f);
    [Tooltip("A guaranteed sinkhole this far ahead (+z) of the spawn. 0 = none.")]
    [Min(0f)] [SerializeField] private float firstSinkholeDistance = 45f;

    [Header("Rendering")]
    [SerializeField] private Material surfaceMaterial;
    [SerializeField] private Material innerMaterial;
    [Tooltip("Layer for the generated chunks; Ground lets the player jump and creatures grip.")]
    [SerializeField] private int chunkLayer = 7;

    [Header("Loading")]
    [Tooltip("Chunks within this distance of the spawn are generated before the first frame.")]
    [Min(0f)] [SerializeField] private float blockingRadius = 40f;
    [Range(1, 32)] [SerializeField] private int meshesPerFrame = 6;
    [Tooltip("Teleport the player back to spawn if they fall out of the world.")]
    [SerializeField] private bool respawnFallingPlayer = true;

    [Header("Erosion")]
    [SerializeField] private bool erosionTestCourse;
    [Min(0f)] [SerializeField] private float erodibility = 1f;
    [Min(0f)] [SerializeField] private float densityBudgetPerSecond = 8f;
    [Min(0.01f)] [SerializeField] private float erosionInterval = 0.1f;
    [Range(1, 8)] [SerializeField] private int erosionMeshesPerFrame = 2;
    [Range(1, 8)] [SerializeField] private int workerCount = 2;
    [SerializeField] private AnimationCurve erosionFalloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    [System.Serializable]
    private struct Sinkhole { public Vector2 position; public float radius; }
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

    private void PrepareLayout()
    {
        var random = new System.Random(seed);
        noiseOffset = new Vector2((float)random.NextDouble() * 1000f, (float)random.NextDouble() * 1000f);

        float maxHill = surfaceAmplitude * (1f + ridges) + rimHeight + overhangAmplitude + 4f;
        yMax = maxHill;
        yMin = -crustThickness - innerGap - surfaceAmplitude * (1f + ridges) * Mathf.Max(innerInvert, ceilingMirror) - innerDetail - caveDepth - 4f;

        if (erosionTestCourse) { yMin = -14f; yMax = 14f; }
        float chunkSize = chunkCells * cellSize;
        chunkCount = new Vector3Int(
            Mathf.CeilToInt(worldSize.x / chunkSize),
            Mathf.CeilToInt((yMax - yMin) / chunkSize),
            Mathf.CeilToInt(worldSize.y / chunkSize));
        gridMin = new Vector3(-chunkCount.x * chunkSize * 0.5f, yMin, -chunkCount.z * chunkSize * 0.5f);

        var holes = new List<Sinkhole>();
        if (firstSinkholeDistance > 0f)
            holes.Add(new Sinkhole { position = new Vector2(0f, firstSinkholeDistance), radius = sinkholeRadius.y });
        int attempts = 0;
        while (holes.Count < sinkholeCount + (firstSinkholeDistance > 0f ? 1 : 0) && attempts++ < 200)
        {
            var position = new Vector2(
                ((float)random.NextDouble() - 0.5f) * worldSize.x * 0.8f,
                ((float)random.NextDouble() - 0.5f) * worldSize.y * 0.8f);
            if (position.magnitude < spawnFlatRadius + spawnBlend) continue;
            bool crowded = false;
            foreach (Sinkhole other in holes)
                crowded |= Vector2.Distance(other.position, position) < 30f;
            if (crowded) continue;
            float radius = Mathf.Lerp(sinkholeRadius.x, sinkholeRadius.y, (float)random.NextDouble());
            holes.Add(new Sinkhole { position = position, radius = radius });
        }
        sinkholes = holes.ToArray();
    }

    private float SurfaceHeight(float x, float z) => field.SurfaceHeight(x, z);
    private float CeilingHeight(float h) => field.CeilingHeight(h);
    private float InnerGroundHeight(float x, float z, float h) => field.InnerGroundHeight(x, z, h);
    [System.Serializable]
    private sealed class DensityField
    {
        public Vector2 worldSize;
        public float cellSize;
        public int chunkCells;
        public int seed;
        public float surfaceAmplitude;
        public float surfaceFrequency;
        public float ridges;
        public float overhangAmplitude;
        public float overhangFrequency;
        public float spawnFlatRadius;
        public float spawnBlend;
        public float rimHeight;
        public float crustThickness;
        public float innerGap;
        public float ceilingMirror;
        public float innerInvert;
        public float innerDetail;
        public float caveFrequency;
        public float caveWidth;
        public float caveDepth;
        public int sinkholeCount;
        public Vector2 sinkholeRadius;
        public float firstSinkholeDistance;
        public bool erosionTestCourse;
        public Vector2 noiseOffset;
        public Sinkhole[] sinkholes;
        public DensityField(ProceduralWorld world)
        {
            worldSize = world.worldSize;
            seed = world.seed;
            surfaceAmplitude = world.surfaceAmplitude;
            surfaceFrequency = world.surfaceFrequency;
            ridges = world.ridges;
            overhangAmplitude = world.overhangAmplitude;
            overhangFrequency = world.overhangFrequency;
            spawnFlatRadius = world.spawnFlatRadius;
            spawnBlend = world.spawnBlend;
            rimHeight = world.rimHeight;
            crustThickness = world.crustThickness;
            innerGap = world.innerGap;
            ceilingMirror = world.ceilingMirror;
            innerInvert = world.innerInvert;
            innerDetail = world.innerDetail;
            caveFrequency = world.caveFrequency;
            caveWidth = world.caveWidth;
            caveDepth = world.caveDepth;
            sinkholeCount = world.sinkholeCount;
            sinkholeRadius = world.sinkholeRadius;
            firstSinkholeDistance = world.firstSinkholeDistance;
            erosionTestCourse = world.erosionTestCourse;
            noiseOffset = world.noiseOffset;
            sinkholes = (Sinkhole[])world.sinkholes.Clone();
            cellSize = world.cellSize;
            chunkCells = world.chunkCells;
        }
        private static float CourseHeight(float x, float z)
        {
            if (x < -8f && z < -5f) return 4f;
            float ramp = Mathf.Max(0f, 6f - (z + 10f) * 0.3f);
            float fork = 1.2f * Mathf.Exp(-x * x * 0.5f - z * z / 16f);
            float basinBank = Mathf.Max(0f, z - 10f);
            return ramp + 0.035f * x * x + 0.08f * x + fork + basinBank * basinBank * 0.2f;
        }
    /// <summary>Surface height above this transform at a local column.</summary>
    public float SurfaceHeight(float x, float z)
    {
        if (erosionTestCourse) return CourseHeight(x, z);
        Vector2 p = new Vector2(x, z) * surfaceFrequency + noiseOffset;
        // Domain warp so hills do not line up on a grid.
        Vector2 warp = new Vector2(Fbm2(p * 0.7f + new Vector2(13.1f, 7.7f), 3), Fbm2(p * 0.7f + new Vector2(-4.3f, 21.9f), 3)) * 1.6f;
        p += warp;
        float rolling = Fbm2(p, 5);
        float ridge = 1f - Mathf.Abs(Fbm2(p * 1.7f + new Vector2(31.7f, -12.4f), 4) * 2f);
        float height = (rolling * 1.4f + ridge * ridge * ridges - 0.25f * ridges) * surfaceAmplitude;

        float flat = Mathf.SmoothStep(0f, 1f, (new Vector2(x, z).magnitude - spawnFlatRadius) / Mathf.Max(spawnBlend, 0.01f));
        height *= flat;

        // Cliffs around the edge of the world.
        float edge = Mathf.Max(Mathf.Abs(x) / (worldSize.x * 0.5f), Mathf.Abs(z) / (worldSize.y * 0.5f));
        height += Mathf.SmoothStep(0f, 1f, (edge - 0.82f) / 0.14f) * rimHeight;
        return height;
    }

    public float CeilingHeight(float h) => -crustThickness - h * ceilingMirror;

    public float InnerGroundHeight(float x, float z, float h)
    {
        Vector2 p = new Vector2(x, z) * surfaceFrequency * 2.3f + noiseOffset * 1.37f;
        float detail = Fbm2(p, 4) * innerDetail;
        float ground = -crustThickness - innerGap - h * innerInvert + detail;
        // Keep a minimum cave height so the inner world never pinches shut.
        return Mathf.Min(ground, CeilingHeight(h) - 6f);
    }

    /// <summary>Positive inside rock.</summary>
    public float Density(Vector3 p, float h, float ceiling, float floor)
    {
        if (erosionTestCourse)
        {
            float tunnel = Mathf.Max(Mathf.Abs(p.x - 9f) - 3f, Mathf.Max(Mathf.Abs(p.y + 3f) - 3f, 4f - p.z));
            return Mathf.Min(h - p.y, tunnel);
        }
        float top = h - p.y;
        float under = p.y - ceiling;
        float crust = Mathf.Min(top, under);
        float ground = floor - p.y;
        float density = Mathf.Max(crust, ground);

        // Seal the inner world at the edge of the map (but leave the sky open).
        float edge = Mathf.Max(Mathf.Abs(p.x) / (worldSize.x * 0.5f), Mathf.Abs(p.z) / (worldSize.y * 0.5f));
        if (edge > 0.9f)
            density = Mathf.Max(density, Mathf.Min((edge - 0.93f) * 200f, top));

        // 3D noise only near a surface, where it can change the shape; none on the spawn flat.
        float spawnDistance = new Vector2(p.x, p.z).magnitude;
        float wild = Mathf.SmoothStep(0f, 1f, (spawnDistance - spawnFlatRadius) / Mathf.Max(spawnBlend, 0.01f));
        if (wild > 0f && Mathf.Abs(density) < overhangAmplitude * 1.2f + 1f)
            density += Fbm3(p * overhangFrequency + new Vector3(noiseOffset.x, 0f, noiseOffset.y), 2) * overhangAmplitude * 2f * wild;

        // Worm tunnels: where two noise fields are both near zero.
        if (caveWidth > 0f && density > 0f && density < caveDepth)
        {
            if (spawnDistance > spawnFlatRadius + spawnBlend * 0.5f)
            {
                Vector3 q = p * caveFrequency + new Vector3(noiseOffset.y, 3.7f, noiseOffset.x);
                float a = ValueNoise3(q) - 0.5f;
                float b = ValueNoise3(q * 1.13f + new Vector3(41.3f, 17.9f, -8.4f)) - 0.5f;
                float tunnel = (Mathf.Sqrt(a * a + b * b) - caveWidth) / caveFrequency * 0.5f;
                density = Mathf.Min(density, tunnel);
            }
        }

        // Sinkholes: wobbly shafts through the crust, stopping above the inner ground.
        if (p.y > floor + 1.5f)
        {
            foreach (Sinkhole hole in sinkholes)
            {
                float dx = p.x - hole.position.x, dz = p.y * 0.07f + p.z - hole.position.y;
                float wobble = 1f + 0.25f * Mathf.Sin(p.y * 0.35f + hole.position.x);
                float shaft = Mathf.Sqrt(dx * dx + dz * dz) - hole.radius * wobble;
                density = Mathf.Min(density, shaft);
            }
        }
        return density;
    }

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

    private static float ValueNoise2(Vector2 p)
    {
        int ix = Mathf.FloorToInt(p.x), iz = Mathf.FloorToInt(p.y);
        float fx = p.x - ix, fz = p.y - iz;
        float ux = fx * fx * (3f - 2f * fx), uz = fz * fz * (3f - 2f * fz);
        float a = Hash(ix, 0, iz), b = Hash(ix + 1, 0, iz), c = Hash(ix, 0, iz + 1), d = Hash(ix + 1, 0, iz + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uz);
    }

    private static float ValueNoise3(Vector3 p)
    {
        int ix = Mathf.FloorToInt(p.x), iy = Mathf.FloorToInt(p.y), iz = Mathf.FloorToInt(p.z);
        float fx = p.x - ix, fy = p.y - iy, fz = p.z - iz;
        float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy), uz = fz * fz * (3f - 2f * fz);
        float x00 = Mathf.Lerp(Hash(ix, iy, iz), Hash(ix + 1, iy, iz), ux);
        float x10 = Mathf.Lerp(Hash(ix, iy + 1, iz), Hash(ix + 1, iy + 1, iz), ux);
        float x01 = Mathf.Lerp(Hash(ix, iy, iz + 1), Hash(ix + 1, iy, iz + 1), ux);
        float x11 = Mathf.Lerp(Hash(ix, iy + 1, iz + 1), Hash(ix + 1, iy + 1, iz + 1), ux);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, uy), Mathf.Lerp(x01, x11, uy), uz);
    }

    /// <summary>Roughly -0.5..0.5.</summary>
    private static float Fbm2(Vector2 p, int octaves)
    {
        float sum = 0f, amplitude = 0.5f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amplitude * (ValueNoise2(p) - 0.5f);
            p = p * 2.03f + new Vector2(17.3f, 9.1f);
            amplitude *= 0.5f;
        }
        return sum;
    }

    /// <summary>Roughly -0.5..0.5.</summary>
    private static float Fbm3(Vector3 p, int octaves)
    {
        float sum = 0f, amplitude = 0.5f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amplitude * (ValueNoise3(p) - 0.5f);
            p = p * 2.03f + new Vector3(5.2f, 1.3f, 7.7f);
            amplitude *= 0.5f;
        }
        return sum;
    }

}
