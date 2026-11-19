using UnityEngine;
/// <summary>Keeps the existing creature membrane contours centred on a moving forest squirrel.</summary>
[DisallowMultipleComponent]
public sealed class ForestCreatureSurface : MonoBehaviour
{
    Renderer surface;
    MaterialPropertyBlock properties;
    Vector3 previousPosition;
    static readonly int Center = Shader.PropertyToID("_Center");
    static readonly int Activity = Shader.PropertyToID("_Activity");
    void Awake()
    {
        foreach (var candidate in GetComponentsInChildren<Renderer>(true))
            if (candidate.name == "Squirrel Visual") { surface = candidate; break; }
        properties = new MaterialPropertyBlock();
        previousPosition = transform.position;
    }
    void LateUpdate()
    {
        if (surface == null) return;
        float speed = (transform.position - previousPosition).magnitude / Mathf.Max(Time.deltaTime, .001f);
        previousPosition = transform.position;
        surface.GetPropertyBlock(properties);
        properties.SetVector(Center, surface.bounds.center);
        properties.SetFloat(Activity, Mathf.Clamp01(speed / 5f));
        surface.SetPropertyBlock(properties);
    }
}
