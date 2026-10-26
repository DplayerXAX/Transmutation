using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Simple UI main menu. Each entry is a UI Button that loads a scene. Mouse, arrow keys and gamepad work
/// through the EventSystem; Esc quits. Loading fades the screen to black.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        public Button button;
        [Tooltip("Scene name as listed in Build Settings.")]
        public string sceneName;
        [Tooltip("Shown when the scene is not in Build Settings yet.")]
        public string unavailableMessage = "This place is not built yet.";
    }

    public Entry[] entries;

    [Header("UI")]
    [Tooltip("Full screen black image used for fades.")]
    public Image fade;
    [Tooltip("Short message line, e.g. when a scene is not built yet.")]
    public Text message;
    public float fadeTime = 0.6f;
    public float messageTime = 2.5f;

    private bool loading;
    private float messageTimer;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 1f;
        if (message != null) message.text = "";

        foreach (Entry e in entries)
        {
            Entry entry = e;
            if (entry.button != null) entry.button.onClick.AddListener(() => Choose(entry));
        }
        // Select the first button so keys and gamepad work straight away.
        if (entries.Length > 0 && entries[0].button != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(entries[0].button.gameObject);

        if (fade != null) StartCoroutine(Fade(1f, 0f));
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Quit();

        if (message != null && messageTimer > 0f)
        {
            messageTimer -= Time.deltaTime;
            Color c = message.color;
            c.a = Mathf.Clamp01(messageTimer / 0.5f);
            message.color = c;
        }
    }

    private void Choose(Entry entry)
    {
        if (loading) return;
        if (string.IsNullOrEmpty(entry.sceneName) || !Application.CanStreamedLevelBeLoaded(entry.sceneName))
        {
            ShowMessage(entry.unavailableMessage);
            return;
        }
        StartCoroutine(Load(entry.sceneName));
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
        Color c = message.color;
        c.a = 1f;
        message.color = c;
        messageTimer = messageTime;
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
