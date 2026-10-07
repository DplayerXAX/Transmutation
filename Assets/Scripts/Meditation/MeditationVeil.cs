using UnityEngine;

/// <summary>
/// The inside-out paper sphere that erases the world. Shrinking it hides everything
/// beyond its surface; growing it brings things back from near to far.
/// </summary>
public sealed class MeditationVeil
{
    private readonly Transform transform;
    private readonly Material material;
    private readonly MeshRenderer renderer;

    public float Wobble { get; }

    public MeditationVeil(Material source, Transform parent)
    {
        material = source != null ? new Material(source) : new Material(Shader.Find("Capstone/Meditation/Veil"));
        material.name = "Meditation Veil (runtime)";
        Wobble = material.HasProperty("_Wobble") ? material.GetFloat("_Wobble") : 0.25f;

        var go = new GameObject("Meditation Veil");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = MeditationShapes.UnitSphere();
        renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        transform = go.transform;
        go.SetActive(false);
    }

    public bool Visible
    {
        get => transform.gameObject.activeSelf;
        set => transform.gameObject.SetActive(value);
    }

    public void Set(Vector3 centre, float radius, float cover)
    {
        transform.position = centre;
        transform.localScale = Vector3.one * Mathf.Max(0.01f, radius);
        material.SetFloat("_Cover", Mathf.Clamp01(cover));
    }

    public void Destroy()
    {
        if (transform != null) Object.Destroy(transform.gameObject);
        if (material != null) Object.Destroy(material);
    }
}
