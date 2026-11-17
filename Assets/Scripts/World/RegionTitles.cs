using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// When the player walks into a region (the flower field, the cave, the mushroom basin, the tower),
/// glyph creatures condense out of the air flush under the top border of the screen (fixed to the camera) and write the region's
/// title in the planet's script, hold it, scatter and evaporate (DropletChoir.SummonTitle).
/// Regions are upright cylinders (centre, radius, height range); the first one containing the player
/// wins. A region's title appears when the player enters it, at most once per Cooldown.
///
/// Scene_World needs no edits: on loading a scene named Scene_World, a "Region Titles (runtime)" object
/// is made with the world map's regions (WorldMapRegions), unless the scene already has a RegionTitles.
/// The creature material is loaded from Resources/GlyphCreatures/MAT_GlyphGold when none is assigned.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlanetGlyphSource))]
public sealed class RegionTitles : MonoBehaviour
{
    public const string WorldSceneName = "Scene_World";
    public const string MaterialResource = "GlyphCreatures/MAT_GlyphGold";

    [System.Serializable]
    public sealed class Region
    {
        public string name = "Region";
        [Tooltip("A PlanetGlyphSource phrase, e.g. \"flower grow _\".")]
        public string title = "flower grow _";
        public Vector3 centre;
        [Min(1f)] public float radius = 40f;
        [Tooltip("World heights the region spans (a cave lies below the forest floor, a tower above it).")]
        public Vector2 heightRange = new Vector2(-50f, 80f);
        [Tooltip("Fade of this region's music outside its edge (used by SceneAudioBoot); 0 = no region music.")]
        [Min(0f)] public float musicFade = 0f;
        [System.NonSerialized] public float lastShown = float.NegativeInfinity;
        /// <summary>The region's music channel, when the scene has one: the title then follows the music.</summary>
        [System.NonSerialized] public RegionMusicChannel music;

        public Region() { }

        public Region(string name, string title, Vector3 centre, float radius, Vector2 heightRange, float musicFade = 0f)
        {
            this.name = name;
            this.title = title;
            this.centre = centre;
            this.radius = radius;
            this.heightRange = heightRange;
            this.musicFade = musicFade;
        }

        /// <summary>
        /// Whether a point counts as being in the region. With region music, that is where the music is at
        /// least half in, so the title and the music change together; otherwise the cylinder itself.
        /// </summary>
        public bool Entered(Vector3 p) => music != null ? music.Weight(p) >= 0.5f : Contains(p);

        public bool Contains(Vector3 p)
        {
            Vector2 d = new Vector2(p.x - centre.x, p.z - centre.z);
            return d.sqrMagnitude <= radius * radius && p.y >= heightRange.x && p.y <= heightRange.y;
        }
    }

    [SerializeField] private Region[] regions = new Region[0];
    [SerializeField] private Material creatureMaterial;
    [Tooltip("Seconds before the same region's title can appear again.")]
    [Min(0f)] [SerializeField] private float cooldown = 90f;
    [Tooltip("Seconds the player must stay inside before the title appears (no flicker at borders).")]
    [Min(0f)] [SerializeField] private float settleTime = 0.4f;
    [Tooltip("How far in front of the eyes the title hangs (it follows the view lazily).")]
    [Min(1.5f)] [SerializeField] private float distanceAhead = 4f;
    [Tooltip("Gap between the top border of the screen and the title's top, as a share of the screen height.")]
    [Range(0f, 0.3f)] [SerializeField] private float topMargin = 0.035f;
    [Tooltip("Height of one glyph in metres.")]
    [Min(0.2f)] [SerializeField] private float glyphHeight = 0.75f;
    [Tooltip("Seconds the finished title holds before it scatters.")]
    [Min(0f)] [SerializeField] private float holdTime = 6f;

    private PlanetGlyphSource source;
    private Transform player;
    private Transform view;
    private Region current, pending;
    private float pendingTime;
    private DropletChoir active;

    public Region[] Regions => regions;
    /// <summary>The region the player is in now (null between regions).</summary>
    public Region Current => current;
    /// <summary>The summoned choir writing a title now, if any.</summary>
    public DropletChoir Active => active;

    /// <summary>Scene_World's regions, from the world map layout (WorldMapTerrain).</summary>
    public static Region[] WorldMapRegions()
    {
        Vector3 gooLow = WorldMapTerrain.GooOffset + WorldMapTerrain.GooLower;
        Vector3 gooHigh = WorldMapTerrain.GooOffset + WorldMapTerrain.GooUpper;
        return new[]
        {
            new Region("Flower Field", PlanetGlyphSource.FlowerField,
                new Vector3(WorldMapTerrain.FlowerCentre.x, 0f, WorldMapTerrain.FlowerCentre.y), 50f, new Vector2(-20f, 60f), 15f),
            // The cave lies under the forest; only its underground heights count, so the forest above does not.
            new Region("Cave", PlanetGlyphSource.Cave,
                new Vector3((gooLow.x + gooHigh.x) * 0.5f, 0f, (gooLow.z + gooHigh.z) * 0.5f), 90f, new Vector2(gooLow.y, -6f), 8f),
            // The crystal mining island floats above the flower field and mushroom basin; listed before them
            // so standing on it counts as the island.
            new Region("Mining Island", PlanetGlyphSource.MiningIsland,
                new Vector3(WorldMapTerrain.IslandOffset.x, 0f, WorldMapTerrain.IslandOffset.z), WorldMapTerrain.IslandRadius,
                new Vector2(WorldMapTerrain.IslandOffset.y - 30f, WorldMapTerrain.IslandOffset.y + 150f), 15f),
            new Region("Mushroom Basin", PlanetGlyphSource.MushroomBasin,
                WorldMapTerrain.MushroomOffset, WorldMapTerrain.MushroomRadius - 2f, new Vector2(-40f, 20f)),
            new Region("Tower", PlanetGlyphSource.Tower,
                new Vector3(WorldMapTerrain.SpireSpot.x, 0f, WorldMapTerrain.SpireSpot.y), 55f, new Vector2(8f, 170f)),
        };
    }

    private static bool hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => hooked = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!hooked)
        {
            hooked = true;
            SceneManager.sceneLoaded += (scene, mode) => EnsureFor(scene);
        }
        EnsureFor(SceneManager.GetActiveScene());
    }

    /// <summary>Adds the runtime Region Titles to Scene_World once (never a second copy).</summary>
    private static void EnsureFor(Scene scene)
    {
        if (!scene.IsValid() || scene.name != WorldSceneName) return;
        if (FindFirstObjectByType<RegionTitles>() != null) return;
        var go = new GameObject("Region Titles (runtime)");
        SceneManager.MoveGameObjectToScene(go, scene);
        var titles = go.AddComponent<RegionTitles>();
        titles.regions = WorldMapRegions();
    }

    private bool linked;

    /// <summary>Pairs each region with the scene's "Region Music - name" channel, if there is one.</summary>
    private void LinkMusic()
    {
        linked = true;
        foreach (RegionMusicChannel channel in FindObjectsByType<RegionMusicChannel>(FindObjectsSortMode.None))
            foreach (Region region in regions)
                if (channel.name == "Region Music - " + region.name) region.music = channel;
    }

    private void Awake() => source = GetComponent<PlanetGlyphSource>();

    private void Update()
    {
        if (player == null)
        {
            var carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
            if (carrier == null) return;
            player = carrier.transform;
        }
        if (view == null && Camera.main != null) view = Camera.main.transform;

        if (!linked) LinkMusic();
        // The music is heard at the camera, so the title is judged there too.
        Vector3 at = view != null ? view.position : player.position;
        Region inside = null;
        float best = 0f;
        foreach (Region region in regions)
        {
            if (!region.Entered(at)) continue;
            // Overlapping regions (the island above the field): the one whose music is strongest wins.
            float weight = region.music != null ? region.music.Weight(at) : 0.75f;
            if (inside == null || weight > best) { inside = region; best = weight; }
        }

        if (inside != pending) { pending = inside; pendingTime = 0f; }
        pendingTime += Time.deltaTime;
        if (pending == current || pendingTime < settleTime) return;
        current = pending;
        if (current != null && Time.time - current.lastShown >= cooldown) Show(current);
    }

    /// <summary>Writes this region's title in the upper middle of the view now (also used by tests).</summary>
    public DropletChoir Show(Region region)
    {
        if (player == null) return null;
        if (view == null && Camera.main != null) view = Camera.main.transform;
        if (view == null) return null;
        if (creatureMaterial == null) creatureMaterial = Resources.Load<Material>(MaterialResource);
        region.lastShown = Time.time;
        if (active != null) active.Release();
        active = DropletChoir.SummonTitle(view, region.title, source, creatureMaterial, glyphHeight, distanceAhead, holdTime, topMargin);
        active.name = "Region Title (" + region.name + ")";
        return active;
    }
}
