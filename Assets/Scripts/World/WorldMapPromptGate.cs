using UnityEngine;

/// <summary>
/// Ends the starting cave's interaction tutorial (the text hints) once the player reaches daylight.
/// The crosshair keeps changing shape (pickup ring etc.) everywhere: that icon is the hint outside the cave.
/// </summary>
public sealed class WorldMapPromptGate : MonoBehaviour
{
    private SmoothFirstPersonController player;
    private InteractionPromptHUD prompts;
    private bool exited;

    private void Update()
    {
        if (player == null) player = FindFirstObjectByType<SmoothFirstPersonController>();
        if (player == null) return;
        Vector3 at = player.transform.position;
        Vector2 horizontal = new Vector2(at.x, at.z);
        if (!exited && at.y > -4f &&
            (at.x >= WorldMapTerrain.SpawnExit.x - 2f || Vector2.Distance(horizontal, WorldMapTerrain.Spawn) > 48f))
            exited = true;
        if (!exited) return;
        if (prompts == null) prompts = FindFirstObjectByType<InteractionPromptHUD>();
        if (prompts != null && prompts.HintsVisible) prompts.SetHintsVisible(false);
    }
}
