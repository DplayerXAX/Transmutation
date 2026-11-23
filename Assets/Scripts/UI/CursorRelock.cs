using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Puts the hidden, locked mouse back after the game window loses it (alt-tab, Escape in the editor, clicking another
/// window). While the first-person player is walking (its controller enabled), the game wants the mouse locked: when
/// the window gets focus back, and on any click into the game, the mouse is locked and hidden again.
/// Screens that free the mouse on purpose turn the walking controller off (reflection space) or have no player
/// (main menu), so they are left alone; showcase scenes with their own mouse menus are skipped.
/// Runs by itself in every scene.
/// </summary>
public sealed class CursorRelock : MonoBehaviour
{
    private SmoothFirstPersonController player;
    private bool skipScene, hadFocus = true;
    private float nextLook;
    private int refocusFrames;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindFirstObjectByType<CursorRelock>() != null) return;
        var go = new GameObject("Cursor Relock");
        DontDestroyOnLoad(go);
        go.AddComponent<CursorRelock>();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextLook || player == null)
        {
            nextLook = Time.unscaledTime + 1f;
            player = FindFirstObjectByType<SmoothFirstPersonController>();
            skipScene = FindFirstObjectByType<AsuraShowcase>() != null || FindFirstObjectByType<LeafGardenShowcase>() != null ||
                        FindFirstObjectByType<ErosionDemoControls>() != null;
        }
        if (!Application.isFocused) { hadFocus = false; return; }
        if (!hadFocus) { hadFocus = true; refocusFrames = 2; }

        bool wantLocked = !skipScene && player != null && player.isActiveAndEnabled;
        if (!wantLocked) return;
        bool loose = Cursor.lockState != CursorLockMode.Locked || Cursor.visible;
        Mouse mouse = Mouse.current;
        bool click = mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame);

        if (refocusFrames > 0 && --refocusFrames == 0) Lock();
        else if (click && loose) Lock();
    }

    private static void Lock()
    {
        // Letting go first makes the editor really take the mouse again when it still reports "locked".
        Cursor.lockState = CursorLockMode.None;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
