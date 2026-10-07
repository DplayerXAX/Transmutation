using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// What the player has taken from landmarks: seeds and inscriptions (glyph ids), saved next to
/// the reflection journal so it survives between sessions.
/// </summary>
public static class LandmarkProgress
{
    [Serializable]
    private sealed class Data
    {
        public List<string> seeds = new List<string>();
        public List<string> glyphs = new List<string>();
    }

    private static Data data;

    /// <summary>Raised after a seed or inscription is collected.</summary>
    public static event Action Changed;

    public static string FilePath => Path.Combine(Application.persistentDataPath, "Reflection", "landmarks.json");

    public static int SeedCount => Current.seeds.Count;
    public static IReadOnlyList<string> Glyphs => Current.glyphs;

    public static bool HasSeed(string id) => Current.seeds.Contains(id);
    public static bool HasGlyph(string id) => Current.glyphs.Contains(id);

    public static void AddSeed(string id)
    {
        if (string.IsNullOrEmpty(id) || HasSeed(id)) return;
        Current.seeds.Add(id);
        Save();
    }

    public static void AddGlyph(string id)
    {
        if (string.IsNullOrEmpty(id) || HasGlyph(id)) return;
        Current.glyphs.Add(id);
        Save();
    }

    public static void ResetAll()
    {
        data = new Data();
        if (File.Exists(FilePath)) File.Delete(FilePath);
        Changed?.Invoke();
    }

    private static Data Current
    {
        get
        {
            if (data == null) Load();
            return data;
        }
    }

    private static void Load()
    {
        data = new Data();
        try
        {
            if (File.Exists(FilePath)) data = JsonUtility.FromJson<Data>(File.ReadAllText(FilePath)) ?? new Data();
        }
        catch (Exception e)
        {
            Debug.LogWarning("Could not read landmark progress: " + e.Message);
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning("Could not save landmark progress: " + e.Message);
        }
        Changed?.Invoke();
    }

    // Domain reload may be off in the editor; start each play session from the file.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        data = null;
        Changed = null;
    }
}

/// <summary>Stroke definitions of the landmark inscriptions, by glyph id.</summary>
public static class LandmarkGlyphs
{
    /// <summary>Strokes in a box about 1 wide and 1.4 tall, base at y = 0.</summary>
    public static IReadOnlyList<Vector2[]> Get(string id)
    {
        switch (id)
        {
            case "spire":
                // The tower, the broken ground line with its hole, and the tower's reflection below.
                return new List<Vector2[]>
                {
                    new[] { new Vector2(0f, 0.05f), new Vector2(0f, 1.35f) },
                    new[] { new Vector2(-0.3f, 1.0f), new Vector2(0f, 1.35f), new Vector2(0.24f, 1.08f) },
                    new[] { new Vector2(-0.24f, 0.32f), new Vector2(0f, 0.05f), new Vector2(0.3f, 0.4f) },
                    new[] { new Vector2(-0.48f, 0.7f), new Vector2(-0.12f, 0.7f) },
                    new[] { new Vector2(0.12f, 0.7f), new Vector2(0.42f, 0.74f) },
                    new[] { new Vector2(0.36f, 1.22f) },
                };
            default:
                return Random(id);
        }
    }

    /// <summary>A simple made-up glyph for ids without a hand-made one.</summary>
    private static IReadOnlyList<Vector2[]> Random(string id)
    {
        var random = new System.Random(id != null ? id.GetHashCode() : 0);
        float R(float min, float max) => min + (float)random.NextDouble() * (max - min);
        var strokes = new List<Vector2[]>
        {
            new[] { new Vector2(R(-0.2f, 0.2f), 0.05f), new Vector2(R(-0.2f, 0.2f), 1.35f) },
        };
        int extra = random.Next(2, 4);
        for (int i = 0; i < extra; i++)
        {
            float y = R(0.2f, 1.2f);
            switch (random.Next(3))
            {
                case 0: strokes.Add(new[] { new Vector2(-0.4f, y), new Vector2(0.4f, y + R(-0.15f, 0.15f)) }); break;
                case 1: strokes.Add(LandmarkShapes.Ring(new Vector2(R(-0.3f, 0.3f), y), R(0.08f, 0.16f), 8)); break;
                default: strokes.Add(new[] { new Vector2(R(-0.4f, 0f), y), new Vector2(R(0f, 0.4f), y + 0.2f), new Vector2(R(0f, 0.4f), y - 0.1f) }); break;
            }
        }
        return strokes;
    }
}
