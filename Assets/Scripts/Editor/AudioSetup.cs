using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Sets up adaptive music in the open scene: event bank asset, AudioManager object,
/// and a music channel on the landmark. Creature channels live in the bank.
/// </summary>
public static class AudioSetup
{
    private const string Folder = "Assets/Audio";
    private const string BankPath = Folder + "/Resources/AudioEvents.asset";
    private const string SfxFolder = Folder + "/SFX";

    [MenuItem("Tools/Capstone/Audio/Set Up Adaptive Music In Open Scene")]
    private static void SetUp()
    {
        AudioEventBank bank = LoadOrCreateBank();
        int linked = LinkClips(bank);

        AudioManager manager = Object.FindFirstObjectByType<AudioManager>();
        if (manager == null)
        {
            var go = new GameObject("AudioManager");
            Undo.RegisterCreatedObjectUndo(go, "Add AudioManager");
            manager = go.AddComponent<AudioManager>();
        }
        var so = new SerializedObject(manager);
        so.FindProperty("bank").objectReferenceValue = bank;
        so.ApplyModifiedProperties();

        string note = "";
        HangingSpire spire = Object.FindFirstObjectByType<HangingSpire>();
        if (spire != null && spire.GetComponent<ClimbMusicChannel>() == null)
        {
            ClimbMusicChannel climb = Undo.AddComponent<ClimbMusicChannel>(spire.gameObject);
            climb.layerEvent = "BGM_climb";
            note += "\nAdded a BGM_climb channel to the Hanging Spire.";
        }
        if (spire != null && !HasPlainChannel(spire.gameObject))
        {
            MusicChannel ch = Undo.AddComponent<MusicChannel>(spire.gameObject);
            ch.layerEvent = "BGM_landmark";
            ch.innerRadius = 15f;
            ch.outerRadius = 70f;
            note += "\nAdded a BGM_landmark channel to the Hanging Spire.";
        }

        HandClimber climber = Object.FindFirstObjectByType<HandClimber>();
        if (climber != null && climber.GetComponent<ClimbReachSound>() == null)
        {
            Undo.AddComponent<ClimbReachSound>(climber.gameObject);
            note += "\nAdded ClimbReachSound to the player.";
        }

        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world != null && world.GetComponent<CaveMusicChannel>() == null)
        {
            CaveMusicChannel cave = Undo.AddComponent<CaveMusicChannel>(world.gameObject);
            cave.layerEvent = "BGM_cave";
            note += "\nAdded a BGM_cave channel to the ProceduralWorld.";
        }

        FirstPersonBody player = Object.FindFirstObjectByType<FirstPersonBody>();
        if (player != null && player.GetComponent<Footsteps>() == null)
        {
            Undo.AddComponent<Footsteps>(player.gameObject);
            note += "\nAdded Footsteps to the player body.";
        }

        if (Object.FindFirstObjectByType<AudioListener>() == null)
            note += "\nWarning: no AudioListener in the scene.";

        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        Selection.activeObject = bank;
        EditorUtility.DisplayDialog("Audio",
            "AudioManager is in the scene and uses " + BankPath + ".\n" +
            "Backend is Wwise: PlayBGM is a Wwise event, each layer (BGM_fuzzy, BGM_cave, ...) is a Game Parameter (0-100).\n" +
            "Switch the AudioManager backend to Unity to hear the placeholder synth instead.\n" +
            "Linked " + linked + " Unity clip(s) by file name." + note +
            "\nSave the scene to keep it.", "OK");
    }

    private static bool HasPlainChannel(GameObject go)
    {
        foreach (MusicChannel ch in go.GetComponents<MusicChannel>())
            if (ch.GetType() == typeof(MusicChannel)) return true;
        return false;
    }

    [MenuItem("Tools/Capstone/Audio/Add Music Zone At Scene View")]
    private static void AddZone()
    {
        Vector3 pos = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        var go = new GameObject("Music Zone");
        Undo.RegisterCreatedObjectUndo(go, "Add Music Zone");
        go.transform.position = pos;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(20f, 10f, 20f);
        var ch = go.AddComponent<MusicChannel>();
        ch.zone = box;
        ch.innerRadius = 0f;
        ch.outerRadius = 15f;
        ch.layerEvent = "BGM_fuzzy";
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
    }

    [MenuItem("Tools/Capstone/Audio/Relink Clips By File Name")]
    private static void Relink()
    {
        AudioEventBank bank = LoadOrCreateBank();
        int linked = LinkClips(bank);
        EditorUtility.DisplayDialog("Audio", "Linked " + linked + " clip(s).", "OK");
    }

    [MenuItem("Tools/Capstone/Audio/Add Sound Effects (Assets/Audio/SFX)")]
    private static void AddSoundEffectsMenu()
    {
        int added = AddSoundEffects();
        EditorUtility.DisplayDialog("Audio", "Sound effects set up (" + added + " new event(s)).", "OK");
    }

    // Sound effects from Assets/Audio/SFX: tentacle shots, the tentacle creature's nearby loop, mouse buttons,
    // bubble pop and the tutorial music. Existing entries keep their tuning and only get their clips refreshed.
    internal static int AddSoundEffects()
    {
        AudioEventBank bank = LoadOrCreateBank();
        int added = 0;
        AudioClip Clip(string file) => AssetDatabase.LoadAssetAtPath<AudioClip>(SfxFolder + "/" + file + ".wav");
        void Sound(string name, string[] files, float volume, float spatial, float maxDistance, bool loop, Vector2 pitch)
        {
            AudioEventDef def = bank.Find(name);
            if (def == null)
            {
                def = new AudioEventDef { name = name, kind = AudioEventKind.Sound, volume = volume, spatialBlend = spatial,
                    maxDistance = maxDistance, loop = loop, pitchRange = pitch, fadeIn = 1.5f, fadeOut = 2.5f };
                bank.events.Add(def);
                added++;
            }
            var clips = new System.Collections.Generic.List<AudioClip>();
            foreach (string f in files)
            {
                AudioClip c = Clip(f);
                if (c != null) clips.Add(c);
                else Debug.LogWarning("[Audio] Missing " + SfxFolder + "/" + f + ".wav");
            }
            def.clips = clips.ToArray();
        }

        Sound("Play_ShootTentacle", new[] { "shootTenacles_D2", "shootTenacles_F2" }, 0.9f, 0.5f, 25f, false, Vector2.one);
        Sound("Play_TentacleNear", new[] { "tenacles" }, 0.8f, 1f, 14f, true, Vector2.one);
        Sound("Play_LMB", new[] { "LMB" }, 0.7f, 0f, 10f, false, Vector2.one);
        Sound("Play_PickUp", new[] { "pickUp_RMB" }, 0.8f, 0f, 10f, false, Vector2.one);
        Sound("Play_Drop", new[] { "drop_RMB" }, 0.8f, 0f, 10f, false, Vector2.one);
        Sound("Play_BubblePop", new[] { "bubble_pop" }, 0.9f, 0.8f, 30f, false, Vector2.one);

        if (!bank.creatureSounds.Exists(s => s.creatureType == "TentacleCreature"))
            bank.creatureSounds.Add(new CreatureSound { creatureType = "TentacleCreature", eventName = "Play_TentacleNear", radius = 14f });

        EditorUtility.SetDirty(bank);
        AssetDatabase.SaveAssets();
        return added;
    }

    private static AudioEventBank LoadOrCreateBank()
    {
        var bank = AssetDatabase.LoadAssetAtPath<AudioEventBank>(BankPath);
        if (bank != null) return bank;

        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Audio");
        if (!AssetDatabase.IsValidFolder(Folder + "/Resources")) AssetDatabase.CreateFolder(Folder, "Resources");
        bank = ScriptableObject.CreateInstance<AudioEventBank>();

        bank.events.Add(Music("PlayBGM", AudioEventKind.MusicBase, 0, 0.7f));
        bank.events.Add(Music("BGM_fuzzy", AudioEventKind.MusicLayer, 1, 1f));
        bank.events.Add(Music("BGM_cave", AudioEventKind.MusicLayer, 5, 1f));
        bank.events.Add(Music("BGM_fluid", AudioEventKind.MusicLayer, 3, 1f));
        bank.events.Add(Music("BGM_landmark", AudioEventKind.MusicLayer, 6, 0.5f));
        bank.events.Add(new AudioEventDef
        {
            name = "SFX_test",
            kind = AudioEventKind.Sound,
            placeholderVoice = 100,
            pitchRange = new Vector2(0.9f, 1.1f)
        });

        bank.creatureChannels.Add(CreatureMap("FuzzCreature", "BGM_fuzzy"));
        bank.creatureChannels.Add(CreatureMap("FireEater", "BGM_fluid"));

        AssetDatabase.CreateAsset(bank, BankPath);
        AssetDatabase.SaveAssets();
        return bank;
    }

    // Fills empty events with clips under Assets/Audio named like the event (e.g. BGM_deco1.wav, BGM_deco1_b.wav).
    private static int LinkClips(AudioEventBank bank)
    {
        if (!AssetDatabase.IsValidFolder(Folder)) return 0;
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { Folder });
        int linked = 0;
        foreach (AudioEventDef def in bank.events)
        {
            if (def.clips != null && def.clips.Length > 0 && def.clips[0] != null) continue;
            var found = new System.Collections.Generic.List<AudioClip>();
            foreach (string g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                string file = Path.GetFileNameWithoutExtension(path);
                if (file == def.name || file.StartsWith(def.name + "_"))
                    found.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(path));
            }
            if (found.Count == 0) continue;
            def.clips = found.ToArray();
            linked += found.Count;
        }
        if (linked > 0)
        {
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
        }
        return linked;
    }

    private static AudioEventDef Music(string name, AudioEventKind kind, int voice, float volume) => new AudioEventDef
    {
        name = name,
        kind = kind,
        placeholderVoice = voice,
        volume = volume,
        loop = true,
        spatialBlend = 0f
    };

    private static CreatureChannel CreatureMap(string type, string layer) => new CreatureChannel
    {
        creatureType = type,
        layerEvent = layer,
        innerRadius = 2f,
        outerRadius = 10f
    };
}
