using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Text for the meditation feature: control hints under the crosshair, a short toast
/// when a creature is remembered, and a full-screen paper blank used to hide the jump
/// between the world and the reflection space.
/// </summary>
[DisallowMultipleComponent]
public sealed class MeditationHUD : MonoBehaviour
{
    [SerializeField] private Color paperColor = new Color(0.86f, 0.85f, 0.82f, 1f);
    [SerializeField] private Color worldTextColor = new Color(1f, 1f, 1f, 0.9f);
    [SerializeField] private Color spaceTextColor = new Color(0.08f, 0.08f, 0.09f, 0.9f);
    [Min(0.5f)] [SerializeField] private float toastSeconds = 3f;

    private Text titleText, hintText, toastText, cornerText;
    private CanvasGroup hintGroup, toastGroup;
    private Image blank;
    private float toastTimer;
    private bool hintVisible;

    public Color PaperColor => paperColor;

    private void Awake()
    {
        var canvasObject = new GameObject("MeditationCanvas", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;
        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // The blank sits under the text so hints stay readable during the cut.
        var blankObject = new GameObject("Blank", typeof(RectTransform));
        blankObject.transform.SetParent(canvasObject.transform, false);
        var blankRect = (RectTransform)blankObject.transform;
        blankRect.anchorMin = Vector2.zero;
        blankRect.anchorMax = Vector2.one;
        blankRect.offsetMin = blankRect.offsetMax = Vector2.zero;
        blank = blankObject.AddComponent<Image>();
        blank.color = new Color(paperColor.r, paperColor.g, paperColor.b, 0f);
        blank.raycastTarget = false;

        hintGroup = Group(canvasObject.transform, "Hint", new Vector2(0.5f, 0.5f), new Vector2(0f, -70f));
        titleText = MakeText(hintGroup.transform, font, 18, new Vector2(0f, 0f));
        hintText = MakeText(hintGroup.transform, font, 22, new Vector2(0f, -28f));
        hintGroup.alpha = 0f;

        toastGroup = Group(canvasObject.transform, "Toast", new Vector2(0.5f, 0f), new Vector2(0f, 120f));
        toastText = MakeText(toastGroup.transform, font, 22, Vector2.zero);
        toastGroup.alpha = 0f;

        var corner = Group(canvasObject.transform, "Corner", new Vector2(0f, 0f), new Vector2(40f, 40f));
        cornerText = MakeText(corner.transform, font, 18, Vector2.zero);
        cornerText.alignment = TextAnchor.LowerLeft;
        ((RectTransform)corner.transform).pivot = Vector2.zero;
        var cornerRect = (RectTransform)cornerText.transform;
        cornerRect.anchorMin = cornerRect.anchorMax = cornerRect.pivot = Vector2.zero;
        cornerRect.anchoredPosition = Vector2.zero;
        SetInSpace(false);
    }

    private void Update()
    {
        hintGroup.alpha = Mathf.MoveTowards(hintGroup.alpha, hintVisible ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        toastTimer -= Time.unscaledDeltaTime;
        toastGroup.alpha = Mathf.MoveTowards(toastGroup.alpha, toastTimer > 0f ? 1f : 0f, Time.unscaledDeltaTime * 2f);
    }

    /// <summary>Dark text for the pale reflection space, light text for the dark world.</summary>
    public void SetInSpace(bool inSpace)
    {
        Color color = inSpace ? spaceTextColor : worldTextColor;
        titleText.color = new Color(color.r, color.g, color.b, color.a * 0.6f);
        hintText.color = color;
        toastText.color = color;
        cornerText.color = new Color(color.r, color.g, color.b, color.a * 0.6f);
    }

    public void SetHint(string title, string hint)
    {
        hintVisible = !string.IsNullOrEmpty(hint);
        if (!hintVisible) return;
        titleText.text = title ?? string.Empty;
        hintText.text = hint;
    }

    public void SetCorner(string text) => cornerText.text = text ?? string.Empty;

    public void Toast(string message)
    {
        toastText.text = message;
        toastTimer = toastSeconds;
    }

    /// <summary>0 = clear, 1 = solid paper over the whole screen.</summary>
    public void SetBlank(float amount)
    {
        blank.color = new Color(paperColor.r, paperColor.g, paperColor.b, Mathf.Clamp01(amount));
    }

    private static CanvasGroup Group(Transform parent, string name, Vector2 anchor, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(1400f, 80f);
        var group = go.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        return group;
    }

    private static Text MakeText(Transform parent, Font font, int size, Vector2 position)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(1400f, 40f);
        var text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }
}
