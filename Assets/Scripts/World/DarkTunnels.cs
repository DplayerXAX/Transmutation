using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The dark caves under the crack of Scene_World (the inner cavern plus the rock and tunnels of DarkTunnelShape) and
/// what lives in them. Added by itself when Scene_World loads (like SceneAudioBoot), so rebuilding the scene keeps it;
/// a DarkTunnels already in the scene is used instead. Once the terrain round the caves is built it places:
///   Bell Snails on pillars and tunnel walls (the fixed lanterns), Glim Lanterns round them, one Lure Maw on the
///   cavern floor, three Tone Mesas lying over the holes down into the floor tunnels, the strange water (hanging pools, floating blobs, drips), and the
///   skeleton in the deepest chamber, holding the conductor baton unless the player already has it.
/// Darkness: below the inner cavern's ceiling (and down the spiral) the fog turns black and close (a few metres of
/// sight, less in the floor tunnels), sun and ambient light go down. Glow creatures and the water's glow are unfogged,
/// so they show from far away.
/// A carried Glim lets you see further. Everything is put back on the way out.
/// </summary>
public sealed class DarkTunnels : MonoBehaviour
{
    [Header("Sight")]
    [Tooltip("Metres you can see in the tunnels.")]
    [SerializeField] private float sight = 1.9f;
    [Tooltip("Metres you can see in the deep descent and the skeleton chamber.")]
    [SerializeField] private float deepSight = 1.2f;
    [Tooltip("Extra metres of sight while carrying a Glim.")]
    [SerializeField] private float lanternSight = 2.5f;

    [Header("Baton")]
    [Tooltip("On: the player holds the conductor baton from the start (testing). Off: it must be taken from the skeleton's fist.")]
    [SerializeField] private bool startWithBaton = true;

    [Header("Testing")]
    [Tooltip("On: once the caves are built the player is moved down to the cavern floor by the stair tower's door. Off: normal spawn.")]
    [SerializeField] private bool spawnUnderground = false;

    public static DarkTunnels Instance { get; private set; }
    public static int TerrainMask { get; private set; } = 1 << 7;
    /// <summary>0..1 how far the dark has closed in on the camera now.</summary>
    public static float Darkness { get; private set; }
    public bool Built { get; private set; }

    public Vector3 EntranceWorld => DarkTunnelShape.TowerExit;
    public Vector3 EntranceOutsideWorld => DarkTunnelShape.Mouth + Vector3.up * 1.5f;
    public Vector3 HallWorld => DarkTunnelShape.Hub + Vector3.up * 0.5f;
    public Vector3 SkeletonWorld => DarkTunnelShape.Skeleton + Vector3.down * 2f;
    public ConductorBaton Baton { get; private set; }

    private ProceduralWorld world;
    private PlayerCreatureCarrier carrier;
    private float shownSight, shownDark;
    private bool overriding, warned, snapDark;
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
        if (startWithBaton && Baton == null) GiveBaton();
        if (!Built && world.GenerationComplete) TryBuild();
        UpdateDarkness();
    }

    // The baton in the player's hand from the start (DarkTunnels.startWithBaton), as soon as there is a player.
    private void GiveBaton()
    {
        if (carrier == null) carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        if (carrier == null || carrier.PlayerBody == null) return;
        var baton = new GameObject("Conductor Baton").AddComponent<ConductorBaton>();
        baton.transform.position = carrier.PlayerBody.position + Vector3.up;
        baton.Build(null);
        if (baton.Take(true)) Baton = baton;
        else Destroy(baton.gameObject);
    }

    private void TryBuild()
    {
        // The caves exist only once the saved chunk meshes were baked after the carve: the grotto must be air.
        if (!Physics.Raycast(DarkTunnelShape.Grotto, Vector3.down, out RaycastHit floor, 12f, TerrainMask) ||
            world.SampleDensity(DarkTunnelShape.Grotto) > 0f)
        {
            if (!warned) Debug.LogWarning("[DarkTunnels] No caves found under the crack. Bake Scene_World's chunk meshes again (TerrainChunks.bytes).", this);
            warned = true;
            return;
        }
        Built = true;
        Random.State saved = Random.state;
        Random.InitState(4127);

        // Bell Snails: the lanterns that mark the way. Each is put on the rock found from a point in the air.
        BellSnail[] snails =
        {
            Snail(new Vector3(-108f, -58f, 172f), Vector3.left),             // pillar by the hub
            Snail(new Vector3(-119f, -58f, 150f), Vector3.left),             // pillar south-west of the hub
            Snail(new Vector3(-89f, -56f, 182f), Vector3.left),              // pillar north-east
            Snail(new Vector3(-140f, -58f, 140f), Vector3.back),             // stair tower, by its door
            Snail(DarkTunnelShape.Grotto, Vector3.up),                       // grotto ceiling
            Snail(new Vector3(-80f, -73f, 150f), Vector3.down),              // floor loop
            Snail(new Vector3(-74f, -72f, 163f), Vector3.down),              // top of the deep descent
            Snail(new Vector3(-120f, -25f, 180f), Vector3.up),               // ceiling tunnel
        };
        // Glims round the snails.
        int[] at = { 0, 0, 1, 4, 4, 7, 3 };
        foreach (int i in at)
            if (snails[i] != null) Glim(snails[i].transform.position + snails[i].SurfaceNormal * 1.2f + Random.insideUnitSphere * 0.6f)
                .Note = i % 2 == 0 ? CaveNote.E : CaveNote.B;
        // Notes: snails G, the Maw D, Glims E or B (above), the mesas one each.
        foreach (BellSnail sn in snails) if (sn != null) sn.Note = CaveNote.G;

        // One Lure Maw, its den on the cavern floor north of the hub.
        Vector3 den = new Vector3(-104f, -58f, 176f);
        if (Physics.Raycast(den, Vector3.down, out RaycastHit denFloor, 12f, TerrainMask))
        {
            var maw = Make<LureMaw>("Lure Maw", denFloor.point);
            maw.SetDen(denFloor.point, new Vector3(-1f, 0f, -0.6f));
            maw.Note = CaveNote.D;
        }

        // Tone Mesas: huge slabs of living rock lying over the holes down into the floor tunnels; play their note to
        // slide them off the way.
        Mesa(new Vector3(-100f, -64f, 152f), CaveNote.E);   // over the floor loop (and the way to the skeleton)
        Mesa(new Vector3(-130f, -64f, 166f), CaveNote.B);   // over the branch to the grotto
        Mesa(new Vector3(-112f, -64f, 186f), CaveNote.G);   // over the north dead end

        // Strange water: pools hanging from the cavern ceiling (dripping down), blobs floating in the dark.
        StrangeWater.Build(transform);

        // The skeleton in the deepest chamber, with the baton in its fist unless the player has it already.
        if (Physics.Raycast(DarkTunnelShape.Skeleton, Vector3.down, out RaycastHit boneFloor, 8f, TerrainMask))
        {
            var bones = new GameObject("Skeleton with the baton").AddComponent<BatonSkeleton>();
            bones.transform.SetParent(transform, true);
            bones.Build(boneFloor.point + Vector3.down * 0.35f, new Vector3(-0.3f, 0f, 1f));
            if (Baton != null) Baton.SetSkeleton(bones);
            else
            {
                Baton = new GameObject("Conductor Baton").AddComponent<ConductorBaton>();
                Baton.Build(bones);
            }
        }
        Random.state = saved;
        if (spawnUnderground) MovePlayerDown();
        Debug.Log("[DarkTunnels] Tunnel creatures and the baton placed.", this);
    }

    // Testing: start on the cavern floor just outside the stair tower, looking towards the hub.
    private void MovePlayerDown()
    {
        if (carrier == null) carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        Rigidbody player = carrier != null ? carrier.PlayerBody : null;
        if (player == null) return;
        // Let go of any wall first: climbing hands would stay gripping the old spot.
        var climber = FindFirstObjectByType<HandClimber>();
        if (climber != null && climber.IsClimbing) climber.StopForBubbleCapture();
        Vector3 at = DarkTunnelShape.TowerExit + new Vector3(0f, 0f, 4f);
        if (Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out RaycastHit floor, 10f, TerrainMask)) at = floor.point + Vector3.up * 1.1f;
        player.position = at;
        player.transform.position = at;
        player.linearVelocity = Vector3.zero;
        snapDark = true; // straight into the dark, no fade from the surface light
        var controller = player.GetComponent<SmoothFirstPersonController>();
        if (controller == null) controller = FindFirstObjectByType<SmoothFirstPersonController>();
        Vector3 look = DarkTunnelShape.Hub - at;
        if (controller != null) controller.SetLook(Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, 0f);
    }

    private void Mesa(Vector3 at, CaveNote tone)
    {
        if (!Physics.Raycast(at + Vector3.up * 6f, Vector3.down, out RaycastHit floor, 14f, TerrainMask)) return;
        Make<ToneCrate>("Tone Mesa (" + tone + ")", floor.point).Settle(floor.point, tone);
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
        shownDark = snapDark ? dark : Mathf.MoveTowards(shownDark, dark, Time.deltaTime * 0.8f);
        snapDark = false;
        Darkness = shownDark;

        if (shownDark <= 0.001f) { Restore(); return; }
        if (!overriding) Save();
        float d = shownDark;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Color.Lerp(savedFogColor, Color.black, d);
        RenderSettings.fogStartDistance = Mathf.Lerp(40f, Mathf.Min(0.6f, shownSight * 0.3f), d);
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
