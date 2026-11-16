using UnityEngine;

/// <summary>Ends the starting cave's interaction tutorial once the player reaches daylight.</summary>
public sealed class WorldMapPromptGate : MonoBehaviour
{
    private SmoothFirstPersonController player;
    private InteractionPromptHUD prompts;
    private CrosshairHUD crosshair;
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
        if (crosshair == null) crosshair = FindFirstObjectByType<CrosshairHUD>();
        if (prompts != null && prompts.HintsVisible) prompts.SetHintsVisible(false);
        if (crosshair != null) crosshair.SetActionCuesVisible(false);
    }
}
