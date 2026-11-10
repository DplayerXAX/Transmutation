using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

/// <summary>Physical page controls and writing on the slab, with a single screen-space sticker satchel.</summary>
public sealed class SlabJournal : MonoBehaviour
{
    private const float Unit = 0.01f;
    private static readonly Color Paper = new(0.96f, 0.92f, 0.82f);
    private static readonly Color Stone = new(0.20f, 0.19f, 0.18f);
    private static readonly Color Brass = new(0.52f, 0.36f, 0.20f);
    private InkSurface ink;
    private SlabJournalData data;
    private SlabStickerCatalog catalog;
    private Camera view;
    private Font font;
    private UnityEngine.UI.GraphicRaycaster pageRaycaster;
    private RectTransform pageArea, satchelPanel, placement;
    private UnityEngine.UI.InputField textField;
    private UnityEngine.UI.Text pageLabel, message;
    private UnityEngine.UI.Button previous, next, type, draw, erase;
    private UnityEngine.UI.RawImage heldPreview;
    private Canvas satchel;
    private bool editing, dirty;
    private SlabJournalButton.Action tool;
    private readonly List<Texture2D> buttonIcons = new();
    private float saveAt, messageUntil;
    private SlabStickerCatalog.Entry held;
    private SlabJournalData.Sticker moved;
    private readonly List<GameObject> placed = new();
    private readonly TextGenerator textMeasure = new();
    private Material buttonStone;
    private SlabJournalData.Page Page => data.pages[data.viewedPage];
    internal bool Drawing => editing && tool != SlabJournalButton.Action.Type && held == null && !data.ReadOnly;
    internal bool Erasing => tool == SlabJournalButton.Action.Erase;

    internal void Setup(InkSurface surface, IReadOnlyList<string> unlocked, string saveRoot = null)
    {
        ink = surface;
        view = Camera.main;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        catalog = Resources.Load<SlabStickerCatalog>("SlabStickerCatalog");
        data = SlabJournalData.Load(saveRoot ?? ReflectionStorage.Root);
        BuildPage();
        BuildSatchel(unlocked);
        DisplayPage();
    }

    internal void BeginEditing(SlabJournalButton.Action initialTool, Camera camera)
    {
        view = camera;
        pageRaycaster.GetComponent<Canvas>().worldCamera = view;
        editing = true;
        pageRaycaster.enabled = true;
        satchel.gameObject.SetActive(true);
        SetTool(initialTool);
        if (data.ReadOnly) Hint("Journal save could not be opened");
    }

    internal void EndEditing()
    {
        if (!editing) return;
        CancelSticker();
        editing = false;
        textField.DeactivateInputField();
        RefreshInput();
        satchel.gameObject.SetActive(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        Save();
    }

    internal bool CancelSticker()
    {
        if (held == null) return false;
        held = null;
        moved = null;
        heldPreview.enabled = false;
        RefreshStickers();
        RefreshInput();
        return true;
    }

    internal bool AcceptsInk(Vector2 cursor) => Drawing &&
        RectTransformUtility.RectangleContainsScreenPoint(pageArea, cursor, view) &&
        !RectTransformUtility.RectangleContainsScreenPoint(satchelPanel, cursor);

    internal void Turn(int step)
    {
        if (step < 0 && data.viewedPage == 0) return;
        if (!Save()) { Hint("Could not save this page"); return; }
        if (step > 0 && data.viewedPage == data.pages.Count - 1)
        {
            if (data.ReadOnly) return;
            data.AddPage();
        }
        CancelSticker();
        textField.DeactivateInputField();
        data.viewedPage += step;
        dirty = true;
        DisplayPage();
        Save();
    }

    internal bool Save()
    {
        if (data == null) return true;
        if (ink != null && !ink.Save()) return false;
        if (!dirty) return true;
        if (!data.Save()) return false;
        dirty = false;
        return true;
    }

    internal void MarkInkDirty() => saveAt = Time.unscaledTime + 0.8f;

    private void Update()
    {
        if (data == null) return;
        if ((dirty || ink.HasUnsavedInk) && Time.unscaledTime >= saveAt && !Save())
            saveAt = Time.unscaledTime + 4f;
        if (message.enabled && Time.unscaledTime >= messageUntil) message.enabled = false;
        if (held != null && Mouse.current != null)
            heldPreview.rectTransform.position = Mouse.current.position.ReadValue();
    }

    private void DisplayPage()
    {
        ink.LoadPage(Page.id, data.InkPath(Page.id));
        textField.SetTextWithoutNotify(Page.text);
        textField.caretPosition = 0;
        pageLabel.text = (data.viewedPage + 1) + " / " + data.pages.Count;
        previous.interactable = data.viewedPage > 0;
        next.interactable = !data.ReadOnly || data.viewedPage + 1 < data.pages.Count;
        RefreshStickers();
        RefreshInput();
    }

    private void ChangeText(string value)
    {
        if (!editing || tool != SlabJournalButton.Action.Type || held != null || data.ReadOnly)
        {
            textField.SetTextWithoutNotify(Page.text);
            return;
        }
        var label = textField.textComponent;
        var settings = label.GetGenerationSettings(label.rectTransform.rect.size);
        settings.verticalOverflow = VerticalWrapMode.Overflow;
        string measured = value.EndsWith("\n") ? value + " " : value;
        float height = textMeasure.GetPreferredHeight(measured, settings) / label.pixelsPerUnit;
        if (height > label.rectTransform.rect.height)
        {
            int caret = Mathf.Min(textField.caretPosition, Page.text.Length);
            textField.SetTextWithoutNotify(Page.text);
            textField.caretPosition = caret;
            Hint("Page full");
            return;
        }
        Page.text = value;
        MarkDirty();
    }

    private void MarkDirty()
    {
        dirty = true;
        saveAt = Time.unscaledTime + 0.8f;
    }

    internal void SetTool(SlabJournalButton.Action selected)
    {
        CancelSticker();
        if (selected != SlabJournalButton.Action.Type) textField.DeactivateInputField();
        tool = selected;
        type.targetGraphic.color = tool == SlabJournalButton.Action.Type ? Brass : Stone;
        draw.targetGraphic.color = tool == SlabJournalButton.Action.Draw ? Brass : Stone;
        erase.targetGraphic.color = tool == SlabJournalButton.Action.Erase ? Brass : Stone;
        RefreshStickers();
        RefreshInput();
    }

    private void RefreshInput()
    {
        bool typing = editing && tool == SlabJournalButton.Action.Type && held == null && !data.ReadOnly;
        textField.interactable = typing;
        textField.targetGraphic.raycastTarget = typing;
        textField.textComponent.raycastTarget = false;
        placement.gameObject.SetActive(editing && held != null);
        pageRaycaster.enabled = editing;
    }

    private void Hint(string text)
    {
        message.text = text;
        message.enabled = true;
        messageUntil = Time.unscaledTime + 2.2f;
    }

    private void BuildPage()
    {
        var face = Rect("Physical Journal", transform, new Vector2(1240, 480), Vector2.zero);
        face.localPosition = new Vector3(0, 2.65f, 0.64f);
        face.localRotation = Quaternion.Euler(0, 180, 0);
        face.localScale = Vector3.one * Unit;
        var canvas = face.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = view;
        pageRaycaster = face.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        pageArea = Rect("Writing Area", face, new Vector2(1190, 385), new Vector2(0, 36));
        var input = Rect("Page Text", pageArea, pageArea.sizeDelta, Vector2.zero);
        var backdrop = input.gameObject.AddComponent<UnityEngine.UI.Image>();
        backdrop.color = Color.clear;
        var writing = Label("Writing", input, input.sizeDelta - new Vector2(24, 18), Vector2.zero, "", 29);
        writing.alignment = TextAnchor.UpperLeft;
        writing.horizontalOverflow = HorizontalWrapMode.Wrap;
        writing.verticalOverflow = VerticalWrapMode.Truncate;
        textField = input.gameObject.AddComponent<UnityEngine.UI.InputField>();
        textField.textComponent = writing;
        textField.targetGraphic = backdrop;
        textField.transition = UnityEngine.UI.Selectable.Transition.None;
        textField.lineType = UnityEngine.UI.InputField.LineType.MultiLineNewline;
        textField.customCaretColor = true;
        textField.caretColor = Paper;
        textField.selectionColor = new Color(0.78f, 0.60f, 0.30f, 0.55f);
        textField.onValueChanged.AddListener(ChangeText);

        placement = Rect("Sticker Placement", pageArea, pageArea.sizeDelta, Vector2.zero);
        placement.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.clear;
        OnClick(placement.gameObject, PlaceSticker);

        buttonStone = RestSpotSystem.MakeRestMaterial(new Color(0.16f, 0.14f, 0.12f));
        previous = PhysicalButton(face, "Previous", -140, SlabJournalButton.Action.Previous);
        next = PhysicalButton(face, "Next", 140, SlabJournalButton.Action.Next);
        type = PhysicalButton(face, "Type", -474, SlabJournalButton.Action.Type);
        draw = PhysicalButton(face, "Draw", 414, SlabJournalButton.Action.Draw);
        erase = PhysicalButton(face, "Erase", 484, SlabJournalButton.Action.Erase);
        pageLabel = Label("Page Number", face, new Vector2(180, 42), new Vector2(0, -218), "", 26);
        message = Label("Journal Status", face, new Vector2(500, 36), new Vector2(0, -184), "", 24);
        message.enabled = false;
        RefreshInput();
    }

    private UnityEngine.UI.Button PhysicalButton(RectTransform parent, string name, float x,
        SlabJournalButton.Action action)
    {
        var rect = Rect(name, parent, new Vector2(62, 46), new Vector2(x, -218));
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = Stone;
        var collider = rect.gameObject.AddComponent<BoxCollider>();
        collider.size = new Vector3(62, 46, 12);
        collider.center = new Vector3(0, 0, 5);
        rect.gameObject.AddComponent<SlabJournalButton>().Setup(this, action);
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "Stone Button";
        block.transform.SetParent(rect, false);
        block.transform.localPosition = Vector3.forward * 7;
        block.transform.localScale = new Vector3(66, 49, 12);
        block.GetComponent<Collider>().enabled = false;
        block.GetComponent<Renderer>().sharedMaterial = buttonStone;
        var glyphRect = Rect("Engraved Symbol", rect, new Vector2(34, 34), Vector2.zero);
        glyphRect.localPosition += Vector3.back * 2;
        var glyph = glyphRect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        glyph.raycastTarget = false;
        glyph.color = Paper;
        glyph.texture = SlabControlIcons.Create(action);
        buttonIcons.Add((Texture2D)glyph.texture);
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() =>
        {
            if (action == SlabJournalButton.Action.Previous) Turn(-1);
            else if (action == SlabJournalButton.Action.Next) Turn(1);
            else SetTool(action);
        });
        return button;
    }

    private void BuildSatchel(IReadOnlyList<string> unlocked)
    {
        var overlay = new GameObject("Sticker Satchel", typeof(RectTransform));
        overlay.transform.SetParent(transform, false);
        satchel = overlay.AddComponent<Canvas>();
        satchel.renderMode = RenderMode.ScreenSpaceOverlay;
        satchel.sortingOrder = 110;
        var scaler = overlay.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        overlay.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        satchelPanel = Rect("Satchel", overlay.transform, new Vector2(146, 0), new Vector2(-12, 0));
        satchelPanel.anchorMin = new Vector2(1, 0.16f);
        satchelPanel.anchorMax = new Vector2(1, 0.84f);
        satchelPanel.pivot = new Vector2(1, 0.5f);
        satchelPanel.anchoredPosition = new Vector2(-12, 0);
        satchelPanel.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.black;
        OnClick(satchelPanel.gameObject, DropInSatchel);

        var scrollRect = Rect("Scroll", satchelPanel, Vector2.zero, Vector2.zero);
        Stretch(scrollRect, new Vector2(7, 7), new Vector2(-7, -7));
        var scroll = scrollRect.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 36;
        var viewport = Rect("Viewport", scrollRect, Vector2.zero, Vector2.zero);
        Stretch(viewport, Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.white;
        viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
        OnClick(viewport.gameObject, DropInSatchel);
        var content = Rect("Sticker Collection", viewport, Vector2.zero, Vector2.zero);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1);
        var layout = content.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
        layout.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 2;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.cellSize = new Vector2(62, 62);
        layout.spacing = new Vector2(6, 6);
        var fitter = content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;
        scroll.viewport = viewport;
        if (catalog != null)
        {
            foreach (string family in unlocked)
            foreach (var item in catalog.Stickers)
                if (item != null && item.familyId == family && item.image != null) AddThumbnail(content, item);
        }
        heldPreview = Rect("Held Sticker", overlay.transform, new Vector2(86, 86), Vector2.zero)
            .gameObject.AddComponent<UnityEngine.UI.RawImage>();
        heldPreview.raycastTarget = false;
        heldPreview.enabled = false;
        overlay.SetActive(false);

        if (EventSystem.current == null)
        {
            var events = new GameObject("Journal Input", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false);
        }
    }

    private void AddThumbnail(RectTransform content, SlabStickerCatalog.Entry item)
    {
        var cell = Rect(item.displayName, content, new Vector2(62, 62), Vector2.zero);
        var art = Art(cell, item.image, 59);
        art.raycastTarget = true;
        var button = cell.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.transition = UnityEngine.UI.Selectable.Transition.None;
        button.onClick.AddListener(() =>
        {
            if (held != null) DropInSatchel();
            else PickUp(item, null);
        });
    }

    private void RefreshStickers()
    {
        foreach (var old in placed) { old.SetActive(false); Destroy(old); }
        placed.Clear();
        foreach (var sticker in Page.stickers)
        {
            if (sticker == null || sticker == moved) continue;
            var item = catalog != null ? catalog.Find(sticker.id) : null;
            if (item?.image == null) continue;
            Vector2 at = Vector2.Scale(sticker.position - Vector2.one * 0.5f, pageArea.sizeDelta);
            var graphic = Art(pageArea, item.image, 88);
            graphic.rectTransform.anchoredPosition = at;
            graphic.raycastTarget = editing && tool == SlabJournalButton.Action.Type && held == null && !data.ReadOnly;
            OnClick(graphic.gameObject, () => PickUp(item, sticker));
            placed.Add(graphic.gameObject);
        }
        placement.SetAsLastSibling();
    }

    private void PickUp(SlabStickerCatalog.Entry item, SlabJournalData.Sticker existing)
    {
        if (!editing || data.ReadOnly || held != null) return;
        held = item;
        moved = existing;
        textField.DeactivateInputField();
        heldPreview.texture = item.image;
        heldPreview.rectTransform.sizeDelta = FitArt(item.image, 86);
        heldPreview.enabled = true;
        RefreshStickers();
        RefreshInput();
    }

    private void PlaceSticker()
    {
        if (held == null || Mouse.current == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
            pageArea, Mouse.current.position.ReadValue(), view, out Vector2 point)) return;
        Vector2 size = pageArea.sizeDelta;
        Vector2 margin = FitArt(held.image, 88) * 0.5f;
        point.x = Mathf.Clamp(point.x, margin.x - size.x * 0.5f, size.x * 0.5f - margin.x);
        point.y = Mathf.Clamp(point.y, margin.y - size.y * 0.5f, size.y * 0.5f - margin.y);
        var sticker = moved ?? new SlabJournalData.Sticker { id = held.id };
        sticker.position = new Vector2(point.x / size.x + 0.5f, point.y / size.y + 0.5f);
        if (moved == null) Page.stickers.Add(sticker);
        MarkDirty();
        CancelSticker();
    }

    internal bool DiscardHeldSticker()
    {
        if (held == null) return false;
        DropInSatchel();
        return true;
    }

    private void DropInSatchel()
    {
        if (moved != null)
        {
            Page.stickers.Remove(moved);
            MarkDirty();
        }
        CancelSticker();
    }

    private static UnityEngine.UI.RawImage Art(Transform parent, Texture2D texture, float maxSize)
    {
        var art = Rect("Sticker Art", parent, FitArt(texture, maxSize), Vector2.zero)
            .gameObject.AddComponent<UnityEngine.UI.RawImage>();
        art.texture = texture;
        art.raycastTarget = false;
        return art;
    }

    private static Vector2 FitArt(Texture2D texture, float maxSize) =>
        new Vector2(texture.width, texture.height) * (maxSize / Mathf.Max(texture.width, texture.height));

    private static void OnClick(GameObject target, System.Action action) =>
        target.AddComponent<SlabJournalClick>().Click = action;

    private UnityEngine.UI.Text Label(string name, Transform parent, Vector2 size, Vector2 at, string value, int fontSize)
    {
        var label = Rect(name, parent, size, at).gameObject.AddComponent<UnityEngine.UI.Text>();
        label.font = font;
        label.fontSize = fontSize;
        label.color = Paper;
        label.text = value;
        label.alignment = TextAnchor.MiddleCenter;
        label.supportRichText = false;
        label.raycastTarget = false;
        return label;
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = min;
        rect.offsetMax = max;
    }

    private void OnApplicationPause(bool paused) { if (paused) Save(); }
    private void OnApplicationQuit() => Save();
    private void OnDestroy()
    {
        Save();
        if (buttonStone != null) Destroy(buttonStone);
        foreach (Texture2D icon in buttonIcons) Destroy(icon);
    }
}

internal sealed class SlabJournalButton : MonoBehaviour
{
    internal enum Action { Previous, Next, Type, Draw, Erase }
    internal SlabJournal Journal { get; private set; }
    internal Action Command { get; private set; }
    internal void Setup(SlabJournal journal, Action action) { Journal = journal; Command = action; }
}

internal sealed class SlabJournalClick : MonoBehaviour, IPointerClickHandler
{
    internal System.Action Click;
    public void OnPointerClick(PointerEventData data)
    {
        if (data.button == PointerEventData.InputButton.Left) Click?.Invoke();
    }
}

internal static class SlabControlIcons
{
    internal static Texture2D Create(SlabJournalButton.Action action)
    {
        (Vector2 start, Vector2 end, float radius)[] strokes = action switch
        {
            SlabJournalButton.Action.Type => new (Vector2, Vector2, float)[]
                { (new(-9, 8), new(9, 8), 1.7f), (new(0, 8), new(0, -10), 1.7f) },
            SlabJournalButton.Action.Draw => new (Vector2, Vector2, float)[]
                { (new(-6, -6), new(8, 8), 2.8f), (new(-11, -11), new(-6, -6), 1.3f) },
            SlabJournalButton.Action.Erase => new (Vector2, Vector2, float)[]
                { (new(-9, -3), new(2, 8), 1.6f), (new(2, 8), new(9, 1), 1.6f),
                  (new(9, 1), new(-2, -10), 1.6f), (new(-2, -10), new(-9, -3), 1.6f),
                  (new(-5, 1), new(2, -6), 1.2f) },
            _ => Arrow(action == SlabJournalButton.Action.Previous ? -1f : 1f)
        };
        const int size = 64;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Vector2 point = new Vector2(x + 0.5f, y + 0.5f) * (30f / size) - Vector2.one * 15f;
            float coverage = 0f;
            foreach (var (start, end, radius) in strokes)
            {
                Vector2 line = end - start;
                float along = Mathf.Clamp01(Vector2.Dot(point - start, line) / line.sqrMagnitude);
                float distance = (point - (start + along * line)).magnitude;
                coverage = Mathf.Max(coverage, Mathf.Clamp01((radius - distance) * 2f + 0.5f));
            }
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(coverage * 255));
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = action + " symbol", wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static (Vector2, Vector2, float)[] Arrow(float dir) =>
        new (Vector2, Vector2, float)[]
        {
            (new(-10 * dir, 0), new(10 * dir, 0), 1.7f),
            (new(3 * dir, 7), new(10 * dir, 0), 1.7f),
            (new(10 * dir, 0), new(3 * dir, -7), 1.7f)
        };
}
