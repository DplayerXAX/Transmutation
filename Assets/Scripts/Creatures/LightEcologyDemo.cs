using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Controls for the dedicated light-ecology playground only.</summary>
public sealed class LightEcologyDemo : MonoBehaviour
{
    public Light roomLight;
    private Material honeyMaterial;
    private void Start()
    {
        honeyMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        honeyMaterial.SetColor("_BaseColor", new Color(0.7f, 1f, 0.2f));
        honeyMaterial.EnableKeyword("_EMISSION");
        for (int i = 0; i < 3; i++)
        {
            var root = new GameObject("Honey — pick up and press F");
            root.transform.position = new Vector3(-1f + i * 0.6f, 0.4f, -2f);
            root.AddComponent<GlowHoney>();
            var bead = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bead.transform.SetParent(root.transform, false);
            bead.transform.localScale = Vector3.one * 0.35f;
            Destroy(bead.GetComponent<Collider>());
            var renderer = bead.GetComponent<Renderer>();
            renderer.sharedMaterial = honeyMaterial;
            bead.AddComponent<GlowBeeLight>().Configure(renderer, new Color(0.7f, 1f, 0.2f), 2f);
        }
    }
    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame && roomLight != null)
        { roomLight.enabled = !roomLight.enabled; AmbientLuminosity.Invalidate(); }
    }
    private void OnGUI()
    {
        GUI.Box(new Rect(15f, 15f, 580f, 90f), "LIGHT ECOLOGY\nRMB: pick up honey    F: coat yourself    L: toggle room light\nDark: moths approach your glow. Bright: they lose interest.\nThrow the Flash Bloom near a green pod: it stores the flash, then glows.");
    }
    private void OnDestroy() { if (honeyMaterial != null) Destroy(honeyMaterial); }
}
