using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class BloodCoatHUD : MonoBehaviour
{
    [SerializeField] private PlayerCreatureStats player;
    [SerializeField, Min(16f)] private float diameter = 64f;
    [SerializeField] private Vector2 inset = new Vector2(24f, 24f);
    private BloodGlobeGraphic globe;
    private RectTransform fill;
    private UnityEngine.UI.Text stoneCount;
    public bool Visible => globe != null && globe.gameObject.activeSelf;
    public float OccupiedScreenWidth => Visible && globe.canvas != null
        ? (inset.x + diameter + 28f + stoneCount.preferredWidth) * globe.canvas.scaleFactor : 0f;

    private static bool sceneHooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSceneHook() => sceneHooked = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForLoadedScenes()
    {
        if (!sceneHooked)
        {
            SceneManager.sceneLoaded += EnsureForScene;
            sceneHooked = true;
        }
        for (int i = 0; i < SceneManager.sceneCount; i++)
            EnsureForScene(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
    }

    private static void EnsureForScene(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        foreach (var hud in FindObjectsByType<BloodCoatHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (hud.gameObject.scene == scene) return;
        PlayerCreatureStats stats = null;
        foreach (var candidate in FindObjectsByType<PlayerCreatureStats>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == scene && candidate.GetComponent<SmoothFirstPersonController>() != null)
            { stats = candidate; break; }
        if (stats == null) return;
        var root = new GameObject("Blood Coat HUD");
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene);
        var hudInstance = root.AddComponent<BloodCoatHUD>();
        hudInstance.player = stats;
        root.SetActive(true);
    }

    private void Awake()
    {
        var canvasObject = new GameObject("Blood Globe Canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50;
        var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 0.5f;
        var group = canvasObject.GetComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;
        var item = new GameObject("Blood Globe", typeof(RectTransform), typeof(CanvasRenderer), typeof(BloodGlobeGraphic));
        item.transform.SetParent(canvasObject.transform, false);
        globe = item.GetComponent<BloodGlobeGraphic>(); globe.raycastTarget = false;
        var rect = globe.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = inset; rect.sizeDelta = Vector2.one * diameter;
        var track = Piece<UnityEngine.UI.Image>("Blood points", rect, new Vector2(diameter + 6f, 0f), new Vector2(9f, diameter));
        track.color = new Color(0.1f, 0.07f, 0.09f, 0.9f);
        var meter = Piece<UnityEngine.UI.Image>("Progress", track.rectTransform, Vector2.one, new Vector2(7f, diameter - 2f));
        meter.color = new Color(0.94f, 0.36f, 0.40f);
        fill = meter.rectTransform;
        stoneCount = Piece<UnityEngine.UI.Text>("Stones earned", track.rectTransform, new Vector2(11f, 0f), new Vector2(24f, 20f));
        stoneCount.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        stoneCount.fontSize = 16;
        stoneCount.horizontalOverflow = HorizontalWrapMode.Overflow;
        stoneCount.alignment = TextAnchor.LowerLeft;
        stoneCount.gameObject.AddComponent<UnityEngine.UI.Outline>().effectColor = Color.black;
        item.SetActive(false);
    }
    private static T Piece<T>(string name, RectTransform parent, Vector2 position, Vector2 size) where T : UnityEngine.UI.Graphic
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(T));
        var graphic = go.GetComponent<T>();
        graphic.raycastTarget = false;
        var rect = graphic.rectTransform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return graphic;
    }
    private void OnEnable()
    {
        if (player == null) player = FindFirstObjectByType<PlayerCreatureStats>();
        if (player == null) return;
        player.CoatChanged += OnCoat;
        player.BloodPointsChanged += OnPoints;
        OnCoat(player.CoatHealth, player.Polarity);
    }
    private void OnDisable()
    {
        if (player != null) player.CoatChanged -= OnCoat;
        if (player != null) player.BloodPointsChanged -= OnPoints;
        if (globe != null) globe.gameObject.SetActive(false);
    }
    private void OnCoat(float health, CreaturePolarity polarity)
    {
        globe.SetCoat(health, polarity);
        OnPoints(player.BloodPoints);
        globe.gameObject.SetActive(health > 0f);
    }
    private void OnPoints(long points)
    {
        fill.localScale = new Vector3(1f, (points % PlayerCreatureStats.PointsPerStone) / (float)PlayerCreatureStats.PointsPerStone, 1f);
        stoneCount.text = (points / PlayerCreatureStats.PointsPerStone).ToString();
    }
}
