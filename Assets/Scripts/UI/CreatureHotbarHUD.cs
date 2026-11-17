using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Four dots along the bottom of the screen for BodyMounts' four slots: a hollow white ring = empty,
/// a solid black dot with a white rim = holding a creature. A small arrow above points at the selected slot and
/// slides to a new one. No text. Builds its own overlay canvas; BodyMounts adds it when the scene has none.
/// The extra tool slot (BodyMounts.SetTool, the conductor baton) is a diamond a little apart on the right; it shows only
/// once there is a tool.
/// </summary>
public sealed class CreatureHotbarHUD : MonoBehaviour
{
    [SerializeField] private BodyMounts mounts;
    [Tooltip("Dot size and gap between dot centres, in pixels at 1080p.")]
    [SerializeField] private float dotSize = 20f;
    [SerializeField] private float spacing = 38f;
    [Tooltip("Height of the dots' middle above the bottom of the screen, in pixels at 1080p.")]
    [SerializeField] private float bottom = 42f;
    [Tooltip("Arrow size and its gap above the dots, in pixels at 1080p.")]
    [SerializeField] private float arrowSize = 10f;
    [SerializeField] private float arrowGap = 9f;
    [SerializeField] private Color ink = new Color(0.04f, 0.04f, 0.05f, 1f);
    [SerializeField] private Color paper = new Color(0.96f, 0.95f, 0.92f, 0.9f);
    [Tooltip("A soft grey plate behind each dot, so a black dot still reads on the dark world.")]
    [SerializeField] private Color plate = new Color(0.55f, 0.55f, 0.55f, 0.45f);

    private RectTransform root;
    private Image[] rims, fills, plates;
    private RectTransform arrow;
    private float arrowX;
    private bool built;
    private float[] pulse;

    public void Bind(BodyMounts target) => mounts = target;

    private void Start()
    {
        if (mounts == null) mounts = FindFirstObjectByType<BodyMounts>();
        if (mounts != null) Build();
    }

    private void Build()
    {
        var canvasObject = new GameObject("Creature Hotbar HUD", typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        root = new GameObject("Dots", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(canvasObject.transform, false);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
        root.anchoredPosition = new Vector2(0f, bottom);

        Sprite disc = MakeSprite(64, 0f);
        Sprite ring = MakeSprite(64, 0.7f);
        int n = mounts.SlotCount;
        rims = new Image[n];
        fills = new Image[n];
        pulse = new float[n];
        plates = new Image[n];
        for (int i = 0; i < n; i++)
        {
            float x = SlotX(i);
            bool tool = i == mounts.ToolSlot;
            plates[i] = NewImage("Slot " + i + " Plate", disc, x, 0f, dotSize * 1.45f, plate);
            rims[i] = NewImage("Slot " + i, ring, x, 0f, dotSize, paper);
            fills[i] = NewImage("Slot " + i + " Fill", disc, x, 0f, dotSize * 0.8f, ink);
            // The tool slot: the same ring and dot turned into a diamond.
            if (tool) rims[i].rectTransform.localEulerAngles = fills[i].rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
            if (tool) { rims[i].sprite = MakeDiamond(64, 0.62f); fills[i].sprite = MakeDiamond(64, 0f); }
        }
        arrow = NewImage("Arrow", MakeArrow(48), 0f, dotSize * 0.5f + arrowGap + arrowSize * 0.5f, arrowSize, paper).rectTransform;
        arrowX = SlotX(mounts.Selected);
        built = true;
    }

    // The four creature slots centred; the tool slot a little apart on the right.
    private float SlotX(int i) => i == mounts.ToolSlot ? (mounts.ToolSlot - 1) * 0.5f * spacing + spacing * 1.35f
        : (i - (mounts.ToolSlot - 1) * 0.5f) * spacing;

    private Image NewImage(string name, Sprite sprite, float x, float y, float size, Color color)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        image.rectTransform.SetParent(root, false);
        image.rectTransform.sizeDelta = new Vector2(size, size);
        image.rectTransform.anchoredPosition = new Vector2(x, y);
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private void LateUpdate()
    {
        if (!built || mounts == null) return;
        bool blocked = MeditationController.BlocksPlayerInput || BoneAwakeningCutscene.Playing;
        root.gameObject.SetActive(!blocked);
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < rims.Length; i++)
        {
            if (i == mounts.ToolSlot)
            {
                bool show = mounts.HasTool;
                plates[i].enabled = rims[i].enabled = fills[i].enabled = show;
                if (!show) continue;
            }
            bool filled = mounts.IsFilled(i);
            // Solid black dot inside the rim when holding; just the hollow rim when empty.
            Color f = fills[i].color;
            f.a = Mathf.MoveTowards(f.a, filled ? 1f : 0f, dt * 8f);
            fills[i].color = f;
            // A small swell when a slot is picked or changes.
            bool chosen = i == mounts.Selected;
            pulse[i] = Mathf.MoveTowards(pulse[i], chosen && Time.time - mounts.LastChange < 0.15f ? 1f : 0f, dt * 6f);
            float s = (chosen ? 1.12f : 1f) + pulse[i] * 0.2f;
            rims[i].rectTransform.localScale = fills[i].rectTransform.localScale = Vector3.one * s;
        }
        // The arrow slides to the selected slot.
        arrowX = Mathf.Lerp(arrowX, SlotX(mounts.Selected), 1f - Mathf.Exp(-18f * dt));
        arrow.anchoredPosition = new Vector2(arrowX, arrow.anchoredPosition.y);
    }

    /// <summary>A soft-edged disc, or a ring when hole is above zero (hole = inner radius / outer radius).</summary>
    private static Sprite MakeSprite(int size, float hole)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
            float a = Mathf.Clamp01((1f - d) * r * 0.5f);
            if (hole > 0f) a *= Mathf.Clamp01((d - hole) * r * 0.5f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    /// <summary>A square (shown turned 45 degrees: a diamond), or its outline when hole is above zero.</summary>
    private static Sprite MakeDiamond(int size, float hole)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Max(Mathf.Abs(x + 0.5f - r), Mathf.Abs(y + 0.5f - r)) / r * 1.18f;
            float a = Mathf.Clamp01((1f - d) * r * 0.5f);
            if (hole > 0f) a *= Mathf.Clamp01((d - hole) * r * 0.5f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    /// <summary>A small triangle pointing down.</summary>
    private static Sprite MakeArrow(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            // Apex at the bottom middle, flat side at the top.
            float v = (y + 0.5f) / size;               // 0 bottom .. 1 top
            float half = v * 0.5f;                     // half width at this height
            float u = Mathf.Abs((x + 0.5f) / size - 0.5f);
            float edge = (half - u) * size;
            float a = Mathf.Clamp01(edge) * Mathf.Clamp01((1f - v) * size);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
