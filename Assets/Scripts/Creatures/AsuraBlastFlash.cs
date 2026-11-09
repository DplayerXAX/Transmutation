using UnityEngine;

public sealed class AsuraBlastFlash : MonoBehaviour
{
    public float Radius { get; set; } = 4f;
    private float elapsed;
    private Renderer surface;
    private MaterialPropertyBlock properties;
    private void Start()
    {
        transform.localScale = Vector3.one * (Radius * 2f);
        surface = GetComponent<Renderer>(); properties = new MaterialPropertyBlock();
    }
    private void Update()
    {
        elapsed += Time.deltaTime;
        float fade = Mathf.Clamp01(1f - elapsed / 0.22f);
        properties.SetColor("_BaseColor", new Color(1f, 0.48f, 0.12f, fade * 0.2f));
        surface.SetPropertyBlock(properties);
        if (elapsed >= 0.22f) Destroy(gameObject);
    }
}
