using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Puts each learned landmark glyph on a stele of the reflection space, high on its face,
/// so the drawing area below stays free. Put next to the ReflectionSpace (the Meditation object).
/// </summary>
[DisallowMultipleComponent]
public sealed class SteleInscriptions : MonoBehaviour
{
    [SerializeField] private ReflectionSpace space;
    [SerializeField] private Material glyphMaterial;
    [Min(0.1f)] [SerializeField] private float glyphSize = 0.42f;
    [Tooltip("Height of the glyph centre on the stele, metres.")]
    [SerializeField] private float glyphHeight = 1.9f;

    private readonly HashSet<string> placed = new HashSet<string>();

    private void Awake()
    {
        if (space == null) space = GetComponent<ReflectionSpace>();
        if (space == null) space = FindFirstObjectByType<ReflectionSpace>();
    }

    private void LateUpdate()
    {
        if (space == null || space.Root == null || !space.Root.gameObject.activeInHierarchy) return;
        IReadOnlyList<string> glyphs = LandmarkProgress.Glyphs;
        if (placed.Count >= glyphs.Count) return;
        for (int i = 0; i < glyphs.Count; i++)
            if (!placed.Contains(glyphs[i]) && Place(glyphs[i], i))
                placed.Add(glyphs[i]);
    }

    private bool Place(string id, int order)
    {
        // Stele 0 stands straight ahead on arrival; glyphs start from the next one.
        var steles = new List<Transform>();
        foreach (Transform child in space.Root)
            if (child.name.StartsWith("Stele ")) steles.Add(child);
        if (steles.Count == 0) return false;
        Transform stele = steles[(order + 1) % steles.Count];

        // Find the stone's front face with a ray, so the glyph sits right on it.
        Vector3 from = stele.position + stele.forward * 2f + Vector3.up * glyphHeight;
        Vector3 point = stele.position + stele.forward * 0.3f + Vector3.up * glyphHeight;
        Vector3 normal = stele.forward;
        foreach (RaycastHit hit in Physics.RaycastAll(from, -stele.forward, 3f, ~0, QueryTriggerInteraction.Ignore))
            if (hit.collider.transform.IsChildOf(stele))
            {
                point = hit.point;
                normal = hit.normal;
                break;
            }

        Mesh mesh = LandmarkShapes.Glyph(LandmarkGlyphs.Get(id), glyphSize, glyphSize * 0.09f);
        var go = new GameObject("Inscription " + id) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(stele, true);
        go.transform.SetPositionAndRotation(point + normal * 0.015f - Vector3.up * (0.7f * glyphSize), Quaternion.LookRotation(-normal, Vector3.up));
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = glyphMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return true;
    }

    public void SetMaterial(Material material) => glyphMaterial = material;
}
