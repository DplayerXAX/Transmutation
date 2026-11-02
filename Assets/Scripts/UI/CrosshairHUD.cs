using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A small ink-like crosshair that changes with what the player can do right now:
///   - nothing to do: a faint dot;
///   - a creature in reach (pick up, pet, use): a thin circle round the dot;
///   - a wall in front that can be climbed: two hand marks either side;
///   - holding something to throw: a wide warm ring of ticks, which spreads while aiming;
///   - holding a bubble flower: a pale bubble ring;
///   - a tentacle creature on the back: a cross with a gap, pale when an arm can stick where it points,
///     dim red when not; brighter while aiming;
///   - climbing, sitting, riding a bone creature, meditating: hidden (or a faint dot while riding).
/// Shapes ease from one to the next. Added by itself; hides a plain centred image the scene may already
/// use as a crosshair, so there is only one.
/// </summary>
public sealed class CrosshairHUD : MonoBehaviour
{
    private enum Mode { Idle, Target, Climb, Throw, Flower, Tentacle, Ride, Hidden }

    [SerializeField] private Color ink = new Color(1f, 1f, 1f, 0.85f);
    [SerializeField] private Color warm = new Color(1f, 0.78f, 0.5f, 0.95f);
    [SerializeField] private Color bubble = new Color(0.7f, 0.92f, 1f, 0.9f);
    [SerializeField] private Color good = new Color(0.75f, 1f, 0.92f, 0.95f);
    [SerializeField] private Color bad = new Color(0.85f, 0.3f, 0.35f, 0.8f);

    private const int Ticks = 8;
    private RectTransform dot;
    private readonly RectTransform[] ticks = new RectTransform[Ticks];
    private Image dotImage;
    private readonly Image[] tickImages = new Image[Ticks];
    private CanvasGroup group;
    private UnityEngine.UI.Image targetRing;
    private float targetRingAlpha;
    private static Sprite targetRingSprite;
    // Throw power: a thin bar under the crosshair that fills while right click is held.
    private RectTransform chargeBack, chargeFill;
    private Image chargeBackImage, chargeFillImage;
    private const float ChargeWidth = 46f, ChargeHeight = 3f, ChargeOffset = 30f;
    private float chargeShown;

    // Eased shape: how far out the ticks are, how long, how many show, their turn, colour and dot size.
    private float radius = 10f, length = 6f, shown, turn, dotSize = 4f, alpha;
    private Color colour = Color.white;

    /// <summary>The crosshair shown right now (for tests).</summary>
    public string CurrentMode { get; private set; }

    private PlayerCreatureCarrier carrier;
    private PlayerCreatureInteractor interactor;
    private PlayerCreaturePetter petter;
    private HandClimber climber;
    private MeditationController meditation;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AddToScene()
    {
        if (FindFirstObjectByType<PlayerCreatureCarrier>() == null || FindFirstObjectByType<CrosshairHUD>() != null) return;
        new GameObject("Crosshair HUD").AddComponent<CrosshairHUD>();
    }

    private void Awake()
    {
        var canvasObject = new GameObject("Crosshair Canvas");
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        group = canvasObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        dot = Piece(canvasObject.transform, "Dot", out dotImage);
        for (int i = 0; i < Ticks; i++) ticks[i] = Piece(canvasObject.transform, "Tick", out tickImages[i]);
        var ring = Piece(canvasObject.transform, "Available Action Ring", out targetRing);
        ring.sizeDelta = Vector2.one * 26f;
        targetRing.sprite = RingSprite();
        targetRing.color = Color.clear;
        chargeBack = Piece(canvasObject.transform, "Throw Power Back", out chargeBackImage);
        chargeBack.sizeDelta = new Vector2(ChargeWidth + 2f, ChargeHeight + 2f);
        chargeBack.anchoredPosition = new Vector2(0f, -ChargeOffset);
        chargeFill = Piece(chargeBack, "Throw Power", out chargeFillImage);
        chargeFill.pivot = new Vector2(0f, 0.5f);
        chargeFill.anchorMin = chargeFill.anchorMax = new Vector2(0f, 0.5f);
        chargeFill.anchoredPosition = new Vector2(1f, 0f);
        HideSceneCrosshair();
    }

    private static RectTransform Piece(Transform parent, string name, out Image image)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        image = go.GetComponent<Image>();
        image.raycastTarget = false;
        return rect;
    }

    private static Sprite RingSprite()
    {
        if (targetRingSprite != null) return targetRingSprite;
        const int size = 96;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Action Ring", hideFlags = HideFlags.HideAndDontSave };
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), Vector2.one * (size * 0.5f));
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(2.8f - Mathf.Abs(distance - 43f)));
            }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        targetRingSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), Vector2.one * 0.5f);
        targetRingSprite.hideFlags = HideFlags.HideAndDontSave;
        return targetRingSprite;
    }

    /// <summary>A plain image sitting in the middle of a scene canvas is the old crosshair: hide it.</summary>
    private static void HideSceneCrosshair()
    {
        foreach (Image image in FindObjectsByType<Image>(FindObjectsSortMode.None))
        {
            if (image.GetComponentInParent<CrosshairHUD>() != null) continue;
            var rect = image.rectTransform;
            bool centred = rect.anchorMin == new Vector2(0.5f, 0.5f) && rect.anchorMax == new Vector2(0.5f, 0.5f) && rect.anchoredPosition.sqrMagnitude < 1f;
            if (centred && rect.sizeDelta.x <= 128f && rect.sizeDelta.y <= 128f && image.transform.childCount == 0) image.enabled = false;
        }
    }

    private void Update()
    {
        if (carrier == null) carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        if (interactor == null) interactor = FindFirstObjectByType<PlayerCreatureInteractor>();
        if (petter == null) petter = FindFirstObjectByType<PlayerCreaturePetter>();
        if (climber == null) climber = FindFirstObjectByType<HandClimber>();
        if (meditation == null) meditation = FindFirstObjectByType<MeditationController>();

        Mode mode = Choose(out bool active, out bool valid);
        CurrentMode = mode.ToString();
        // Target shape for each mode.
        float r = 0f, len = 0f, count = 0f, rot = 0f, size = 4f, a = 1f;
        Color c = ink;
        switch (mode)
        {
            case Mode.Idle: count = 0f; size = 4f; a = 0.55f; break;
            case Mode.Target: size = 4f; break;
            case Mode.Climb: r = 14f; len = 10f; count = 2f; rot = 0f; size = 3f; break;
            case Mode.Throw: r = active ? 20f : 13f; len = 5f; count = 8f; rot = 22.5f; size = 4f; c = warm; break;
            case Mode.Flower: r = active ? 15f : 11f; len = 4f; count = 8f; rot = 0f; size = 3f; c = bubble; break;
            case Mode.Tentacle: r = active ? 13f : 9f; len = active ? 9f : 7f; count = 4f; rot = 0f; size = 3f; c = valid ? good : active ? bad : ink; break;
            case Mode.Ride: count = 0f; size = 3f; a = 0.35f; break;
            case Mode.Hidden: a = 0f; break;
        }
        float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
        radius = Mathf.Lerp(radius, r, k);
        length = Mathf.Lerp(length, len, k);
        shown = Mathf.Lerp(shown, count, k);
        turn = Mathf.LerpAngle(turn, rot, k);
        dotSize = Mathf.Lerp(dotSize, size, k);
        colour = Color.Lerp(colour, c, k);
        alpha = Mathf.Lerp(alpha, a, k);
        group.alpha = alpha;
        targetRingAlpha = Mathf.Lerp(targetRingAlpha, mode == Mode.Target ? 1f : 0f, k);
        targetRing.color = new Color(ink.r, ink.g, ink.b, ink.a * targetRingAlpha);

        dot.sizeDelta = Vector2.one * dotSize;
        dotImage.color = colour;
        // Ticks spread evenly over however many show; two show as left and right hand marks.
        int whole = Mathf.CeilToInt(shown - 0.01f);
        for (int i = 0; i < Ticks; i++)
        {
            bool on = i < whole;
            ticks[i].gameObject.SetActive(on);
            if (!on) continue;
            float fade = Mathf.Clamp01(shown - i);
            float angle = (turn + i * 360f / Mathf.Max(1, whole)) * Mathf.Deg2Rad;
            Vector2 at = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (radius + length * 0.5f);
            ticks[i].anchoredPosition = at;
            // Hand marks stand upright; the others point out from the centre.
            bool hands = whole == 2;
            ticks[i].localRotation = Quaternion.Euler(0f, 0f, hands ? 0f : angle * Mathf.Rad2Deg);
            ticks[i].sizeDelta = hands ? new Vector2(2f, length) : new Vector2(length, 2f);
            Color tc = colour;
            tc.a *= fade;
            tickImages[i].color = tc;
        }
        UpdateChargeBar(mode);
    }

    private void UpdateChargeBar(Mode mode)
    {
        bool charging = mode == Mode.Throw && carrier != null && carrier.IsChargingThrow;
        float target = charging ? carrier.ThrowCharge01 : 0f;
        // Fills as fast as the charge; empties quickly after the throw.
        chargeShown = charging ? target : Mathf.MoveTowards(chargeShown, 0f, Time.unscaledDeltaTime * 4f);
        float show = charging ? 1f : Mathf.Clamp01(chargeShown * 4f);
        chargeBack.gameObject.SetActive(show > 0.01f);
        if (show <= 0.01f) return;
        chargeFill.sizeDelta = new Vector2(ChargeWidth * chargeShown, ChargeHeight);
        chargeBackImage.color = new Color(0f, 0f, 0f, 0.35f * show);
        Color fill = Color.Lerp(warm, new Color(1f, 0.45f, 0.25f, 1f), chargeShown);
        fill.a *= show;
        chargeFillImage.color = fill;
    }

    private Mode Choose(out bool active, out bool valid)
    {
        active = valid = false;
        if (meditation != null && meditation.IsMeditating) return Mode.Hidden;
        if (RestSpotSystem.Instance != null && RestSpotSystem.Instance.Sitting) return Mode.Hidden;
        if (BoneRide.Mount != null) return Mode.Ride;
        if (climber != null && climber.IsClimbing) return Mode.Hidden;
        if (carrier != null && carrier.CarriedCreature != null)
        {
            if (carrier.CarriesTentacle)
            {
                TentacleTether tether = TentacleTether.Instance;
                active = tether != null && tether.Aiming;
                valid = tether != null && tether.AimValid;
                // A shot that could not stick: the cross flashes red for a moment (there is no text).
                if (tether != null && tether.Problem != null) { active = true; valid = false; }
                return Mode.Tentacle;
            }
            if (carrier.CarriedCreature is BubbleFlower)
            {
                active = UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.isPressed;
                return Mode.Flower;
            }
            if (carrier.ThrowEnabled && CreatureThrower.Instance != null && CreatureThrower.Instance.HoldingThrowable)
            {
                active = CreatureThrower.Instance.Aiming;
                return Mode.Throw;
            }
            return Mode.Target;
        }
        bool target = (carrier != null && carrier.FindCreature() != null)
                      || (interactor != null && interactor.FindCreature() is Creature c && !string.IsNullOrEmpty(c.InteractionPrompt))
                      || (petter != null && petter.FindPettable() != null);
        if (target) return Mode.Target;
        if (climber != null && climber.CanStartClimb) return Mode.Climb;
        return Mode.Idle;
    }
}
