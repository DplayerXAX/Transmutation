using System;
using UnityEngine;

public enum TerrainRecipeKind { Original = 0, AmplifiedMountains = 1, GoldenMeadow = 2 }

/// <summary>Serializable shape recipe, frozen with TerrainSculptData. Tools and meshing belong to ProceduralWorld.</summary>
[Serializable]
public sealed class TerrainGenerationRecipe
{
    public TerrainRecipeKind kind;
    public int version = 1;
    [Min(1f)] public float mountainHeight = 310f;
    [Min(0.0001f)] public float mountainFrequency = 0.0035f;
    [Min(0f)] public float overhangStrength = 80f;
    [Range(0f, 0.3f)] public float cavernWidth = 0.085f;
    [Range(0, 64)] public int islandCount = 24;
    public Vector2 islandElevation = new Vector2(340f, 490f);
    public Vector2 islandRadius = new Vector2(40f, 85f);
    public Vector2 verticalBounds = new Vector2(-90f, 558f);
    [Tooltip("Fixed lattice spacing for the base recipe only; sculpting and erosion use the world's finer sample grid.")]
    [Min(1f)] public float baseSampleSpacing = 9f;
    public Vector3 safeSpawn = new Vector3(0f, 148f, -360f);
}
