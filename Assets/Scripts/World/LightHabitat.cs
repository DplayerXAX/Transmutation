using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>A local light switch for the sheltered luminous-creature habitat.</summary>
public sealed class LightHabitat : MonoBehaviour
{
    public Light habitatLight;
    private SmoothFirstPersonController player;
    private void Start()
    {
        foreach (var candidate in FindObjectsByType<SmoothFirstPersonController>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == gameObject.scene) { player = candidate; break; }
    }
    private bool InReach => player != null && player.enabled && (player.transform.position - transform.position).sqrMagnitude < 12f * 12f;
    private void Update()
    {
        if (InReach && habitatLight != null && Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
            ToggleLight();
    }
    public void ToggleLight()
    {
        if (habitatLight == null) return;
        habitatLight.enabled = !habitatLight.enabled;
        AmbientLuminosity.Invalidate();
    }
    private void OnGUI()
    {
        if (InReach) GUI.Box(new Rect(20f, 20f, 380f, 45f), "Luminous habitat — [L] toggle habitat light");
    }
}
