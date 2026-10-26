using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

/// <summary>
/// A saved, hand-sculpted ProceduralWorld. Holds the frozen layout (every generation setting, the noise offset and
/// the sinkholes at the moment it was baked) plus the sculpt brush edits as density changes on the world's sample
/// grid. With this asset on a ProceduralWorld, the world is built from it instead of from the inspector settings,
/// so the terrain stays the same until it is sculpted again.
///
/// Edits are kept in memory as a dictionary and packed (gzip + base64) only when Unity saves the asset.
/// Made by Tools > Capstone > Terrain > Bake Open Scene World To Sculpted Terrain.
/// </summary>
public sealed class TerrainSculptData : ScriptableObject, ISerializationCallbackReceiver
{
    [Tooltip("Extra empty space above the highest hill, so sculpted peaks are not cut off (m). Safe to raise later.")]
    [Min(0f)] [SerializeField] private float headroom = 24f;

    [Header("Saved Data (Read Only)")]
    [SerializeField] private int editedSamples;
    [SerializeField] private string bakedFrom;
    [SerializeField, HideInInspector] private string layout;
    [SerializeField, HideInInspector] private float[] checks;
    [SerializeField, HideInInspector] private string packedEdits;
    [SerializeField, HideInInspector] private int revision;

    private readonly Dictionary<Vector3Int, float> edits = new Dictionary<Vector3Int, float>();
    private bool dirty;

    public bool HasLayout => !string.IsNullOrEmpty(layout);
    public string Layout => layout;
    public float Headroom => headroom;
    /// <summary>Densities at fixed points when baked; a mismatch means the generator code changed since.</summary>
    public float[] Checks => checks;
    public IReadOnlyDictionary<Vector3Int, float> Edits => edits;
    /// <summary>Goes up with every change; undo brings back the old number.</summary>
    public int Revision => revision;

    /// <summary>Freezes a new layout and drops every sculpt edit.</summary>
    public void Freeze(string layoutJson, float[] checkValues, string source)
    {
        layout = layoutJson;
        checks = checkValues;
        bakedFrom = source;
        edits.Clear();
        revision++;
        dirty = true;
    }

    /// <summary>Replaces the frozen layout but keeps every sculpt edit (used to move a sinkhole).</summary>
    public void SetLayout(string layoutJson, float[] checkValues)
    {
        layout = layoutJson;
        checks = checkValues;
        revision++;
        dirty = true;
    }

    /// <summary>Sets the density change at one grid sample; zero removes it.</summary>
    public void SetEdit(Vector3Int sample, float delta)
    {
        if (Mathf.Abs(delta) < 1e-4f) edits.Remove(sample);
        else edits[sample] = delta;
        revision++;
        dirty = true;
    }

    public void ClearEdits()
    {
        edits.Clear();
        revision++;
        dirty = true;
    }

    public void OnBeforeSerialize()
    {
        if (!dirty) return;
        packedEdits = Pack(edits);
        editedSamples = edits.Count;
        dirty = false;
    }

    public void OnAfterDeserialize()
    {
        Unpack(packedEdits, edits);
        dirty = false;
    }

    // Format: count, then x, y, z as shorts and the delta as a float, all gzipped.
    private static string Pack(Dictionary<Vector3Int, float> source)
    {
        if (source.Count == 0) return string.Empty;
        using var memory = new MemoryStream();
        using (var zip = new GZipStream(memory, System.IO.Compression.CompressionLevel.Optimal, true))
        using (var writer = new BinaryWriter(zip))
        {
            writer.Write(source.Count);
            foreach (var pair in source)
            {
                writer.Write((short)pair.Key.x);
                writer.Write((short)pair.Key.y);
                writer.Write((short)pair.Key.z);
                writer.Write(pair.Value);
            }
        }
        return Convert.ToBase64String(memory.ToArray());
    }

    private static void Unpack(string packed, Dictionary<Vector3Int, float> target)
    {
        target.Clear();
        if (string.IsNullOrEmpty(packed)) return;
        try
        {
            using var memory = new MemoryStream(Convert.FromBase64String(packed));
            using var zip = new GZipStream(memory, CompressionMode.Decompress);
            using var reader = new BinaryReader(zip);
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var key = new Vector3Int(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
                target[key] = reader.ReadSingle();
            }
        }
        catch (Exception error)
        {
            Debug.LogError("Sculpted terrain edits could not be read: " + error.Message);
            target.Clear();
        }
    }
}
