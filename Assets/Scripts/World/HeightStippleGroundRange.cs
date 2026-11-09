using System.Collections.Generic;
using UnityEngine;

/// <summary>Calibrates height dots to the actual surface submesh, excluding inner-world geometry.</summary>
[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(ProceduralWorld))]
public sealed class HeightStippleGroundRange : MonoBehaviour
{
    private static readonly int HeightLow = Shader.PropertyToID("_HeightLow");
    private static readonly int HeightHigh = Shader.PropertyToID("_HeightHigh");
    private readonly Dictionary<Renderer, Vector2> ranges = new Dictionary<Renderer, Vector2>();
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<int> triangles = new List<int>();
    private readonly List<Renderer> removed = new List<Renderer>();
    private MaterialPropertyBlock block;
    private ProceduralWorld world;
    private bool dirty;
    private float nextUpdate;
    private Matrix4x4 lastTransform;

    private void OnEnable()
    {
        block = new MaterialPropertyBlock();
        world = GetComponent<ProceduralWorld>();
        world.ChunkCreated += OnChunkCreated;
        world.ChunkUpdated += OnChunkUpdated;
        lastTransform = transform.localToWorldMatrix;
        RebuildRanges();
    }

    private void OnDisable()
    {
        if (world != null)
        {
            world.ChunkCreated -= OnChunkCreated;
            world.ChunkUpdated -= OnChunkUpdated;
        }
        foreach (var entry in ranges)
        {
            if (entry.Key == null) continue;
            var material = entry.Key.sharedMaterial;
            if (material == null) continue;
            entry.Key.GetPropertyBlock(block, 0);
            block.SetFloat(HeightLow, material.GetFloat(HeightLow));
            block.SetFloat(HeightHigh, material.GetFloat(HeightHigh));
            entry.Key.SetPropertyBlock(block, 0);
        }
        ranges.Clear();
    }

    private void RebuildRanges()
    {
        ranges.Clear();
        foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
            Register(filter.gameObject, filter.sharedMesh);
        dirty = true;
    }

    private void OnChunkCreated(GameObject chunk, Mesh mesh) => Register(chunk, mesh);
    private void OnChunkUpdated(GameObject chunk, Mesh mesh, Bounds changed) => Register(chunk, mesh);

    private void Register(GameObject chunk, Mesh mesh)
    {
        if (mesh == null || !mesh.isReadable || mesh.subMeshCount == 0) return;
        var renderer = chunk.GetComponent<Renderer>();
        if (renderer == null) return;
        var materials = renderer.sharedMaterials;
        if (materials.Length == 0 || materials[0] == null ||
            materials[0].shader == null || materials[0].shader.name != "Capstone/World/HeightStippleGround") return;
        mesh.GetVertices(vertices);
        mesh.GetTriangles(triangles, 0);
        float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
        foreach (int index in triangles)
        {
            float height = renderer.transform.TransformPoint(vertices[index]).y;
            minimum = Mathf.Min(minimum, height);
            maximum = Mathf.Max(maximum, height);
        }
        if (triangles.Count == 0) ranges.Remove(renderer);
        else ranges[renderer] = new Vector2(minimum, maximum);
        dirty = true;
    }

    private void LateUpdate()
    {
        if (lastTransform != transform.localToWorldMatrix)
        {
            lastTransform = transform.localToWorldMatrix;
            RebuildRanges();
        }
        if (!dirty || Time.realtimeSinceStartup < nextUpdate) return;
        nextUpdate = Time.realtimeSinceStartup + 0.25f;
        dirty = false;
        float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
        removed.Clear();
        foreach (var entry in ranges)
        {
            if (entry.Key == null) { removed.Add(entry.Key); continue; }
            minimum = Mathf.Min(minimum, entry.Value.x);
            maximum = Mathf.Max(maximum, entry.Value.y);
        }
        foreach (var renderer in removed) ranges.Remove(renderer);
        if (float.IsInfinity(minimum)) return;
        // A perfectly flat floor stays neutral grey.
        if (maximum - minimum < 0.01f) { minimum -= 0.5f; maximum += 0.5f; }
        foreach (var entry in ranges)
        {
            entry.Key.GetPropertyBlock(block, 0);
            block.SetFloat(HeightLow, minimum);
            block.SetFloat(HeightHigh, maximum);
            entry.Key.SetPropertyBlock(block, 0);
        }
    }
}
