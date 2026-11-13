using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gives Scene_World the audio setup Scene_Daniel has, when the scene loads: Wwise init, the AudioManager
/// with the event bank (Resources/AudioEvents), the spire climb channel, the climb reach sound, and the
/// region music (flower field, cave, mining island). Done at load time so rebuilding the scene keeps it.
/// Anything the scene already has is left alone.
/// </summary>
public static class SceneAudioBoot
{
    private static readonly string[] Scenes = { RegionTitles.WorldSceneName };
    private static bool hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => hooked = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook()
    {
        if (hooked) return;
        SceneManager.sceneLoaded += OnSceneLoaded;
        hooked = true;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (System.Array.IndexOf(Scenes, scene.name) < 0) return;

        var root = new GameObject("Audio (runtime)");
        SceneManager.MoveGameObjectToScene(root, scene);

        if (Object.FindFirstObjectByType<AkInitializer>() == null)
        {
            var wwise = new GameObject("WwiseGlobal (runtime)");
            SceneManager.MoveGameObjectToScene(wwise, scene);
            wwise.AddComponent<AkInitializer>();
        }

        Camera cam = Camera.main;
        if (cam != null && Object.FindFirstObjectByType<AkAudioListener>() == null) cam.gameObject.AddComponent<AkAudioListener>();

        if (AudioManager.Instance == null && Object.FindFirstObjectByType<AudioManager>() == null)
        {
            var go = new GameObject("AudioManager");
            go.transform.SetParent(root.transform);
            go.AddComponent<AudioManager>().SetBank(Resources.Load<AudioEventBank>("AudioEvents"));
        }

        HangingSpire spire = Object.FindFirstObjectByType<HangingSpire>();
        if (spire != null && spire.GetComponent<ClimbMusicChannel>() == null)
            spire.gameObject.AddComponent<ClimbMusicChannel>().layerEvent = "BGM_climb";

        HandClimber climber = Object.FindFirstObjectByType<HandClimber>();
        if (climber != null && climber.GetComponent<ClimbReachSound>() == null) climber.gameObject.AddComponent<ClimbReachSound>();

        FirstPersonBody body = Object.FindFirstObjectByType<FirstPersonBody>();
        if (body != null && body.GetComponent<Footsteps>() == null) body.gameObject.AddComponent<Footsteps>();

        if (Object.FindFirstObjectByType<RegionMusicChannel>() == null) AddWorldRegions(root.transform);
    }

    // Region music for the world map; the region shapes are the same ones the region titles use.
    private static void AddWorldRegions(Transform parent)
    {
        foreach (RegionTitles.Region r in RegionTitles.WorldMapRegions())
        {
            if (r.name == "Flower Field") Region(parent, r.name, "BGM_flowers", r.centre, r.radius, r.heightRange, 15f);
            else if (r.name == "Cave") Region(parent, r.name, "BGM_hungrycave", r.centre, r.radius, r.heightRange, 8f);
        }
        // The crystal mining island floats above the flower field and mushroom basin.
        Vector3 island = WorldMapTerrain.IslandOffset;
        Region(parent, "Mining Island", "BGM_island", new Vector3(island.x, 0f, island.z), WorldMapTerrain.IslandRadius,
            new Vector2(island.y - 30f, island.y + 150f), 15f);
    }

    private static void Region(Transform parent, string name, string layer, Vector3 centre, float radius, Vector2 heights, float fade)
    {
        var go = new GameObject("Region Music - " + name);
        go.transform.SetParent(parent);
        go.transform.position = centre;
        var ch = go.AddComponent<RegionMusicChannel>();
        ch.layerEvent = layer;
        ch.radius = radius;
        ch.heightRange = heights;
        ch.fadeDistance = fade;
    }
}
