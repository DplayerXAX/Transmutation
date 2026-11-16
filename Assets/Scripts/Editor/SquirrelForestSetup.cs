using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Standalone silk-tree habitat on the southern pine forest's sampled WorldMapTerrain surface.</summary>
[InitializeOnLoad]
public static class SquirrelForestSetup
{
    public const string ScenePath = "Assets/Scenes/Scene_SquirrelForest.unity";
    private const string Folder = "Assets/Generated/SquirrelForest";
    private const string Request = "Temp/SquirrelForest.build";
    private static readonly Vector2 SourceCentre = new Vector2(-120f, -110f);
    static SquirrelForestSetup() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { if (File.Exists(ScenePath)) Review(); else Build(); }
        catch (Exception e) { Directory.CreateDirectory("ArtCaptures/SquirrelForest"); File.WriteAllText("ArtCaptures/SquirrelForest/Build.txt", e.ToString()); Debug.LogException(e); }
    }

    private static void Review()
    {
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var externalColliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c => c.gameObject.scene != scene && c.enabled).ToArray();
        foreach (var collider in externalColliders) collider.enabled = false;
        try
        {
            var roots = scene.GetRootGameObjects();
            var forest = roots.SelectMany(g => g.GetComponentsInChildren<SilkForestManager>()).Single();
            var system = roots.SelectMany(g => g.GetComponentsInChildren<SquirrelSystemManager>()).Single();
            var groundMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/ForestGround.asset");
            var vertices = groundMesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i].y = Height(vertices[i].x, vertices[i].z);
            groundMesh.vertices = vertices; groundMesh.RecalculateNormals(); groundMesh.RecalculateBounds(); EditorUtility.SetDirty(groundMesh);
            var groundCollider = roots.SelectMany(g => g.GetComponentsInChildren<MeshCollider>()).First(c => c.gameObject.layer == 7);
            groundCollider.sharedMesh = null; groundCollider.sharedMesh = groundMesh;
            BakeForest(forest);
            var anchors = forest.GetComponentsInChildren<SquirrelTree>();
            var settings = new SerializedObject(system); var trees = settings.FindProperty("trees"); trees.arraySize = anchors.Length;
            for (int i = 0; i < anchors.Length; i++) trees.GetArrayElementAtIndex(i).objectReferenceValue = anchors[i];
            settings.ApplyModifiedPropertiesWithoutUndo();
            var player = roots.First(g => g.name == "PlayerObjects"); player.transform.position = new Vector3(0, Height(0, -34) + 2, -34);
            var reviewCamera = roots.SelectMany(g => g.GetComponentsInChildren<Camera>()).Single(c => c.name == "Forest review camera");
            reviewCamera.transform.position = new Vector3(35, Height(35, -45) + 20, -45); reviewCamera.transform.LookAt(new Vector3(0, Height(0, 0) + 5, 0));
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Physics.SyncTransforms(); system.InitializeSystem();
            var camera = roots.SelectMany(g => g.GetComponentsInChildren<Camera>()).Single(c => c.name == "Forest review camera");
            Capture(camera, scene);
            var treeBounds = new Bounds(); bool first = true;
            foreach (var r in forest.GetComponentsInChildren<Renderer>()) { if (first) { treeBounds = r.bounds; first = false; } else treeBounds.Encapsulate(r.bounds); }
            if (system.Squirrels.Count != 6 || system.Nests.Count != 6 || system.Foods.Count != 60) throw new InvalidOperationException("Unexpected ecosystem count.");
            foreach (var food in system.Foods)
                if (Mathf.Abs(Vector3.Dot(food.transform.up, Vector3.up)) > 0.4f) throw new InvalidOperationException("Upright food.");
            foreach (var nest in system.Nests)
                if (anchors.Min(t => Vector3.ProjectOnPlane(t.GroundPosition - nest.transform.position, Vector3.up).magnitude) > 2f) throw new InvalidOperationException("Nest outside root area.");
            typeof(SquirrelSystemManager).GetMethod("TickFoodGrowth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(system, new object[] { 15f });
            if (system.Foods.Count != 63) throw new InvalidOperationException("Food replenishment failed.");
            File.WriteAllText("ArtCaptures/SquirrelForest/Build.txt", $"PASS\nScene: {ScenePath}\nTerrain source: WorldMapTerrain.Height at {SourceCentre}; 128 x 128 m; 1 m samples.\nTrees: {forest.GeneratedTreeCount}; low-canopy anchors: {anchors.Length}.\nValidated 6 squirrels, 6 root-side nests, 60 sideways foods.\nTree bounds: {treeBounds}\n");
        }
        finally { foreach (var collider in externalColliders) if (collider != null) collider.enabled = true; SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true); }
    }

    [MenuItem("Tools/Capstone/Squirrel Forest/Create New Forest Scene")]
    public static void Build()
    {
        if (File.Exists(ScenePath)) throw new InvalidOperationException("Forest scene already exists; keep the authored scene intact.");
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var externalColliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c => c.gameObject.scene != scene && c.enabled).ToArray();
        foreach (var collider in externalColliders) collider.enabled = false;
        try
        {
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            Ground();
            Physics.SyncTransforms();
            var forest = new GameObject("Silk forest - low canopy squirrel habitat").AddComponent<SilkForestManager>();
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(SilkForestSetup.PrefabPath).GetComponent<SilkForestManager>();
            EditorUtility.CopySerialized(template, forest);
            // Clear the copied generated-root reference before generating independent scene meshes.
            var settings = new SerializedObject(forest);
            settings.FindProperty("generatedRoot").objectReferenceValue = null;
            settings.ApplyModifiedPropertiesWithoutUndo();
            forest.forestSize = new Vector2(96, 96); forest.maximumTrees = 90;
            forest.minimumSpacing = 5.5f; forest.heightRange = new Vector2(9, 16);
            forest.overallHeightRange = new Vector2(2f, 6f);
            forest.projectToGround = true; forest.groundLayers = 1 << 7;
            forest.squirrelHabitatAnchors = true; forest.generateOnStart = false;
            forest.Regenerate(); BakeForest(forest);
            var anchors = forest.GetComponentsInChildren<SquirrelTree>();
            if (anchors.Length < 6) throw new InvalidOperationException("Insufficient low-canopy trees.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Systems/Squirrels/SquirrelSystemManager.prefab");
            var system = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, scene)).GetComponent<SquirrelSystemManager>();
            system.name = "Squirrel ecosystem - low canopy homes and fallen acorns";
            var so = new SerializedObject(system);
            so.FindProperty("autoDiscoverScene").boolValue = false;
            so.FindProperty("population").intValue = 6;
            so.FindProperty("initialFoodCount").intValue = 60;
            so.FindProperty("maxUnstoredFood").intValue = 84;
            so.FindProperty("foodSpawnRadius").floatValue = 8;
            so.FindProperty("minimumTreeDistance").floatValue = 2;
            so.FindProperty("scatterFoodOnSide").boolValue = true;
            var trees = so.FindProperty("trees"); trees.arraySize = anchors.Length;
            for (int i = 0; i < anchors.Length; i++) trees.GetArrayElementAtIndex(i).objectReferenceValue = anchors[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/PlayerObjects.prefab"), scene);
            player.transform.position = new Vector3(0, Height(0, -34) + 2, -34);
            // The existing player prefab retains its carry, throw, climb and creature interaction components.
            var light = new GameObject("Warm forest daylight").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.6f; light.color = new Color(1, 0.91f, 0.76f);
            light.transform.rotation = Quaternion.Euler(42, -35, 0); light.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.135f, 0.162f, 0.177f);
            RenderSettings.ambientEquatorColor = new Color(0.075f, 0.096f, 0.084f);
            RenderSettings.ambientGroundColor = new Color(0.039f, 0.051f, 0.036f);
            RenderSettings.fog = true; RenderSettings.fogColor = new Color(0.3f, 0.4f, 0.42f);
            RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.006f;
            var camera = new GameObject("Forest review camera").AddComponent<Camera>(); camera.enabled = false;
            camera.transform.position = new Vector3(35, Height(35, -45) + 20, -45);
            camera.transform.LookAt(new Vector3(0, Height(0, 0) + 5, 0)); camera.farClipPlane = 300;
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
            // Verify the actual scene's complete ecosystem without persisting transient spawned members.
            var randomState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(4107); Physics.SyncTransforms(); system.InitializeSystem();
            if (!system.IsInitialized || system.Nests.Count != 6 || system.Squirrels.Count != 6 || system.Foods.Count != 60)
                throw new InvalidOperationException($"Ecology counts: {system.Squirrels.Count} squirrels, {system.Nests.Count} nests, {system.Foods.Count} food.");
            foreach (var home in system.Nests)
            {
                float distance = anchors.Min(t => Vector2.Distance(new Vector2(home.transform.position.x, home.transform.position.z), new Vector2(t.GroundPosition.x, t.GroundPosition.z)));
                if (distance > 2f) throw new InvalidOperationException("Nest is not beside a low-canopy root.");
            }
            foreach (var food in system.Foods)
                if (Mathf.Abs(Vector3.Dot(food.transform.up, Vector3.up)) > 0.4f) throw new InvalidOperationException("Food spawned upright.");
            Directory.CreateDirectory("ArtCaptures/SquirrelForest");
            Capture(camera, scene);
            File.WriteAllText("ArtCaptures/SquirrelForest/Build.txt", $"PASS\nScene: {ScenePath}\nTerrain: WorldMapTerrain.Height, source centre {SourceCentre}, 128 x 128 m, 1 m sampling.\nTrees: {forest.GeneratedTreeCount}; eligible low-canopy trees: {anchors.Length}.\nValidated 6 squirrels, 6 root-side nests, 60 sideways ground foods.\nFull manager retains food replenishment, territories, fights, nest ownership and collection.\n");
            UnityEngine.Random.state = randomState;
            EditorSceneManager.CloseScene(scene, true);
            Debug.Log("Squirrel forest created and validated: " + ScenePath);
        }
        finally
        {
            foreach (var collider in externalColliders) if (collider != null) collider.enabled = true;
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static float Height(float x, float z) => WorldMapTerrain.Height(x + SourceCentre.x, z + SourceCentre.y) - WorldMapTerrain.Height(SourceCentre.x, SourceCentre.y);
    private static void BakeForest(SilkForestManager forest)
    {
        string path = Folder + "/ForestTrees.asset";
        var library = AssetDatabase.LoadAssetAtPath<SquirrelForestMeshLibrary>(path);
        if (library != null) { SaveMeshLibrary(path); return; }
        library = ScriptableObject.CreateInstance<SquirrelForestMeshLibrary>(); AssetDatabase.CreateAsset(library, path);
        foreach (var filter in forest.GeneratedRoot.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = filter.sharedMesh;
            if (mesh == null) continue;
            if (EditorUtility.IsPersistent(mesh)) mesh = Object.Instantiate(mesh);
            AssetDatabase.AddObjectToAsset(mesh, library); filter.sharedMesh = mesh;
        }
        EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library); SaveMeshLibrary(path);
    }
    private static void SaveMeshLibrary(string path)
    {
        var mode = EditorSettings.serializationMode;
        try { EditorSettings.serializationMode = SerializationMode.ForceBinary; AssetDatabase.ForceReserializeAssets(new[] { path }); }
        finally { EditorSettings.serializationMode = mode; }
    }
    private static void Ground()
    {
        const int cells = 128, row = cells + 1;
        var vertices = new Vector3[row * row]; var uv = new Vector2[vertices.Length]; var indices = new int[cells * cells * 6];
        for (int z = 0; z <= cells; z++) for (int x = 0; x <= cells; x++)
        { int i = z * row + x; vertices[i] = new Vector3(x - 64, Height(x - 64, z - 64), z - 64); uv[i] = new Vector2(x / 12f, z / 12f); }
        int at = 0;
        for (int z = 0; z < cells; z++) for (int x = 0; x < cells; x++)
        { int i = z * row + x; indices[at++] = i; indices[at++] = i + row; indices[at++] = i + 1; indices[at++] = i + 1; indices[at++] = i + row; indices[at++] = i + row + 1; }
        var mesh = new Mesh { name = "World pine forest surface", vertices = vertices, uv = uv, triangles = indices };
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, Folder + "/ForestGround.asset");
        var ground = new GameObject("Ground - Scene_World southern pine forest contours"); ground.layer = 7;
        ground.AddComponent<MeshFilter>().sharedMesh = mesh; ground.AddComponent<MeshCollider>().sharedMesh = mesh;
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Generated/WorldMap/World_InkStone.mat");
        ground.AddComponent<MeshRenderer>().sharedMaterial = material;
    }
    private static void Capture(Camera camera, Scene scene)
    {
        bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        var layers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).Select(r => r.gameObject).Distinct().ToDictionary(g => g, g => g.layer);
        foreach (var go in layers.Keys) go.layer = 29;
        int mask = camera.cullingMask; camera.cullingMask = 1 << 29;
        var target = new RenderTexture(1400, 1000, 24); var texture = new Texture2D(1400, 1000, TextureFormat.RGB24, false);
        var old = RenderTexture.active;
        try { camera.scene = scene; camera.targetTexture = target; camera.aspect = 1.4f; camera.Render(); RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, 1400, 1000), 0, 0); texture.Apply(); File.WriteAllBytes("ArtCaptures/SquirrelForest/Overview.png", texture.EncodeToPNG()); }
        finally { foreach (var pair in layers) pair.Key.layer = pair.Value; camera.cullingMask = mask; ShaderUtil.allowAsyncCompilation = async; camera.targetTexture = null; RenderTexture.active = old; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(texture); }
    }
}
