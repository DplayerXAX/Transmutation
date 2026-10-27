using UnityEngine;

/// <summary>A placeable gameplay volume that changes interaction hints for the entering player.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class InteractionHintsTrigger : MonoBehaviour
{
    [SerializeField] private bool hintsVisible;
    [SerializeField] private bool oneShot = true;
    [Tooltip("Optional. When empty, finds the HUD in the entering player's scene.")]
    [SerializeField] private InteractionPromptHUD hud;
    public bool HasTriggered { get; private set; }

    private void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    private void OnTriggerEnter(Collider other)
    {
        if (oneShot && HasTriggered) return;
        var player = other.GetComponentInParent<SmoothFirstPersonController>();
        if (player == null && other.attachedRigidbody != null)
            player = other.attachedRigidbody.GetComponent<SmoothFirstPersonController>();
        if (player == null || !player.isActiveAndEnabled) return;

        if (hud == null || hud.gameObject.scene != player.gameObject.scene)
        {
            hud = null;
            foreach (var candidate in FindObjectsByType<InteractionPromptHUD>(FindObjectsSortMode.None))
                if (candidate.gameObject.scene == player.gameObject.scene) { hud = candidate; break; }
        }
        if (hud == null) return;
        hud.SetHintsVisible(hintsVisible);
        HasTriggered = true;
    }
}
