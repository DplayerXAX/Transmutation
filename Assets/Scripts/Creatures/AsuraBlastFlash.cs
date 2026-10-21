using UnityEngine;

public sealed class AsuraBlastFlash : MonoBehaviour
{
    public float Radius { get; set; } = 4f;
    private float elapsed;
    private void Update()
    {
        elapsed += Time.deltaTime;
        transform.localScale = Vector3.one * (Radius * 2f * Mathf.Clamp01(elapsed / 0.12f));
        if (elapsed >= 0.22f) Destroy(gameObject);
    }
}
