using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>One creature the player has met, as stored on disk.</summary>
[Serializable]
public sealed class JournalEntry
{
    public string key;
    public string displayName;
    public string firstSeen;
    public Vector3 encounterPosition;

    // Where the player put the cast inside the reflection space (space-local).
    public bool placed;
    public Vector3 castPosition;
    public float castYaw;
}

[Serializable]
public sealed class JournalData
{
    public List<JournalEntry> entries = new List<JournalEntry>();
}

/// <summary>
/// Files for the reflection space: drawings, the creature journal and creature casts.
/// Everything lives under persistentDataPath/Reflection so it survives between play sessions.
/// </summary>
public static class ReflectionStorage
{
    public static string Root => Path.Combine(Application.persistentDataPath, "Reflection");
    public static string JournalPath => Path.Combine(Root, "journal.json");
    public static string DrawingPath(string id) => Path.Combine(Root, "drawings", id + ".png");
    public static string CastPath(string key) => Path.Combine(Root, "casts", key + ".mesh");

    public static JournalData LoadJournal()
    {
        try
        {
            if (File.Exists(JournalPath))
                return JsonUtility.FromJson<JournalData>(File.ReadAllText(JournalPath)) ?? new JournalData();
        }
        catch (Exception e)
        {
            Debug.LogWarning("Could not read the creature journal: " + e.Message);
        }
        return new JournalData();
    }

    public static void SaveJournal(JournalData data)
    {
        try
        {
            EnsureFolder(JournalPath);
            File.WriteAllText(JournalPath, JsonUtility.ToJson(data, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning("Could not save the creature journal: " + e.Message);
        }
    }

    public static void SaveBytes(string path, byte[] bytes)
    {
        try
        {
            EnsureFolder(path);
            File.WriteAllBytes(path, bytes);
        }
        catch (Exception e)
        {
            Debug.LogWarning("Could not save " + path + ": " + e.Message);
        }
    }

    public static byte[] LoadBytes(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception e)
        {
            Debug.LogWarning("Could not read " + path + ": " + e.Message);
            return null;
        }
    }

    // Simple binary mesh: positions, normals, triangles.
    public static void SaveMesh(Mesh mesh, string path)
    {
        try
        {
            EnsureFolder(path);
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.triangles;
            using (var writer = new BinaryWriter(File.Open(path, FileMode.Create)))
            {
                writer.Write(1);
                writer.Write(vertices.Length);
                for (int i = 0; i < vertices.Length; i++)
                {
                    WriteVector(writer, vertices[i]);
                    WriteVector(writer, i < normals.Length ? normals[i] : Vector3.up);
                }
                writer.Write(triangles.Length);
                for (int i = 0; i < triangles.Length; i++) writer.Write(triangles[i]);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("Could not save cast " + path + ": " + e.Message);
        }
    }

    public static Mesh LoadMesh(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                reader.ReadInt32();
                int count = reader.ReadInt32();
                var vertices = new Vector3[count];
                var normals = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    vertices[i] = ReadVector(reader);
                    normals[i] = ReadVector(reader);
                }
                var triangles = new int[reader.ReadInt32()];
                for (int i = 0; i < triangles.Length; i++) triangles[i] = reader.ReadInt32();

                var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = vertices;
                mesh.normals = normals;
                mesh.triangles = triangles;
                mesh.RecalculateBounds();
                return mesh;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("Could not read cast " + path + ": " + e.Message);
            return null;
        }
    }

    private static void EnsureFolder(string filePath)
    {
        string folder = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
    }

    private static void WriteVector(BinaryWriter writer, Vector3 v)
    {
        writer.Write(v.x);
        writer.Write(v.y);
        writer.Write(v.z);
    }

    private static Vector3 ReadVector(BinaryReader reader)
    {
        return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }
}
