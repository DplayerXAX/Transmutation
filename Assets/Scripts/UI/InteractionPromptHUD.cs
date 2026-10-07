using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows short control hints under the crosshair: carrying and interacting with the
/// Creature in view, and climbing / wall running / ledge actions when they are possible.
/// Builds its own overlay Canvas, so it only needs to exist once in the scene.
/// </summary>
[DisallowMultipleComponent]
public sealed class InteractionPromptHUD : MonoBehaviour
{
    [Header("Player References (found automatically when empty)")]
    [SerializeField] private PlayerCreatureCarrier carrier;
    [SerializeField] private PlayerCreatureInteractor interactor;
    [SerializeField] private AdvancedFirstPersonTraversal traversal;
    [SerializeField] private HandClimber handClimber;

    [Header("Layout")]
    [Tooltip("Offset of the prompt block from the screen centre, in reference pixels (1920x1080).")]
    [SerializeField] private Vector2 offsetFromCentre = new Vector2(0f, -70f);
    [SerializeField] private int titleFontSize = 18;
    [SerializeField] private int hintFontSize = 22;
    [SerializeField] private Color titleColor = new Color(1f, 1f, 1f, 0.55f);
    [SerializeField] private Color hintColor = new Color(1f, 1f, 1f, 0.92f);
    [Tooltip("Seconds to fade the prompt in or out.")]
    [Min(0.01f)] [SerializeField] private float fadeTime = 0.12f;

    [Header("Text")]
    [SerializeField] private bool showCreatureName = true;
    [SerializeField] private string carryHint = "[Hold RMB]  Carry";
    [SerializeField] private string releaseHint = "[Release RMB]  Drop";
    [Tooltip("Used when the carrier is in Toggle Key mode. {0} is the key name.")]
    [SerializeField] private string pickUpKeyHintFormat = "[{0}]  Pick up";
    [SerializeField] private string dropKeyHintFormat = "[{0}]  Drop";
    [SerializeField] private string interactHintFormat = "[LMB]  {0}";
    [SerializeField] private string climbHint = "[Hold W]  Climb";
    [SerializeField] private string climbJumpHint = "[Space]  Jump off wall";
    [SerializeField] private string wallRunHint = "[Hold W]  Wall run";
    [SerializeField] private string wallRunActiveHint = "[Space]  Wall jump    [Shift] / [Ctrl]  Up / Down";
    [SerializeField] private string ledgeHint = "[Space]  Jump    [WASD]  Let go";
    [SerializeField] private string handClimbHint = "[WASD]  Climb    [Space]  Push off";

    private CanvasGroup group;
    private Text titleText;
    private Text hintText;

    private void Awake()
    {
        if (carrier == null) carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        if (interactor == null) interactor = FindFirstObjectByType<PlayerCreatureInteractor>();
        if (traversal == null) traversal = FindFirstObjectByType<AdvancedFirstPersonTraversal>();
        if (handClimber == null) handClimber = FindFirstObjectByType<HandClimber>();
        BuildCanvas();
    }

    private void Update()
    {
        string title = null;
        string hint = null;
        ChoosePrompt(ref title, ref hint);

        bool visible = !string.IsNullOrEmpty(hint);
        if (visible)
        {
            titleText.text = title ?? string.Empty;
            titleText.gameObject.SetActive(!string.IsNullOrEmpty(title));
            hintText.text = hint;
        }

        float target = visible ? 1f : 0f;
        group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / fadeTime);
    }

    /// <summary>Highest priority first: what the player is doing, then what they could do.</summary>
    private void ChoosePrompt(ref string title, ref string hint)
    {
        if (carrier != null && carrier.CarriedCreature != null)
        {
            title = NameOf(carrier.CarriedCreature);
            hint = DropHint();
            string extra = carrier.CarriedCreature.CarriedPrompt;
            if (!string.IsNullOrEmpty(extra)) hint = extra + "\n" + hint;
            return;
        }

        if (handClimber != null && handClimber.isActiveAndEnabled && handClimber.IsClimbing)
        {
            if (!handClimber.IsMantling) hint = handClimbHint;
            return;
        }

        if (traversal != null && traversal.isActiveAndEnabled)
        {
            if (traversal.HoldingLedge) { hint = ledgeHint; return; }
            if (traversal.IsWallRunning) { hint = wallRunActiveHint; return; }
            if (traversal.IsClimbing && traversal.CanClimbJump) { hint = climbJumpHint; return; }
        }

        Creature carryTarget = carrier != null && carrier.isActiveAndEnabled ? carrier.FindCreature() : null;
        Creature interactTarget = interactor != null && interactor.isActiveAndEnabled ? interactor.FindCreature() : null;
        string interactVerb = interactTarget != null ? interactTarget.InteractionPrompt : null;
        if (string.IsNullOrEmpty(interactVerb)) interactTarget = null;

        if (carryTarget != null || interactTarget != null)
        {
            title = NameOf(carryTarget != null ? carryTarget : interactTarget);
            if (interactTarget != null && carryTarget != null && interactTarget != carryTarget)
                carryTarget = null; // Different targets: favour the one left-click reaches.

            string interactLine = interactTarget != null ? string.Format(interactHintFormat, interactVerb) : null;
            string carryLine = carryTarget != null ? PickUpHint() : null;
            hint = interactLine != null && carryLine != null
                ? interactLine + "    " + carryLine
                : interactLine ?? carryLine;
            return;
        }

        if (handClimber != null && handClimber.isActiveAndEnabled && handClimber.CanStartClimb)
        {
            hint = climbHint;
            return;
        }

        if (traversal != null && traversal.isActiveAndEnabled)
        {
            if (traversal.CanStartClimb) { hint = climbHint; return; }
            if (traversal.CanStartWallRun) { hint = wallRunHint; return; }
        }
    }

    private bool UsesToggleKey => carrier != null && carrier.InputMode == PlayerCreatureCarrier.CarryInput.ToggleKey;

    private string PickUpHint() =>
        UsesToggleKey ? string.Format(pickUpKeyHintFormat, carrier.ToggleKeyBinding) : carryHint;

    private string DropHint() =>
        UsesToggleKey ? string.Format(dropKeyHintFormat, carrier.ToggleKeyBinding) : releaseHint;

    private string NameOf(Creature creature)
    {
        if (!showCreatureName || creature == null) return null;
        string name = creature.CreatureName;
        return string.IsNullOrEmpty(name) || name == "Unnamed Creature" ? null : name.ToUpperInvariant();
    }

    private void BuildCanvas()
    {
        var canvasObject = new GameObject("InteractionPromptCanvas", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);

        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        group = canvasObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var stack = new GameObject("Prompt", typeof(RectTransform));
        stack.transform.SetParent(canvasObject.transform, false);
        var stackRect = (RectTransform)stack.transform;
        stackRect.anchorMin = stackRect.anchorMax = new Vector2(0.5f, 0.5f);
        stackRect.pivot = new Vector2(0.5f, 1f);
        stackRect.anchoredPosition = offsetFromCentre;
        stackRect.sizeDelta = new Vector2(1200f, 0f);

        var layout = stack.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.spacing = 4f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = stack.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText = CreateText(stack.transform, "Title", font, titleFontSize, titleColor);
        hintText = CreateText(stack.transform, "Hint", font, hintFontSize, hintColor);
    }

    private static Text CreateText(Transform parent, string name, Font font, int size, Color color)
    {
        var textObject = new GameObject(name, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);

        var text = textObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.raycastTarget = false;

        // A soft dark edge keeps white text readable over both the pale ground and dark tops.
        var shadow = textObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        return text;
    }
}
