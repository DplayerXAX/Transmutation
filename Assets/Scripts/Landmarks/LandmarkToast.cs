using UnityEngine;

/// <summary>A short line of text low on the screen that fades out. Created on first use.</summary>
public sealed class LandmarkToast : MonoBehaviour
{
    private static LandmarkToast instance;
    private string message;
    private float shownAt = -100f;
    private GUIStyle style;

    private const float Duration = 4f;

    public static void Show(string text)
    {
        if (instance == null)
        {
            var go = new GameObject("Landmark Toast") { hideFlags = HideFlags.DontSave };
            instance = go.AddComponent<LandmarkToast>();
        }
        instance.message = text;
        instance.shownAt = Time.unscaledTime;
    }

    private void OnGUI()
    {
        float age = Time.unscaledTime - shownAt;
        if (string.IsNullOrEmpty(message) || age > Duration) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20 };
        }
        float alpha = Mathf.Clamp01(age / 0.4f) * Mathf.Clamp01((Duration - age) / 1.2f);
        var rect = new Rect(0f, Screen.height * 0.78f, Screen.width, 40f);
        style.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.8f);
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), message, style);
        style.normal.textColor = new Color(0.9f, 0.9f, 0.87f, alpha);
        GUI.Label(rect, message, style);
    }
}
