using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The dark tunnels behind the spawn chamber of Scene_World (rock carved by DarkTunnelShape) and what lives in them.
/// Added by itself when Scene_World loads (like SceneAudioBoot), so rebuilding the scene keeps it; a DarkTunnels
/// already in the scene is used instead. Once the terrain round the tunnels is built it places:
///   Bell Snails on the walls (the fixed lanterns), Glim Lanterns round them, one Lure Maw in the hall,
///   and the skeleton holding the conductor baton in the deepest chamber.
/// Darkness: while the camera is in the tunnels the fog turns black and close (a few metres of sight, less in the
/// deep descent), sun and ambient light go down. Glow creatures use unfogged shaders, so they show from far away.
/// A carried Glim lets you see further. Everything is put back on the way out.
/// </summary>
public sealed class DarkTunnels : MonoBehaviour
{
    [Header("Sight")]
    [Tooltip("Metres you can see in the tunnels.")]
    [SerializeField] private float sight = 8f;
    [Tooltip("Metres you can see in the deep descent and the skeleton chamber.")]
    [SerializeField] private float deepSight = 3.5f;
    [Tooltip("Extra metres of sight while carrying a Glim.")]
    [SerializeField] private float lanternSight = 7f;

    [Header("Baton")]
    [Tooltip("On: the player holds the conductor baton from the start (testing). Off: it must be taken from the skeleton's fist.")]
    [SerializeField] private bool startWithBaton = true;

    public static DarkTunnels Instance { get; private set; }
    public static int TerrainMask { get; private set; } = 1 << 7;
    /// <summary>0..1 how far the dark has closed in on the camera now.</summary>
    public static float Darkness { get; private set; }
    public bool Built { get; private set; }

    public Vector3 EntranceWorld => DarkTunnelShape.Mouth + new Vector3(-6f, -0.5f, -2f);
    public Vector3 EntranceOutsideWorld => DarkTunnelShape.Mouth + new Vector3(6f, 0.6f, 0f);
    public Vector3 HallWorld => DarkTunnelShape.HallCentre + Vector3.down * 2.5f;
    public Vector3 SkeletonWorld => DarkTunnelShape.Skeleton + Vector3.down * 2f;

    private ProceduralWorld world;
    private PlayerCreatureCarrier carrier;
    private float shownSight, shownDark;
    private bool overriding, warned;
    private bool savedFog;
    private FogMode savedMode;
    private Color savedFogColor, savedSky, savedEquator, savedGround, savedAmbient;
    private float savedStart, savedEnd, savedDensity, savedAmbientIntensity, savedReflection, savedSun = -1f;
    private Light sun;

    // ---------------- Boot ----------------

    private static bool hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { hooked = false; Instance = null; Darkness = 0f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook()
    {
        if (hooked) return;
        SceneManager.sceneLoaded += OnSceneLoaded;
        hooked = true;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != RegionTitles.WorldSceneName || FindFirstObjectByType<DarkTunnels>() != null) return;
        var go = new GameObject("Dark Tunnels (runtime)");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<DarkTunnels>();
    }

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        Restore();
        if (Instance == this) Instance = null;
    }

    // ---------------- Placing ----------------

    private void Update()
    {
        if (world == null) world = FindFirstObjectByType<ProceduralWorld>();
        if (world == null) return;
        TerrainMask = 1 << world.TerrainLayer;
        if (!Built && world.GenerationComplete) TryBuild();
        UpdateDarkness();
    }

    private void TryBuild()
    {
        // The tunnels exist only once the saved chunk meshes were baked after the carve.
        if (!Physics.Raycast(DarkTunnelShape.HallCentre, Vector3.down, out RaycastHit floor, 12f, TerrainMask) ||
            world.SampleDensity(DarkTunnelShape.HallCentre) > 0f)
        {
            if (!warned) Debug.LogWarning("[DarkTunnels] No tunnel hall found under the spawn forest. Bake Scene_World's chunk meshes again (TerrainChunks.bytes).", this);
            warned = true;
            return;
        }
        Built = true;
        Random.State saved = Random.state;
        Random.InitState(4127);

        // Bell Snails: the lanterns that mark the way. Each is put on the rock found from a point in the air.
        BellSnail[] snails =
        {
            Snail(new Vector3(-195f, -23f, -146f), Vector3.right),           // hall, east wall
            Snail(new Vector3(-216f, -22f, -146f), Vector3.left),            // hall, west wall
            Snail(DarkTunnelShape.Grotto, Vector3.up),                       // grotto ceiling
            Snail(new Vector3(-222f, -15f, -118f), Vector3.left),            // upper loop
            Snail(new Vector3(-213f, -27.3f, -168f), Vector3.left),          // top of the deep descent
            Snail(new Vector3(-177f, -24f, -158f), Vector3.right),           // east loop
            Snail(new Vector3(-201f, -16.5f, -119f), Vector3.down),          // where the upper loop meets the crawl
        };
        // Glims round the snails (two at the hall's east wall, two in the grotto, one on the upper loop).
        int[] at = { 0, 0, 2, 2, 3, 1 };
        foreach (int i in at)
            if (snails[i] != null) Glim(snails[i].transform.position + snails[i].SurfaceNormal * 1.2f + Random.insideUnitSphere * 0.6f);

        // One Lure Maw, its den on the hall floor near the descent.
        Vector3 den = new Vector3(-195f, -24f, -151f);
        if (Physics.Raycast(den, Vector3.down, out RaycastHit denFloor, 8f, TerrainMask))
        {
            var maw = Make<LureMaw>("Lure Maw", denFloor.point);
            maw.SetDen(denFloor.point, new Vector3(1f, 0f, 0.6f));
        }

        // The skeleton and its baton in the deepest chamber.
        if (Physics.Raycast(DarkTunnelShape.Skeleton, Vector3.down, out RaycastHit boneFloor, 8f, TerrainMask))
        {
            var bones = new GameObject("Skeleton with the baton").AddComponent<BatonSkeleton>();
            bones.transform.SetParent(transform, true);
            bones.Build(boneFloor.point + Vector3.down * 0.35f, new Vector3(-0.3f, 0f, 1f));
            var baton = new GameObject("Conductor Baton").AddComponent<ConductorBaton>();
            baton.Build(bones);
            if (startWithBaton) baton.Take(true);
        }
        Random.state = saved;
        Debug.Log("[DarkTunnels] Tunnel creatures and the baton placed.", this);
    }

    private BellSnail Snail(Vector3 air, Vector3 direction)
    {
        // The given direction first, then a few around it, so a tunnel mouth in the way does not lose the snail.
        RaycastHit hit = default;
        bool found = false;
        foreach (float turn in new[] { 0f, 40f, -40f, 80f, -80f })
        {
            Vector3 dir = Quaternion.AngleAxis(turn, Vector3.up) * direction;
            if (Physics.Raycast(air, dir, out hit, 16f, TerrainMask)) { found = true; break; }
        }
        if (!found)
        {
            Debug.LogWarning("[DarkTunnels] No rock for a Bell Snail from " + air, this);
            return null;
        }
        var s = Make<BellSnail>("Bell Snail", hit.point);
        s.Attach(hit.point, hit.normal);
        return s;
    }

    private GlimLantern Glim(Vector3 point)
    {
        var g = Make<GlimLantern>("Glim Lantern", point);
        g.SetHome(point);
        return g;
    }

    private T Make<T>(string name, Vector3 position) where T : Component
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = position;
        go.AddComponent<Rigidbody>().isKinematic = true;
        return go.AddComponent<T>();
    }

    // ---------------- Darkness ----------------

    private void UpdateDarkness()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 eye = cam.transform.position;
        float dark = DarkTunnelShape.Darkness(eye);
        if (carrier == null) carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        bool lantern = carrier != null && carrier.CarriedCreature is GlimLantern;
        float want = Mathf.Lerp(sight, deepSight, DarkTunnelShape.Deep(eye)) + (lantern ? lanternSight : 0f);
        shownSight = shownSight <= 0f ? want : Mathf.MoveTowards(shownSight, want, Time.deltaTime * 4f);
        shownDark = Mathf.MoveTowards(shownDark, dark, Time.deltaTime * 0.8f);
        Darkness = shownDark;

        if (shownDark <= 0.001f) { Restore(); return; }
        if (!overriding) Save();
        float d = shownDark;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Color.Lerp(savedFogColor, Color.black, d);
        RenderSettings.fogStartDistance = Mathf.Lerp(40f, 0.6f, d);
        RenderSettings.fogEndDistance = Mathf.Lerp(300f, shownSight, d);
        float keep = 1f - d * 0.92f;
        RenderSettings.ambientSkyColor = savedSky * keep;
        RenderSettings.ambientEquatorColor = savedEquator * keep;
        RenderSettings.ambientGroundColor = savedGround * keep;
        RenderSettings.ambientLight = savedAmbient * keep;
        RenderSettings.reflectionIntensity = savedReflection * keep;
        if (sun != null) sun.intensity = savedSun * (1f - d);
    }

    private void Save()
    {
        overriding = true;
        savedFog = RenderSettings.fog;
        savedMode = RenderSettings.fogMode;
        savedFogColor = RenderSettings.fogColor;
        savedStart = RenderSettings.fogStartDistance;
        savedEnd = RenderSettings.fogEndDistance;
        savedDensity = RenderSettings.fogDensity;
        savedSky = RenderSettings.ambientSkyColor;
        savedEquator = RenderSettings.ambientEquatorColor;
        savedGround = RenderSettings.ambientGroundColor;
        savedAmbient = RenderSettings.ambientLight;
        savedAmbientIntensity = RenderSettings.ambientIntensity;
        savedReflection = RenderSettings.reflectionIntensity;
        sun = RenderSettings.sun;
        if (sun == null)
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.isActiveAndEnabled) { sun = l; break; }
        savedSun = sun != null ? sun.intensity : -1f;
    }

    private void Restore()
    {
        if (!overriding) return;
        overriding = false;
        RenderSettings.fog = savedFog;
        RenderSettings.fogMode = savedMode;
        RenderSettings.fogColor = savedFogColor;
        RenderSettings.fogStartDistance = savedStart;
        RenderSettings.fogEndDistance = savedEnd;
        RenderSettings.fogDensity = savedDensity;
        RenderSettings.ambientSkyColor = savedSky;
        RenderSettings.ambientEquatorColor = savedEquator;
        RenderSettings.ambientGroundColor = savedGround;
        RenderSettings.ambientLight = savedAmbient;
        RenderSettings.ambientIntensity = savedAmbientIntensity;
        RenderSettings.reflectionIntensity = savedReflection;
        if (sun != null && savedSun >= 0f) sun.intensity = savedSun;
    }
}
