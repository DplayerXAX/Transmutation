using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;

/// <summary>
/// Seeded, volumetric amplified terrain. Height bias is combined with blended 3D range noise,
/// then cheese/spaghetti cavities are subtracted. Skylands are a separate union in the same field.
/// Inspired by Minecraft's density pipeline, not a port of its biome splines or noise implementation.
/// A shape recipe for ProceduralWorld, with no mesh, collider, tool or lifecycle of its own.
/// This class contains no Unity object access and can be sampled on worker threads.
/// </summary>
public sealed class MountainDensity
{
    public struct Island { public Vector3 centre; public Vector2 radius; public float depth; }
    public readonly Island[] Islands;
    private readonly Vector3 offset;
    private readonly TerrainGenerationRecipe recipe;
    private readonly ConcurrentDictionary<Vector3Int, float> lattice = new ConcurrentDictionary<Vector3Int, float>();

    public MountainDensity(int seed, Vector2 worldSize, TerrainGenerationRecipe settings)
    {
        if (settings.version != 1) throw new ArgumentException("Unsupported amplified terrain recipe version: " + settings.version);
        if (settings.baseSampleSpacing < 1f || settings.verticalBounds.x >= settings.verticalBounds.y)
            throw new ArgumentException("Invalid amplified terrain sample spacing or bounds.");
        recipe = settings;
        var random = new System.Random(seed);
        offset = new Vector3((float)random.NextDouble() * 900f, (float)random.NextDouble() * 900f, (float)random.NextDouble() * 900f);
        var islands = new List<Island>();
        int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(recipe.islandCount)));
        for (int i = 0; i < recipe.islandCount; i++)
        {
            float px = ((i % columns + 0.5f + ((float)random.NextDouble() - 0.5f) * 0.4f) / columns - 0.5f) * worldSize.x * 0.88f;
            float pz = ((i / columns + 0.5f + ((float)random.NextDouble() - 0.5f) * 0.4f) / columns - 0.5f) * worldSize.y * 0.88f;
            if (Vector2.Distance(new Vector2(px, pz), new Vector2(recipe.safeSpawn.x, recipe.safeSpawn.z)) < 115f) continue;
            islands.Add(new Island { centre = new Vector3(px, Mathf.Lerp(recipe.islandElevation.x, recipe.islandElevation.y, (float)random.NextDouble()), pz),
                radius = new Vector2(Mathf.Lerp(recipe.islandRadius.x, recipe.islandRadius.y, (float)random.NextDouble()), Mathf.Lerp(recipe.islandRadius.x, recipe.islandRadius.y, (float)random.NextDouble())),
                depth = 55f + (float)random.NextDouble() * 65f });
        }
        Islands = islands.ToArray();
    }

    public float Height(float x, float z)
    {
        Vector3 p = new Vector3(x * recipe.mountainFrequency, 0f, z * recipe.mountainFrequency) + offset;
        float warp = Fbm(p * 1.6f, 2) * 0.65f;
        p += new Vector3(warp, 0f, -warp * 0.7f);
        float continents = Fbm(p * 0.65f, 3);
        float ridge = 1f - Mathf.Abs(Fbm(p * 2.1f, 4));
        float erosion = Mathf.Clamp01(0.5f + Fbm(p * 0.8f + Vector3.one * 31f, 2));
        float h = 8f + continents * 75f + Mathf.Pow(ridge, 4f) * Mathf.Lerp(recipe.mountainHeight * 0.55f, recipe.mountainHeight, erosion);
        float spawnDistance = Vector2.Distance(new Vector2(x, z), new Vector2(recipe.safeSpawn.x, recipe.safeSpawn.z));
        return Mathf.Lerp(recipe.safeSpawn.y, h, Mathf.SmoothStep(0f, 1f, (spawnDistance - 22f) / 55f));
    }

    public float Sample(Vector3 p)
    {
        // All consumers (mesh, brushes, erosion and queries) evaluate this identical frozen base lattice.
        float spacing = recipe.baseSampleSpacing;
        Vector3 g = p / spacing;
        Vector3Int lo = Vector3Int.FloorToInt(g);
        Vector3 f = g - lo;
        float result = 0f;
        for (int y=0; y<2; y++) for (int z=0; z<2; z++) for (int x=0; x<2; x++)
        {
            float weight = (x==0 ? 1f-f.x : f.x) * (y==0 ? 1f-f.y : f.y) * (z==0 ? 1f-f.z : f.z);
            if (weight <= 0f) continue;
            Vector3Int key = lo + new Vector3Int(x,y,z);
            if (!lattice.TryGetValue(key, out float value)) { value = Exact((Vector3)key * spacing); lattice.TryAdd(key,value); }
            result += weight * value;
        }
        return result;
    }

    private float Exact(Vector3 p)
    {
        float height = Height(p.x, p.z);
        float d = height - p.y;
        float safe = Mathf.SmoothStep(0f, 1f, (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(recipe.safeSpawn.x, recipe.safeSpawn.z)) - 28f) / 38f);
        // Blending independent lower/upper noise fields makes shelves, arches and detached crags.
        // A shallow vertical gradient is the crucial amplified feature: 3D noise can defeat gravity's bias.
        if (Mathf.Abs(d) < 115f)
        {
            Vector3 q = Vector3.Scale(p, new Vector3(0.012f, 0.019f, 0.012f)) + offset;
            float lower = Fbm(q, 3), upper = Fbm(q + Vector3.one * 47f, 3);
            float blend = Mathf.Clamp01(Noise(q * 0.48f + Vector3.one * 19f) * 0.9f + 0.5f);
            d += Mathf.Lerp(lower, upper, blend) * recipe.overhangStrength * safe;
        }
        if (d > -18f && p.y > -65f && p.y < height + 38f && safe > 0f)
        {
            Vector3 q = Vector3.Scale(p, new Vector3(0.012f, 0.021f, 0.012f)) + offset;
            float cheese = (Mathf.Abs(Fbm(q + Vector3.one * 83f, 2)) - recipe.cavernWidth) * 105f;
            float a = Noise(q * 1.9f + Vector3.one * 103f);
            float b = Noise(q * 1.9f + Vector3.one * 151f);
            float spaghetti = (Mathf.Sqrt(a * a + b * b) - 0.18f) * 58f;
            float cavity = Mathf.Min(cheese, spaghetti);
            // Solid basement and a protected overlook, even where caves open through cliffs.
            cavity = Mathf.Max(cavity, (-65f - p.y) * 2f);
            d = Mathf.Lerp(d, Mathf.Min(d, cavity), safe);
        }
        if (p.y > 195f)
        {
            foreach (Island island in Islands)
            {
                float dx = (p.x - island.centre.x) / island.radius.x;
                float dz = (p.z - island.centre.z) / island.radius.y;
                if (Mathf.Abs(dx) > 1.2f || Mathf.Abs(dz) > 1.2f || p.y > island.centre.y + 15f) continue;
                float r = Mathf.Sqrt(dx * dx + dz * dz);
                float rough = Noise(p * 0.045f + offset) * 3.5f;
                float edge = (1f - r) * Mathf.Min(island.radius.x, island.radius.y) + rough;
                float top = island.centre.y + Noise(new Vector3(p.x * 0.025f, 7f, p.z * 0.025f) + offset) * 4f;
                float bottom = island.centre.y - 9f - Mathf.Clamp01(1f - Mathf.Pow(r, 1.3f)) * island.depth;
                d = Mathf.Max(d, Mathf.Min(edge, Mathf.Min(top - p.y, p.y - bottom + rough)));
            }
        }
        return d;
    }

    private static float Fbm(Vector3 p, int octaves)
    {
        float sum = 0f, amplitude = 0.57f;
        for (int i = 0; i < octaves; i++) { sum += Noise(p) * amplitude; p = p * 2.03f + Vector3.one * 17.3f; amplitude *= 0.5f; }
        return sum;
    }

    private static float Noise(Vector3 p) => ProceduralWorld.TerrainNoise(p);
}
