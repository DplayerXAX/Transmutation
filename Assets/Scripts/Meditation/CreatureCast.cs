using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// A frozen copy of how a creature looked when it was first met: one mesh per renderer,
/// with that renderer's own materials, at the creature's real size (base on y = 0).
/// Saved to disk as meshes plus material names; materials are found again by name.
/// </summary>
public sealed class CreatureCast
{
    public sealed class Part
    {
        public Mesh mesh;
        public Material[] materials;
        public string[] materialNames;
        public MaterialPropertyBlock block;
    }

    public readonly List<Part> parts = new List<Part>();
    public Bounds bounds;

    private const int FileVersion = 2;

    /// <summary>Copies every visible mesh of the creature. Returns null if nothing could be copied.</summary>
    public static CreatureCast Capture(Creature creature, float largestSize)
    {
        Transform root = creature.transform;
        Matrix4x4 toLocal = Matrix4x4.TRS(root.position, Quaternion.Euler(0f, root.eulerAngles.y, 0f), Vector3.one).inverse;
        var cast = new CreatureCast();
        int vertexCount = 0;

        foreach (Renderer renderer in creature.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            Mesh source;
            Matrix4x4 matrix;
            bool baked = false;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                if (skinned.sharedMesh == null) continue;
                source = new Mesh();
                skinned.BakeMesh(source, true);
                baked = true;
                matrix = toLocal * Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
            }
            else if (renderer is MeshRenderer)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
                source = filter.sharedMesh;
                matrix = toLocal * renderer.transform.localToWorldMatrix;
            }
            else continue;

            vertexCount += source.vertexCount;
            if (vertexCount > 400000)
            {
                if (baked) UnityEngine.Object.Destroy(source);
                break;
            }

            try
            {
                // Keep sub-meshes separate so each keeps its own material.
                var pieces = new CombineInstance[source.subMeshCount];
                for (int s = 0; s < pieces.Length; s++)
                    pieces[s] = new CombineInstance { mesh = source, subMeshIndex = s, transform = matrix };
                var mesh = new Mesh { name = renderer.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(pieces, false, true);

                Material[] materials = renderer.sharedMaterials;
                var part = new Part
                {
                    mesh = mesh,
                    materials = materials,
                    materialNames = Array.ConvertAll(materials, m => m != null ? CleanName(m.name) : "")
                };
                if (renderer.HasPropertyBlock())
                {
                    part.block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(part.block);
                }
                cast.parts.Add(part);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not copy " + renderer.name + " for a cast: " + e.Message);
            }
            if (baked) UnityEngine.Object.Destroy(source);
        }

        if (cast.parts.Count == 0) return null;
        cast.Normalize(largestSize);
        return cast;
    }

    /// <summary>Puts the base on y = 0 and shrinks very large creatures to fit.</summary>
    private void Normalize(float largestSize)
    {
        Bounds all = default;
        bool first = true;
        foreach (Part part in parts)
        {
            part.mesh.RecalculateBounds();
            if (first) all = part.mesh.bounds;
            else all.Encapsulate(part.mesh.bounds);
            first = false;
        }
        float largest = Mathf.Max(all.size.x, Mathf.Max(all.size.y, all.size.z));
        float scale = largest > largestSize ? largestSize / largest : 1f;
        Vector3 pivot = new Vector3(all.center.x, all.min.y, all.center.z);
        foreach (Part part in parts)
        {
            Vector3[] vertices = part.mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = (vertices[i] - pivot) * scale;
            part.mesh.vertices = vertices;
            if (part.mesh.normals == null || part.mesh.normals.Length != vertices.Length) part.mesh.RecalculateNormals();
            part.mesh.RecalculateBounds();
        }
        bounds = new Bounds((all.center - pivot) * scale, all.size * scale);
    }

    /// <summary>Lumpy stand-in for a creature whose meshes could not be copied.</summary>
    public static CreatureCast Pebble(int seed, float size)
    {
        Mesh mesh = MeditationShapes.Pebble(seed, size);
        var cast = new CreatureCast { bounds = mesh.bounds };
        cast.parts.Add(new Part { mesh = mesh, materials = new Material[1], materialNames = new[] { "" } });
        return cast;
    }

    private static string CleanName(string name) => name.Replace(" (Instance)", "").Trim();

    // ---------- Files ----------

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var writer = new BinaryWriter(File.Open(path, FileMode.Create)))
            {
                writer.Write(FileVersion);
                writer.Write(parts.Count);
                foreach (Part part in parts) WritePart(writer, part);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("Could not save cast " + path + ": " + e.Message);
        }
    }

    public static CreatureCast Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                // Version 1 files held a single white mesh; make a new cast instead.
                if (reader.ReadInt32() != FileVersion) return null;
                var cast = new CreatureCast();
                int count = reader.ReadInt32();
                Dictionary<string, Material> lookup = LoadedMaterials();
                for (int i = 0; i < count; i++) cast.parts.Add(ReadPart(reader, lookup));
                bool first = true;
                foreach (Part part in cast.parts)
                {
                    if (first) cast.bounds = part.mesh.bounds;
                    else cast.bounds.Encapsulate(part.mesh.bounds);
                    first = false;
                }
                return cast.parts.Count > 0 ? cast : null;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("Could not read cast " + path + ": " + e.Message);
            return null;
        }
    }

    private static void WritePart(BinaryWriter writer, Part part)
    {
        Mesh mesh = part.mesh;
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector2[] uvs = mesh.uv;
        Color32[] colors = mesh.colors32;

        writer.Write(vertices.Length);
        foreach (Vector3 v in vertices) WriteVector(writer, v);
        writer.Write(normals.Length == vertices.Length);
        if (normals.Length == vertices.Length) foreach (Vector3 n in normals) WriteVector(writer, n);
        writer.Write(uvs.Length == vertices.Length);
        if (uvs.Length == vertices.Length)
            foreach (Vector2 uv in uvs) { writer.Write(uv.x); writer.Write(uv.y); }
        writer.Write(colors.Length == vertices.Length);
        if (colors.Length == vertices.Length)
            foreach (Color32 c in colors) { writer.Write(c.r); writer.Write(c.g); writer.Write(c.b); writer.Write(c.a); }

        writer.Write(mesh.subMeshCount);
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            int[] triangles = mesh.GetTriangles(s);
            writer.Write(triangles.Length);
            foreach (int t in triangles) writer.Write(t);
            writer.Write(part.materialNames != null && s < part.materialNames.Length ? part.materialNames[s] : "");
        }
    }

    private static Part ReadPart(BinaryReader reader, Dictionary<string, Material> lookup)
    {
        int count = reader.ReadInt32();
        var vertices = new Vector3[count];
        for (int i = 0; i < count; i++) vertices[i] = ReadVector(reader);
        Vector3[] normals = null;
        if (reader.ReadBoolean())
        {
            normals = new Vector3[count];
            for (int i = 0; i < count; i++) normals[i] = ReadVector(reader);
        }
        Vector2[] uvs = null;
        if (reader.ReadBoolean())
        {
            uvs = new Vector2[count];
            for (int i = 0; i < count; i++) uvs[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        }
        Color32[] colors = null;
        if (reader.ReadBoolean())
        {
            colors = new Color32[count];
            for (int i = 0; i < count; i++)
                colors[i] = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
        }

        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = vertices;
        if (normals != null) mesh.normals = normals;
        if (uvs != null) mesh.uv = uvs;
        if (colors != null) mesh.colors32 = colors;

        int subMeshes = reader.ReadInt32();
        mesh.subMeshCount = subMeshes;
        var names = new string[subMeshes];
        var materials = new Material[subMeshes];
        for (int s = 0; s < subMeshes; s++)
        {
            var triangles = new int[reader.ReadInt32()];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = reader.ReadInt32();
            mesh.SetTriangles(triangles, s);
            names[s] = reader.ReadString();
            lookup.TryGetValue(names[s], out materials[s]);
        }
        if (normals == null) mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return new Part { mesh = mesh, materials = materials, materialNames = names };
    }

    // Every material currently loaded, by name (scene creatures keep theirs loaded).
    private static Dictionary<string, Material> LoadedMaterials()
    {
        var lookup = new Dictionary<string, Material>();
        foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
        {
            if (material == null) continue;
            string name = CleanName(material.name);
            if (!lookup.ContainsKey(name)) lookup[name] = material;
        }
        return lookup;
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
