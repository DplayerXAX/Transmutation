using UnityEngine;

/// <summary>Player adapter for the same luminosity-to-light driver used by Glow Bees.</summary>
[RequireComponent(typeof(PlayerCreatureStats)), DisallowMultipleComponent]
public sealed class PlayerLuminosityVisuals : MonoBehaviour
{
    public static readonly Color Tint = new Color(0.6f, 1f, 0.25f);
    private GameObject lightRoot;
    public Light Lamp => lightRoot != null ? lightRoot.GetComponent<Light>() : null;
    public void Refresh() { if (lightRoot != null) lightRoot.GetComponent<GlowBeeLight>().Refresh(); }

    private void Awake()
    {
        lightRoot = new GameObject("Player luminosity");
        lightRoot.transform.SetParent(transform, false);
        lightRoot.transform.localPosition = Vector3.up * 0.5f;
        lightRoot.AddComponent<GlowBeeLight>().Configure(null, Tint, 5f);
    }
    private void OnEnable() { if (lightRoot != null) lightRoot.SetActive(true); }
    private void OnDisable() { if (lightRoot != null) lightRoot.SetActive(false); }
    private void OnDestroy() { if (lightRoot != null) Destroy(lightRoot); }
}
