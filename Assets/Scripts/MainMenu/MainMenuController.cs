using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Runs the 3D main menu. Mouse hover / click picks a MainMenuButton by raycast; arrow keys or A / D
/// (and a gamepad d-pad) move the selection, Enter / Space / gamepad south loads it, Esc quits.
/// The camera drifts slowly and leans toward the mouse. Loading fades the screen to black.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    public Camera menuCamera;
    public MainMenuButton[] buttons;

    [Header("UI")]
    [Tooltip("Full screen black image used for fades.")]
    public Image fade;
    [Tooltip("Short message line, e.g. when a scene is not built yet.")]
    public Text message;
    public float fadeTime = 0.8f;
    public float messageTime = 2.5f;

    [Header("Camera")]
    public float driftAmount = 0.25f;
    public float mouseLean = 0.5f;
    public Vector3 lookTarget = new Vector3(0f, 1.6f, 0f);

    private int selected = -1;
    private bool loading;
    private Vector2 lastMouse;
    private Vector3 cameraHome;
    private Vector2 lean;
    private float messageTimer;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 1f;
        if (menuCamera == null) menuCamera = Camera.main;
        if (menuCamera != null) cameraHome = menuCamera.transform.position;
        if (message != null) message.text = "";
        if (fade != null) StartCoroutine(Fade(1f, 0f));
    }

    private void Update()
    {
        MoveCamera();
        UpdateMessage();
        if (loading || buttons == null || buttons.Length == 0) return;

        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;
        Gamepad pad = Gamepad.current;

        if (mouse != null)
        {
            Vector2 p = mouse.position.ReadValue();
            // Only take the hover from the mouse when it moves, so keys can still change the selection.
            if ((p - lastMouse).sqrMagnitude > 1f)
            {
                lastMouse = p;
                selected = Pick(p);
            }
            if (mouse.leftButton.wasPressedThisFrame)
            {
                int hit = Pick(p);
                if (hit >= 0) Choose(hit);
            }
        }

        bool left = false, right = false, go = false;
        if (keyboard != null)
        {
            left |= keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame;
            right |= keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame;
            go |= keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame;
            if (keyboard.escapeKey.wasPressedThisFrame) Quit();
        }
        if (pad != null)
        {
            left |= pad.dpad.left.wasPressedThisFrame;
            right |= pad.dpad.right.wasPressedThisFrame;
            go |= pad.buttonSouth.wasPressedThisFrame;
        }

        if (left) selected = selected <= 0 ? buttons.Length - 1 : selected - 1;
        if (right) selected = selected < 0 || selected >= buttons.Length - 1 ? 0 : selected + 1;
        if (go && selected >= 0) Choose(selected);

        for (int i = 0; i < buttons.Length; i++)
            if (buttons[i] != null) buttons[i].Hovered = i == selected;
    }

    private int Pick(Vector2 screen)
    {
        if (menuCamera == null) return -1;
        Ray ray = menuCamera.ScreenPointToRay(screen);
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f)) return -1;
        MainMenuButton b = hit.collider.GetComponentInParent<MainMenuButton>();
        return b == null ? -1 : System.Array.IndexOf(buttons, b);
    }

    private void Choose(int index)
    {
        MainMenuButton b = buttons[index];
        if (string.IsNullOrEmpty(b.sceneName) || !Application.CanStreamedLevelBeLoaded(b.sceneName))
        {
            ShowMessage(b.unavailableMessage);
            return;
        }
        StartCoroutine(Load(b.sceneName));
    }

    private IEnumerator Load(string sceneName)
    {
        loading = true;
        if (fade != null) yield return Fade(0f, 1f);
        SceneManager.LoadScene(sceneName);
    }

    private IEnumerator Fade(float from, float to)
    {
        fade.gameObject.SetActive(true);
        for (float t = 0f; t < fadeTime; t += Time.unscaledDeltaTime)
        {
            SetFade(Mathf.Lerp(from, to, t / fadeTime));
            yield return null;
        }
        SetFade(to);
        if (to <= 0f) fade.gameObject.SetActive(false);
    }

    private void SetFade(float a)
    {
        Color c = fade.color;
        c.a = a;
        fade.color = c;
    }

    private void ShowMessage(string text)
    {
        if (message == null) return;
        message.text = text;
        messageTimer = messageTime;
    }

    private void UpdateMessage()
    {
        if (message == null || messageTimer <= 0f) return;
        messageTimer -= Time.deltaTime;
        Color c = message.color;
        c.a = Mathf.Clamp01(messageTimer / 0.5f);
        message.color = c;
    }

    private void MoveCamera()
    {
        if (menuCamera == null) return;
        Vector2 target = Vector2.zero;
        if (Mouse.current != null && Screen.width > 0 && Screen.height > 0)
        {
            Vector2 p = Mouse.current.position.ReadValue();
            target = new Vector2(p.x / Screen.width - 0.5f, p.y / Screen.height - 0.5f) * 2f;
        }
        lean = Vector2.Lerp(lean, target, Time.deltaTime * 2f);

        float t = Time.time;
        Vector3 drift = new Vector3(Mathf.Sin(t * 0.13f), Mathf.Sin(t * 0.21f) * 0.5f, Mathf.Sin(t * 0.17f) * 0.5f) * driftAmount;
        Transform cam = menuCamera.transform;
        cam.position = cameraHome + drift + new Vector3(lean.x, lean.y * 0.5f, 0f) * mouseLean;
        cam.rotation = Quaternion.LookRotation(lookTarget - cam.position, Vector3.up);
    }

    private void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
