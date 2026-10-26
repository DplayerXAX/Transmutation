using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A small outlined mouse drawn under the crosshair. Its left or right button fills in when that button
/// does something right now, and brightens while it is held down. Hidden when neither button does anything.
/// The shapes are drawn into textures at start, so no art asset is needed. Used by InteractionPromptHUD.
/// </summary>
public sealed class MouseButtonIcon : MonoBehaviour
{
    private const int Width = 40, Height = 60;

    private Image outline, left, right;
    private CanvasGroup group;
    private static Sprite outlineSprite, leftSprite, rightSprite;

    private static readonly Color Off = new Color(1f, 1f, 1f, 0.12f);
    private static readonly Color On = new Color(1f, 1f, 1f, 0.78f);
    private static readonly Color Pressed = new Color(1f, 0.93f, 0.75f, 1f);

    /// <summary>Makes the icon under parent, offset from the screen centre (reference pixels).</summary>
    public static MouseButtonIcon Build(Transform parent, Vector2 offset, float scale)
    {
        var go = new GameObject("Mouse Button Icon", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = offset;
        rect.sizeDelta = new Vector2(Width, Height) * scale;
        var icon = go.AddComponent<MouseButtonIcon>();
        icon.group = go.AddComponent<CanvasGroup>();
        icon.group.alpha = 0f;
        icon.group.interactable = icon.group.blocksRaycasts = false;
        MakeSprites();
        icon.left = Layer(go.transform, "Left", leftSprite);
        icon.right = Layer(go.transform, "Right", rightSprite);
        icon.outline = Layer(go.transform, "Outline", outlineSprite);
        return icon;
    }

    private static Image Layer(Transform parent, string name, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }

    /// <summary>For tests: whether each button is shown lit, and the drawn shapes.</summary>
    public bool LeftLit => left != null && left.color.a > 0.5f;
    public bool RightLit => right != null && right.color.a > 0.5f;
    public bool Visible => group != null && group.alpha > 0.5f;
    public static Texture2D[] Textures => outlineSprite == null ? null : new[] { outlineSprite.texture, leftSprite.texture, rightSprite.texture };

    /// <summary>Which buttons do something now, and which are held down.</summary>
    public void Show(bool leftUsable, bool rightUsable, bool leftHeld, bool rightHeld, float fadeTime)
    {
        bool visible = leftUsable || rightUsable;
        group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeTime));
        left.color = !leftUsable ? Off : leftHeld ? Pressed : On;
        right.color = !rightUsable ? Off : rightHeld ? Pressed : On;
        outline.color = new Color(1f, 1f, 1f, 0.85f);
    }

    // ---------------- Drawing ----------------

    private static void MakeSprites()
    {
        if (outlineSprite != null) return;
        outlineSprite = Draw(Part.Outline);
        leftSprite = Draw(Part.Left);
        rightSprite = Draw(Part.Right);
    }

    private enum Part { Outline, Left, Right }

    /// <summary>
    /// The mouse is a rounded body (a rectangle with fully rounded ends); a line across it a little above the
    /// middle and one down the middle above that split off the two buttons.
    /// </summary>
    private static Sprite Draw(Part part)
    {
        const int scale = 2; // drawn at twice the size for smooth edges
        int w = Width * scale, h = Height * scale;
        var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Mouse " + part, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[w * h];
        float radius = w * 0.5f - 2f * scale;
        float stroke = 2.4f * scale;
        float split = h * 0.58f;      // the line under the buttons
        float gap = 1.6f * scale;     // half the width of the lines between buttons
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float px = x + 0.5f, py = y + 0.5f;
            // Signed distance to the rounded body (negative inside).
            float cx = w * 0.5f;
            float top = h - radius - 2f * scale, bottom = radius + 2f * scale;
            float cy = Mathf.Clamp(py, bottom, top);
            float d = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy)) - radius;
            float a = 0f;
            if (part == Part.Outline)
            {
                float body = Mathf.Clamp01(stroke * 0.5f + 0.5f - Mathf.Abs(d + stroke * 0.5f));
                float across = py > split - gap && py < split + gap && d < 0f ? 1f : 0f;
                float down = Mathf.Abs(px - cx) < gap && py > split && d < 0f ? 1f : 0f;
                a = Mathf.Max(body, Mathf.Max(across, down));
            }
            else
            {
                bool inside = d < -stroke * 0.9f && py > split + gap * 1.6f;
                bool side = part == Part.Left ? px < cx - gap * 1.6f : px > cx + gap * 1.6f;
                a = inside && side ? Mathf.Clamp01(-d - stroke * 0.9f) : 0f;
            }
            pixels[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f * scale);
    }
}
