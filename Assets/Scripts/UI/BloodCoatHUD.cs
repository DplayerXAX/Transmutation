using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class BloodCoatHUD : MonoBehaviour
{
    [SerializeField] private PlayerCreatureStats player;
    [SerializeField, Min(16f)] private float diameter = 64f;
    [SerializeField] private Vector2 inset = new Vector2(24f, 24f);
    private BloodGlobeGraphic globe;
    public bool Visible => globe != null && globe.gameObject.activeSelf;
    public float OccupiedScreenWidth => Visible && globe.canvas != null
        ? (inset.x + diameter + 16f) * globe.canvas.scaleFactor : 0f;

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
        item.SetActive(false);
    }
    private void OnEnable()
    {
        if (player == null) player = FindFirstObjectByType<PlayerCreatureStats>();
        if (player == null) return;
        player.CoatChanged += OnCoat;
        OnCoat(player.CoatHealth, player.Polarity);
    }
    private void OnDisable()
    {
        if (player != null) player.CoatChanged -= OnCoat;
        if (globe != null) globe.gameObject.SetActive(false);
    }
    private void OnCoat(float health, CreaturePolarity polarity)
    {
        globe.SetCoat(health, polarity);
        globe.gameObject.SetActive(health > 0f);
    }
}
