using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Authors the tutorial once as ordinary saved objects, not runtime-generated terrain.</summary>
public static class YoTutorialSetup
{
    private const string ScenePath = "Assets/Scenes/Scene_YoTutorial.unity";
    private const string Generated = "Assets/Generated/YoTutorial";
    private const string TriggerPath = "Assets/Prefabs/Gameplay/InteractionHintsTrigger.prefab";
    private static Material stone, ground, darkStone, towerStone;
    private static Mesh rock;

    [MenuItem("Tools/Capstone/Yo Tutorial/Build Opening Layout")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before building the tutorial.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new InvalidOperationException("Open Scene_YoTutorial before building its layout.");
        foreach (var existing in scene.GetRootGameObjects())
            if (existing.name == "Yo Tutorial") throw new InvalidOperationException("The tutorial is already built. Edit its saved objects in the hierarchy.");

        EnsureGroundTag();
        EnsureFolder(Generated);
        stone = ReadableMaterial("Assets/Materials/World/MAT_Decor_Stone.mat", "CaveStone", new Color(0.52f, 0.55f, 0.58f));
        ground = ReadableMaterial("Assets/Materials/World/MAT_Decor_Organic.mat", "Ground", new Color(0.65f, 0.67f, 0.6f));
        darkStone = ReadableMaterial("Assets/Materials/World/MAT_Realm_Basalt.mat", "DipStone", new Color(0.35f, 0.38f, 0.4f));
        towerStone = ReadableMaterial("Assets/Materials/Landmarks/MAT_Landmark_SpireStone.mat", "TowerStone", new Color(0.78f, 0.8f, 0.76f));
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerObjects.prefab");
        var shroomPrefab = AssetDatabase.LoadAssetAtPath<BounceShroom>("Assets/Prefabs/Creatures/BounceShroom.prefab");
        if (stone == null || ground == null || darkStone == null || towerStone == null || playerPrefab == null || shroomPrefab == null)
            throw new InvalidOperationException("The tutorial needs the existing world materials, player and BounceShroom prefabs.");

        EnsureFolder(Generated);
        EnsureFolder("Assets/Prefabs/Gameplay");
        rock = RockMesh();
        var root = new GameObject("Yo Tutorial").transform;
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Build Yo Tutorial");
        Transform cave = Group(root, "01 Cave — spawn, practice, bounce exit");
        BuildCave(cave);
        BuildBasin(Group(root, "02 Basin — three future encounters"));

        var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
        // PlayerObjects must stay a scene root: carry collision filtering uses transform.root.
        player.transform.position = new Vector3(0f, 1.15f, 0f);
        player.transform.rotation = Quaternion.identity;
        player.name = "PlayerObjects";
        foreach (GameObject existing in scene.GetRootGameObjects())
            if (existing.name == "Main Camera")
            {
                // Keep the initial scene camera available for editing, without two active listeners.
                existing.name = "Original Scene Camera (inactive)";
                existing.SetActive(false);
            }

        var hud = Group(root, "Interaction Hints — cave on, exit off").gameObject.AddComponent<InteractionPromptHUD>();
        var hudFields = new SerializedObject(hud);
        hudFields.FindProperty("showText").boolValue = true;
        hudFields.FindProperty("hintsVisible").boolValue = true;
        hudFields.FindProperty("carrier").objectReferenceValue = player.GetComponentInChildren<PlayerCreatureCarrier>(true);
        hudFields.FindProperty("interactor").objectReferenceValue = player.GetComponentInChildren<PlayerCreatureInteractor>(true);
        hudFields.ApplyModifiedPropertiesWithoutUndo();

        var shroomGroup = Group(cave, "Practice BounceShrooms");
        for (int i = 0; i < 3; i++)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(shroomPrefab.gameObject, scene);
            instance.transform.SetParent(shroomGroup, false);
            instance.transform.position = new Vector3((i - 1) * 3f, 0.08f, 23f + (i == 1 ? 1f : 0f));
            instance.name = "Practice BounceShroom " + (i + 1);
        }

        var triggerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TriggerPath);
        if (triggerPrefab == null)
        {
            var template = new GameObject("InteractionHintsTrigger");
            template.AddComponent<BoxCollider>().isTrigger = true;
            template.AddComponent<InteractionHintsTrigger>();
            triggerPrefab = PrefabUtility.SaveAsPrefabAsset(template, TriggerPath);
            Object.DestroyImmediate(template);
        }
        var exit = (GameObject)PrefabUtility.InstantiatePrefab(triggerPrefab, scene);
        exit.transform.SetParent(root, false);
        exit.name = "Cave Exit — hide interaction hints";
        exit.transform.position = new Vector3(0f, 8f, 55f);
        exit.GetComponent<BoxCollider>().size = new Vector3(12f, 12f, 3f);
        var fields = new SerializedObject(exit.GetComponent<InteractionHintsTrigger>());
        fields.FindProperty("hud").objectReferenceValue = hud;
        fields.ApplyModifiedPropertiesWithoutUndo();

        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (light.gameObject.scene == scene && light.type == LightType.Directional)
            {
                light.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
                light.intensity = 1.3f;
                light.color = new Color(1f, 0.95f, 0.85f);
            }
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.46f, 0.49f, 0.55f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.62f, 0.68f, 0.71f);
        RenderSettings.fogStartDistance = 100f;
        RenderSettings.fogEndDistance = 300f;
        var view = SceneView.lastActiveSceneView;
        if (view != null) view.LookAt(new Vector3(6f, 3f, 91f), Quaternion.Euler(50f, -25f, 0f), 110f);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Yo Tutorial saved: cave lesson, three outdoor encounter clearings, tower and reusable hint trigger.");
    }

    private static void BuildCave(Transform parent)
    {
        var spawn = Group(parent, "Spawn Alcove");
        Box(spawn, "Alcove Floor", new Vector3(0f, -1f, 1f), new Vector3(10f, 2f, 12f), ground);
        Box(spawn, "Back Rock", new Vector3(0f, 3f, -5f), new Vector3(12f, 8f, 3f), stone);
        Box(spawn, "Left Rock", new Vector3(-5f, 3f, 1f), new Vector3(3f, 8f, 12f), stone);
        Box(spawn, "Right Rock", new Vector3(5f, 3f, 1f), new Vector3(3f, 8f, 12f), stone);
        Box(spawn, "Alcove Ceiling", new Vector3(0f, 6.5f, 1f), new Vector3(12f, 3f, 14f), stone);
        Box(spawn, "Door Left", new Vector3(-4f, 3f, 7f), new Vector3(4f, 8f, 2f), stone);
        Box(spawn, "Door Right", new Vector3(4f, 3f, 7f), new Vector3(4f, 8f, 2f), stone);
        var corridor = Group(parent, "Short Arrival Corridor");
        Box(corridor, "Corridor Floor", new Vector3(0f, -1f, 12f), new Vector3(6f, 2f, 12f), ground);
        Box(corridor, "Left Wall", new Vector3(-3.5f, 3f, 12f), new Vector3(3f, 8f, 12f), stone);
        Box(corridor, "Right Wall", new Vector3(3.5f, 3f, 12f), new Vector3(3f, 8f, 12f), stone);
        Box(corridor, "Low Rock Ceiling", new Vector3(0f, 5.5f, 12f), new Vector3(9f, 3f, 13f), stone);

        var chamber = Group(parent, "BounceShroom Chamber");
        Box(chamber, "Practice Floor", new Vector3(0f, -1f, 22.5f), new Vector3(26f, 2f, 11f), ground);
        // Ramp top joins the practice floor at z=28 and dip floor at z=33.
        Ramp(chamber, "Walk Down Into Dip", 26f, 28f, 33f, 0f, -2f);
        Box(chamber, "Safe Dip Floor", new Vector3(0f, -3f, 40.5f), new Vector3(26f, 2f, 15f), darkStone);
        Box(chamber, "Left Cave Shell", new Vector3(-14.5f, 4f, 32.5f), new Vector3(3f, 18f, 33f), stone);
        Box(chamber, "Right Cave Shell", new Vector3(14.5f, 4f, 32.5f), new Vector3(3f, 18f, 33f), stone);
        Box(chamber, "Entry Back Left", new Vector3(-8f, 4f, 17f), new Vector3(12f, 18f, 3f), stone);
        Box(chamber, "Entry Back Right", new Vector3(8f, 4f, 17f), new Vector3(12f, 18f, 3f), stone);
        Box(chamber, "Above Entry", new Vector3(0f, 9f, 17f), new Vector3(5f, 8f, 3f), stone);
        Box(chamber, "Chamber Ceiling", new Vector3(0f, 12.5f, 33f), new Vector3(32f, 3f, 34f), stone);
        Box(chamber, "Exit Cliff — 6m above dip", new Vector3(0f, 0f, 49f), new Vector3(30f, 8f, 2f), stone);
        Box(chamber, "Exit Left Rock", new Vector3(-9.5f, 7.5f, 49f), new Vector3(11f, 15f, 3f), stone);
        Box(chamber, "Exit Right Rock", new Vector3(9.5f, 7.5f, 49f), new Vector3(11f, 15f, 3f), stone);
        Box(chamber, "Exit Arch Top", new Vector3(0f, 12f, 49f), new Vector3(10f, 5f, 3f), stone);
        Box(chamber, "Exit Landing", new Vector3(0f, 2f, 55f), new Vector3(12f, 4f, 12f), ground);
        Boulder(chamber, "Left Exit Buttress", new Vector3(-9f, 7f, 54f), new Vector3(7f, 18f, 13f), 18f);
        Boulder(chamber, "Right Exit Buttress", new Vector3(9f, 7f, 54f), new Vector3(7f, 18f, 13f), -12f);

        for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 7; i++)
                Boulder(chamber, "Rough Cave Wall " + side + " " + i,
                    new Vector3(side * 14.7f, 5.5f, 18f + i * 4.7f), new Vector3(4f, 13f + i % 3, 7f), i * 19f);
        for (int i = 0; i < 5; i++)
            Boulder(corridor, "Tunnel Rock " + i, new Vector3(i % 2 == 0 ? -3.8f : 3.8f, 2.5f, 8f + i * 2f), new Vector3(3f, 6f, 4f), i * 32f);
        Lamp(parent, "Spawn Soft Light", new Vector3(0f, 3.5f, 3f), 11f, 4f);
        Lamp(parent, "Practice Soft Light", new Vector3(0f, 6f, 24f), 22f, 8f);
        Lamp(parent, "Exit Reflected Light", new Vector3(0f, 6.5f, 44f), 18f, 7f);
    }

    private static void BuildBasin(Transform parent)
    {
        Box(parent, "Basin Ground", new Vector3(0f, 1f, 134f), new Vector3(140f, 6f, 156f), ground);
        var entry = Group(parent, "Basin Arrival — tower first glimpse");
        Clearing(entry, "Arrival", new Vector3(0f, 4f, 68f), 17f);
        var clearings = new[] { new Vector3(27f, 4f, 90f), new Vector3(43f, 4f, 125f), new Vector3(21f, 4f, 162f) };
        var previous = new Vector3(0f, 4f, 63f);
        for (int i = 0; i < clearings.Length; i++)
        {
            var area = Group(parent, "Encounter " + (i + 1) + " — future creature clearing");
            Clearing(area, "Clearing Floor", clearings[i], 18f + i);
            var anchor = Group(area, "Creature Placement Anchor");
            anchor.position = clearings[i] + Vector3.up * 0.3f;
            Path(parent, previous, clearings[i]);
            previous = clearings[i];
        }
        var towerArea = Group(parent, "Tower Clearing — future combined encounter");
        Clearing(towerArea, "Tower Clearing Floor", new Vector3(0f, 4f, 194f), 26f);
        Path(parent, previous, new Vector3(0f, 4f, 192f));
        var tower = MeshObject(towerArea, "Distant Tower Blockout", TowerMesh(), towerStone);
        tower.transform.position = new Vector3(0f, 4.2f, 198f);
        Group(towerArea, "Combined Creature Encounter Anchor").position = new Vector3(0f, 4.3f, 185f);
        var hills = Group(parent, "Terrain Dividers and Basin Rim");
        Boulder(hills, "Arrival Hill — hides direct route to tower", new Vector3(-8f, 7f, 105f), new Vector3(43f, 24f, 48f), 12f);
        Boulder(hills, "First Clearing Divider", new Vector3(13f, 7f, 111f), new Vector3(19f, 14f, 14f), -20f);
        Boulder(hills, "Second Clearing Divider", new Vector3(12f, 7f, 144f), new Vector3(23f, 15f, 20f), 18f);
        Boulder(hills, "Outer Clearing Divider", new Vector3(59f, 8f, 146f), new Vector3(20f, 16f, 27f), -15f);
        for (int i = 0; i < 15; i++)
        {
            float angle = (i / 14f * 290f - 145f) * Mathf.Deg2Rad;
            var at = new Vector3(Mathf.Sin(angle) * 76f, 10f, 134f + Mathf.Cos(angle) * 87f);
            Boulder(hills, "Basin Rim " + (i + 1), at, new Vector3(28f + i % 3 * 4f, 32f + i % 4 * 3f, 33f), i * 31f);
        }
        // Solid borders catch bodies behind the decorative rim, rather than leaving open world edges.
        Box(hills, "West Rim Core", new Vector3(-72f, 11f, 134f), new Vector3(8f, 26f, 170f), stone);
        Box(hills, "East Rim Core", new Vector3(72f, 11f, 134f), new Vector3(8f, 26f, 170f), stone);
        Box(hills, "North Rim Core", new Vector3(0f, 11f, 216f), new Vector3(150f, 26f, 8f), stone);
        Box(hills, "South Left Rim", new Vector3(-40f, 11f, 56f), new Vector3(64f, 26f, 8f), stone);
        Box(hills, "South Right Rim", new Vector3(40f, 11f, 56f), new Vector3(64f, 26f, 8f), stone);
    }

    private static void Clearing(Transform parent, string name, Vector3 centre, float radius)
    {
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = name;
        disc.transform.SetParent(parent, false);
        disc.transform.position = centre;
        disc.transform.localScale = new Vector3(radius * 2f, 0.1f, radius * 2f);
        disc.layer = LayerMask.NameToLayer("Ground");
        disc.tag = "Ground";
        disc.GetComponent<Renderer>().sharedMaterial = ground;
        for (int i = 0; i < 5; i++)
        {
            float a = (160f + i * 38f) * Mathf.Deg2Rad;
            Boulder(parent, "Clearing Edge Stone " + i, centre + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius), new Vector3(2.4f, 2f, 3.4f), i * 27f);
        }
    }

    private static void Path(Transform parent, Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        var path = Box(parent, "Walkable Connection", (from + to) * 0.5f + Vector3.down * 0.1f, new Vector3(7f, 0.3f, delta.magnitude + 1f), ground);
        path.transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
    }

    private static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = size;
        go.layer = LayerMask.NameToLayer("Ground");
        go.tag = "Ground";
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    private static void Boulder(Transform parent, string name, Vector3 position, Vector3 size, float yaw)
    {
        var go = MeshObject(parent, name, rock, stone);
        go.transform.position = position;
        go.transform.localScale = size;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    private static GameObject MeshObject(Transform parent, string name, Mesh mesh, Material material)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
        go.transform.SetParent(parent, false);
        go.layer = LayerMask.NameToLayer("Ground");
        go.tag = "Ground";
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        go.GetComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    private static void Ramp(Transform parent, string name, float width, float start, float end, float high, float low)
    {
        var vertices = new[] { new Vector3(-width/2,high,start), new Vector3(width/2,high,start), new Vector3(-width/2,low,end), new Vector3(width/2,low,end), new Vector3(-width/2,-4f,start), new Vector3(width/2,-4f,start), new Vector3(-width/2,-4f,end), new Vector3(width/2,-4f,end) };
        int[] triangles = { 0,2,1, 1,2,3, 4,5,6, 5,7,6, 0,1,4, 1,5,4, 2,6,3, 3,6,7, 0,4,2, 2,4,6, 1,3,5, 3,7,5 };
        MeshObject(parent, name, SaveMesh("CaveDipRamp", vertices, triangles), ground);
    }

    private static Mesh RockMesh()
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        const int sides = 11, rings = 6;
        for (int y = 0; y <= rings; y++)
        {
            float latitude = Mathf.PI * y / rings;
            for (int x = 0; x < sides; x++)
            {
                float angle = x * Mathf.PI * 2f / sides;
                float noise = 1f + 0.09f * Mathf.Sin(x * 7.31f + y * 5.19f);
                vertices.Add(new Vector3(Mathf.Cos(angle) * Mathf.Sin(latitude), Mathf.Cos(latitude), Mathf.Sin(angle) * Mathf.Sin(latitude)) * (0.5f * noise));
            }
        }
        for (int y = 0; y < rings; y++)
            for (int x = 0; x < sides; x++)
            {
                int a = y * sides + x, b = y * sides + (x + 1) % sides, c = a + sides, d = b + sides;
                triangles.AddRange(new[] { a, b, c, b, d, c });
            }
        return SaveMesh("FacetedRock", vertices.ToArray(), triangles.ToArray());
    }

    private static Mesh TowerMesh()
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        const int sides = 9;
        for (int ring = 0; ring < 5; ring++)
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                float radius = (6f - ring * 0.55f) * (1f + 0.1f * Mathf.Sin(i * 3.7f + ring));
                vertices.Add(new Vector3(Mathf.Cos(angle) * radius, ring * 12f, Mathf.Sin(angle) * radius));
            }
        for (int ring = 0; ring < 4; ring++)
            for (int i = 0; i < sides; i++)
            {
                int a = ring * sides + i, b = ring * sides + (i + 1) % sides;
                triangles.AddRange(new[] { a, a + sides, b, b, a + sides, b + sides });
            }
        vertices.Add(new Vector3(0f, 48f, 0f));
        for (int i = 0; i < sides; i++) triangles.AddRange(new[] { 45, 36 + (i + 1) % sides, 36 + i });
        return SaveMesh("TowerBlockout", vertices.ToArray(), triangles.ToArray());
    }

    private static Mesh SaveMesh(string name, Vector3[] vertices, int[] triangles)
    {
        string path = Generated + "/" + name + ".asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;
        // Split triangle corners for the project's faceted rock look.
        var flat = new Vector3[triangles.Length];
        var indices = new int[triangles.Length];
        for (int i = 0; i < triangles.Length; i++) { flat[i] = vertices[triangles[i]]; indices[i] = i; }
        mesh = new Mesh { name = name, vertices = flat, triangles = indices };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    private static void Lamp(Transform parent, string name, Vector3 position, float range, float intensity)
    {
        var light = Group(parent, name).gameObject.AddComponent<Light>();
        light.transform.position = position;
        light.type = LightType.Point;
        light.color = new Color(0.8f, 0.9f, 1f);
        light.range = range;
        light.intensity = intensity;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    private static void EnsureGroundTag()
    {
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var tags = settings.FindProperty("tags");
        for (int i = 0; i < tags.arraySize; i++)
            if (tags.GetArrayElementAtIndex(i).stringValue == "Ground") return;
        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = "Ground";
        settings.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
    }

    private static Material ReadableMaterial(string source, string name, Color color)
    {
        string path = Generated + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        var original = AssetDatabase.LoadAssetAtPath<Material>(source);
        if (original == null) return null;
        material = new Material(original) { name = name };
        foreach (string property in new[] { "_DotChance", "_GridChance", "_GlitchAmount", "_RingCount", "_SwayAmount", "_BreathAmount", "_StrataFrequency" })
            if (material.HasProperty(property)) material.SetFloat(property, 0f);
        if (material.HasProperty("_HatchStrength")) material.SetFloat("_HatchStrength", 0.15f);
        if (material.HasProperty("_HatchScale")) material.SetFloat("_HatchScale", 3f);
        if (material.HasProperty("_LineWidth")) material.SetFloat("_LineWidth", 0.6f);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_ShadowColor")) material.SetColor("_ShadowColor", color * 0.6f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
