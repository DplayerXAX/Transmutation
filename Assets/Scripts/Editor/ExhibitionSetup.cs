using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// Exhibition: copies Scene_Daniel to Scene_Exhibition, keeps the player, UI, meditation and audio, swaps the
/// generated world for a flat stone floor, and lays out a ring of stalls, one per creature or feature, each with
/// a sign. Creatures are spawned by ExhibitionStall in Play mode and grow back when lost.
/// Scene_Daniel itself is not changed.
/// </summary>
public static class ExhibitionSetup
{
    private const string SourceScene = "Assets/Scenes/Scene_Daniel.unity";
    private const string TargetScene = "Assets/Scenes/Scene_Exhibition.unity";
    private const string Creatures = "Assets/Prefabs/Creatures/";
    private const int GroundLayer = 7;
    private const float RingRadius = 17f;

    // Root objects kept from Scene_Daniel (anything else is removed).
    private static readonly string[] KeepNames =
    {
        "PlayerObjects", "Canvas", "EventSystem", "Directional Light", "Meditation", "AudioManager",
        "WwiseGlobal", "InteractionPromptHUD",
    };

    private class Exhibit
    {
        public string title;
        public string text;
        public Color accent;
        public string[] prefabs = new string[0];  // under Assets/Prefabs/Creatures
        public GameObject sceneTemplate;           // or a creature taken from Scene_Daniel
        public System.Action<Transform, Material> extra;
    }

    [MenuItem("Tools/Capstone/Exhibition/Create Exhibition Scene (Copy Of Scene_Daniel)")]
    private static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (File.Exists(TargetScene))
        {
            if (!EditorUtility.DisplayDialog("Exhibition", "Scene_Exhibition already exists. Replace it with a fresh copy of Scene_Daniel?", "Replace", "Cancel"))
                return;
            AssetDatabase.DeleteAsset(TargetScene);
        }
        if (!AssetDatabase.CopyAsset(SourceScene, TargetScene))
        {
            EditorUtility.DisplayDialog("Exhibition", "Could not copy " + SourceScene + ".", "OK");
            return;
        }
        Scene scene = EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);
        Configure(scene);
        EditorSceneManager.SaveScene(scene);
        AddToBuildSettings();
        Debug.Log("Scene_Exhibition created and saved, and added to Build Settings. Press Play to walk round the stalls.");
    }

    private static void Configure(Scene scene)
    {
        Material stone = Load<Material>("Assets/Materials/Landmarks/MAT_Landmark_SpireStone.mat") ?? Load<Material>("Assets/Materials/World/MAT_Decor_Stone.mat");
        Material floor = Load<Material>("Assets/Materials/World/MAT_World_Surface.mat") ?? stone;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // The tentacle and a fuzz from Scene_Daniel become stall templates.
        TentacleCreature tentacle = Object.FindFirstObjectByType<TentacleCreature>();
        FuzzCreature fuzz = Object.FindFirstObjectByType<FuzzCreature>();
        var holder = new GameObject("Exhibition Templates");
        if (tentacle != null) tentacle.transform.SetParent(holder.transform, true);
        if (fuzz != null) fuzz.transform.SetParent(holder.transform, true);

        int removed = 0;
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            if (rootObject == holder || Keep(rootObject)) continue;
            Object.DestroyImmediate(rootObject);
            removed++;
        }

        var hall = new GameObject("Exhibition").transform;

        // Flat floor on the Ground layer (walkable and climbable like the generated world).
        GameObject ground = Block("Floor", hall, new Vector3(0f, -0.5f, 0f), new Vector3(90f, 1f, 90f), floor);
        ground.layer = GroundLayer;

        List<Exhibit> exhibits = Exhibits(tentacle, fuzz);
        for (int i = 0; i < exhibits.Count; i++)
        {
            float a = i / (float)exhibits.Count * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            MakeStall(hall, exhibits[i], dir * RingRadius, dir, stone, font);
        }

        // Centre sign: how to get around.
        Sign(hall, "Welcome", "EXHIBITION",
            "Every creature and feature has a stall round the ring.\nRight click carry    Left click use    M meditate",
            new Vector3(0f, 0f, 4f), Vector3.forward, new Color(0.95f, 0.95f, 0.95f), stone, font);

        Object.DestroyImmediate(holder);

        // Player in the middle, facing the first stall.
        SmoothFirstPersonController player = Object.FindFirstObjectByType<SmoothFirstPersonController>();
        if (player != null)
        {
            Vector3 offset = new Vector3(0f, 1.2f, 0f) - player.transform.position;
            player.transform.root.position += offset;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = hall.gameObject;
        Debug.Log($"Exhibition set up: removed {removed} root objects, {exhibits.Count} stalls.");
    }

    private static List<Exhibit> Exhibits(TentacleCreature tentacle, FuzzCreature fuzz)
    {
        var list = new List<Exhibit>
        {
            new Exhibit { title = "GOO", accent = new Color(0.3f, 1f, 0.35f), prefabs = new[] { "GooCreature.prefab" },
                text = "Coat your hands with it to climb smooth stone.\nShift climbs faster but uses goo faster.",
                extra = SmoothTower },
            new Exhibit { title = "CLIMBING", accent = new Color(0.9f, 0.9f, 0.9f),
                text = "Bare hands grip rough stone.\nWalk into the wall and hold on; Shift to climb fast.",
                extra = RoughWall },
            new Exhibit { title = "TENTACLE", accent = new Color(1f, 0.3f, 0.55f), sceneTemplate = tentacle != null ? tentacle.gameObject : null,
                text = "Its arms stick to nearby surfaces.\nCarry it: Space climbs up, Ctrl climbs down." },
            new Exhibit { title = "FUZZ", accent = new Color(1f, 0.75f, 0.2f), sceneTemplate = fuzz != null ? fuzz.gameObject : null,
                text = "A sticky furry blob.\nGet close and it follows you in a chain." },
            new Exhibit { title = "BUBBLE FLOWER", accent = new Color(0.2f, 0.8f, 1f), prefabs = new[] { "BubbleFlower.prefab" },
                text = "Blows trapping bubbles.\nCarry it and left click to shoot one." },
            new Exhibit { title = "FIRE", accent = new Color(1f, 0.35f, 0.1f), prefabs = new[] { "FireSource.prefab", "Fire Flower.prefab" },
                text = "The fire source heats what is near it.\nA heated flower blooms into fire bubbles." },
            new Exhibit { title = "SKY SPIDER", accent = new Color(0.75f, 0.6f, 1f), prefabs = new[] { "SkySpider.prefab" },
                text = "Spins webs that catch creatures\nand drains their age." },
            new Exhibit { title = "COMPOST", accent = new Color(0.55f, 0.85f, 0.3f), prefabs = new[] { "CompostCreature.prefab" },
                text = "Takes in dead matter and remains.\nYou can ride on it." },
            new Exhibit { title = "SQUIRREL", accent = new Color(0.95f, 0.55f, 0.25f), prefabs = new[] { "Squirrel.prefab" },
                text = "Gathers food and brings it\nback to its nest.", extra = Nest },
            new Exhibit { title = "BOUNCE SHROOM", accent = new Color(1f, 0.3f, 0.9f), prefabs = new[] { "BounceShroom.prefab" },
                text = "Swells into a springy platform.\nJump on it to bounce high." },
            new Exhibit { title = "CORRUPT SEED", accent = new Color(0.6f, 1f, 0.2f), prefabs = new[] { "CorruptSeed.prefab" },
                text = "Plant food that infects whoever eats it.\nThe host dies into more seeds." },
            new Exhibit { title = "BIRDS", accent = new Color(0.3f, 0.6f, 1f), prefabs = new[] { "BirdCreature.prefab" },
                text = "Fly in flocks overhead and hunt food.\nOld birds fall as bones.", extra = Flock },
            new Exhibit { title = "ASURA", accent = new Color(1f, 0.15f, 0.2f), prefabs = new[] { "Asura/GeneralAsura.prefab" },
                text = "A fighting creature prototype.\nKeep your distance." },
        };
        return list;
    }

    private static void MakeStall(Transform hall, Exhibit exhibit, Vector3 position, Vector3 outward, Material stone, Font font)
    {
        var stall = new GameObject(exhibit.title + " Stall").transform;
        stall.SetParent(hall, false);
        stall.position = position;
        stall.rotation = Quaternion.LookRotation(-outward, Vector3.up);

        // A low stone disc marks the stall (visual only, the floor carries everything).
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = "Stall Disc";
        Object.DestroyImmediate(disc.GetComponent<Collider>());
        disc.transform.SetParent(stall, false);
        disc.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        disc.transform.localScale = new Vector3(6f, 0.02f, 6f);
        disc.GetComponent<Renderer>().sharedMaterial = stone;

        // Sign between the stall and the middle, readable from the middle.
        Sign(stall, "Sign", exhibit.title, exhibit.text, position - outward * 3.6f, outward, exhibit.accent, stone, font);

        // Creatures: one ExhibitionStall each, side by side.
        var templates = new List<GameObject>();
        foreach (string path in exhibit.prefabs)
        {
            var prefab = Load<GameObject>(Creatures + path);
            if (prefab == null)
            {
                Debug.LogWarning("Exhibition: missing prefab " + Creatures + path + "; stall " + exhibit.title + " shows its sign only.");
                continue;
            }
            templates.Add(prefab);
        }
        int count = templates.Count + (exhibit.sceneTemplate != null ? 1 : 0);
        int index = 0;
        foreach (GameObject prefab in templates)
            AddSpawner(stall, (GameObject)PrefabUtility.InstantiatePrefab(prefab), index++, count);
        if (exhibit.sceneTemplate != null)
            AddSpawner(stall, exhibit.sceneTemplate, index, count);

        exhibit.extra?.Invoke(stall, stone);
    }

    private static void AddSpawner(Transform stall, GameObject template, int index, int count)
    {
        float x = (index - (count - 1) * 0.5f) * 2.2f;
        var spot = new GameObject(template.name + " Spawn").transform;
        spot.SetParent(stall, false);
        spot.localPosition = new Vector3(x, 0f, 0f);
        template.transform.SetParent(spot, false);
        template.transform.localPosition = Vector3.zero;
        template.SetActive(false);
        var spawner = spot.gameObject.AddComponent<ExhibitionStall>();
        spawner.template = template;
    }

    // ---------------- Stall extras ----------------

    // A smooth tower behind the goo stall: bare hands slide off, goo hands hold.
    private static void SmoothTower(Transform stall, Material stone)
    {
        GameObject tower = Block("Smooth Tower", stall, new Vector3(0f, 7f, -4f), new Vector3(3f, 14f, 3f), stone);
        tower.layer = GroundLayer;
        tower.AddComponent<SlipperySurface>();
    }

    // A rough wall with a ledge on top: climbable with bare hands.
    private static void RoughWall(Transform stall, Material stone)
    {
        GameObject wall = Block("Rough Wall", stall, new Vector3(0f, 2.5f, -2f), new Vector3(6f, 5f, 1.5f), stone);
        wall.layer = GroundLayer;
    }

    private static void Nest(Transform stall, Material stone)
    {
        var prefab = Load<GameObject>(Creatures + "Nest.prefab");
        if (prefab == null) return;
        var nest = (GameObject)PrefabUtility.InstantiatePrefab(prefab, stall);
        nest.transform.localPosition = new Vector3(0f, 0f, -2.5f);
    }

    // A small flock circling above the whole hall.
    private static void Flock(Transform stall, Material stone)
    {
        var bird = Load<BirdCreature>(Creatures + "BirdCreature.prefab");
        if (bird == null) return;
        var birds = new GameObject("Exhibition Birds");
        birds.transform.SetParent(stall.parent, false);
        var manager = birds.AddComponent<BirdFlockManager>();
        var serialized = new SerializedObject(manager);
        serialized.FindProperty("skyBounds").boundsValue = new Bounds(new Vector3(0f, 12f, 0f), new Vector3(70f, 10f, 70f));
        serialized.ApplyModifiedProperties();
        var area = new GameObject("Bird Spawn Area");
        area.transform.SetParent(birds.transform, false);
        area.transform.position = new Vector3(0f, 12f, 0f);
        area.AddComponent<BirdSpawnArea>().Configure(bird, 10, 5f, 2031);
    }

    // ---------------- Helpers ----------------

    private static void Sign(Transform parent, string name, string title, string text, Vector3 position, Vector3 facing,
        Color accent, Material stone, Font font)
    {
        var sign = new GameObject(name).transform;
        sign.SetParent(parent, true);
        sign.position = position;
        sign.rotation = Quaternion.LookRotation(facing, Vector3.up);

        GameObject slab = Block("Slab", sign, new Vector3(0f, 0.55f, 0.12f), new Vector3(2.8f, 1.1f, 0.12f), stone);
        slab.layer = 0;

        TextMesh head = Label(sign, "Title", title, font, new Vector3(0f, 1.55f, 0f), 0.045f, accent);
        head.fontStyle = FontStyle.Bold;
        Label(sign, "Text", text, font, new Vector3(0f, 0.6f, 0.04f), 0.018f, new Color(0.95f, 0.95f, 0.95f));
    }

    private static TextMesh Label(Transform parent, string name, string text, Font font, Vector3 position, float size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        // Text faces the reader, who looks along the sign's forward.
        var mesh = go.AddComponent<TextMesh>();
        mesh.text = text;
        mesh.font = font;
        mesh.fontSize = 100;
        mesh.characterSize = size;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.color = color;
        go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        return mesh;
    }

    private static GameObject Block(string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    private static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);

    private static void AddToBuildSettings()
    {
        if (EditorBuildSettings.scenes.Any(s => s.path == TargetScene)) return;
        var list = EditorBuildSettings.scenes.ToList();
        list.Add(new EditorBuildSettingsScene(TargetScene, true));
        EditorBuildSettings.scenes = list.ToArray();
    }

    private static bool Keep(GameObject rootObject)
    {
        if (KeepNames.Contains(rootObject.name)) return true;
        if (rootObject.GetComponentInChildren<ProceduralWorld>(true) != null) return false;
        if (rootObject.GetComponentInChildren<SmoothFirstPersonController>(true) != null) return true;
        if (rootObject.GetComponentInChildren<Canvas>(true) != null || rootObject.GetComponentInChildren<EventSystem>(true) != null) return true;
        Light light = rootObject.GetComponent<Light>();
        return light != null && light.type == LightType.Directional;
    }
}
