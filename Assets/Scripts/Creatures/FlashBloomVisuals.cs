using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Visible flash independent of the ground shader's support for additional point lights.</summary>
public sealed class FlashBloomVisuals : MonoBehaviour
{
    private FlashBloom bloom;
    private Renderer halo;
    private Material material;
    private RaycastHit[] hits = new RaycastHit[8];
    private void Awake()
    {
        bloom = GetComponent<FlashBloom>();
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Flash halo";
        sphere.layer = 2;
        sphere.transform.position = transform.position;
        var collider = sphere.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
        halo = sphere.GetComponent<Renderer>();
        halo.shadowCastingMode = ShadowCastingMode.Off; halo.receiveShadows = false;
        material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Flash Bloom additive halo", renderQueue = 3000 };
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.One);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        halo.sharedMaterial = material; halo.enabled = false;
    }

    private void LateUpdate()
    {
        float age = bloom != null && bloom.IsAvailableForPerception ? bloom.FlashAge : float.PositiveInfinity;
        halo.enabled = age < 0.6f;
        if (!halo.enabled) return;
        halo.transform.position = bloom.EmissionPosition;
        halo.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 5f, Mathf.Clamp01(age / 0.6f));
        material.SetColor("_BaseColor", new Color(4f, 4f, 4f, Mathf.Clamp01(1f - age / 0.6f) * 0.65f));
    }

    public float GlareFor(Camera camera)
    {
        if (bloom == null || !bloom.IsAvailableForPerception || camera == null || bloom.FlashAge >= 0.45f) return 0f;
        Vector3 viewport = camera.WorldToViewportPoint(bloom.EmissionPosition);
        if (viewport.z <= 0f || viewport.x < -0.1f || viewport.x > 1.1f || viewport.y < -0.1f || viewport.y > 1.1f) return 0f;
        float distance = Vector3.Distance(camera.transform.position, bloom.EmissionPosition);
        if (distance >= 18f || CreatureSpatialQuery.FirstSolid(gameObject.scene, camera.transform.position, bloom.EmissionPosition,
            bloom, camera.transform.root.gameObject, ref hits, out _)) return 0f;
        return 0.8f * Mathf.Clamp01(1f - distance / 18f) * Mathf.Clamp01((0.45f - bloom.FlashAge) / 0.3f);
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        float alpha = GlareFor(Camera.main);
        if (alpha <= 0f) return;
        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = old;
    }

    private void OnDisable() { if (halo != null) halo.enabled = false; }
    private void OnDestroy()
    {
        if (halo != null) Destroy(halo.gameObject);
        if (material != null) Destroy(material);
    }
}
