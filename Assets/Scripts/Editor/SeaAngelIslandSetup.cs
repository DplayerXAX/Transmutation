using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SeaAngelIslandSetup
{
    const string PrefabPath = "Assets/Prefabs/Creatures/Fishing/SeaAngelIsland.prefab";
    const string MaterialPath = "Assets/Prefabs/Creatures/Fishing/IslandBack.mat";
    [MenuItem("Tools/Creatures/Fishing/Build Moving Sea Angel Island")]
    public static void Build()
    {
        if (Application.isPlaying) throw new System.InvalidOperationException("Exit Play Mode before building the island.");
        Scene scene = SceneManager.GetSceneByPath(FishingSceneSetup.ScenePath);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(FishingSceneSetup.ScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        foreach (var root in scene.GetRootGameObjects())
            if (root.GetComponent<MovingFrame>()) { Selection.activeGameObject = root; return; }

        var routeObject = new GameObject("Island Flight Route — edit child waypoints");
        var route = routeObject.AddComponent<IslandFlightPath>();
        Vector3[] points = { new(0,12,0), new(64,20,18), new(90,32,60), new(64,25,102),
            new(0,14,120), new(-64,12,102), new(-90,25,60), new(-64,20,18) };
        route.waypoints = new Transform[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            var point = new GameObject($"Waypoint {i + 1:00}"); point.transform.SetParent(route.transform);
            point.transform.position = points[i]; route.waypoints[i] = point.transform;
        }
        route.Rebuild();
        var rootObject = new GameObject("Giant Sea Angel Island");
        rootObject.transform.SetPositionAndRotation(route.Position(0), route.Heading(0));
        var frame = rootObject.AddComponent<MovingFrame>();
        var islandBody = rootObject.GetComponent<Rigidbody>(); islandBody.isKinematic = true;
        islandBody.useGravity = false; islandBody.interpolation = RigidbodyInterpolation.Interpolate;
        var habitat = rootObject.AddComponent<IslandHabitat>(); habitat.frame = frame;
        var visuals = rootObject.AddComponent<SeaAngelIslandVisuals>();
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!material)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", new Color(.14f,.24f,.3f));
            material.SetFloat("_Smoothness", .65f); AssetDatabase.CreateAsset(material, MaterialPath);
        }
        var deck = new GameObject("Continuous Back Collision"); deck.layer = 7; deck.transform.SetParent(rootObject.transform, false);
        var deckCollider = deck.AddComponent<BoxCollider>(); deckCollider.center = new Vector3(0,-.5f,0);
        deckCollider.size = new Vector3(48,1,72); frame.deck = deckCollider;
        for (int x = 0; x < 2; x++) for (int z = 0; z < 3; z++)
        {
            var tile = Part(rootObject.transform, $"Back {x+1}-{z+1}", PrimitiveType.Cube,
                new Vector3((x-.5f)*24,-.5f,(z-1)*24), new Vector3(24,1,24), material);
            Object.DestroyImmediate(tile.GetComponent<Collider>());
        }
        IslandPrototypeRefresh.UpdateAppearance(rootObject);
        IslandPrototypeRefresh.AddHill(rootObject);
        var underside = new GameObject("Body Collision"); underside.transform.SetParent(rootObject.transform, false);
        var shell = underside.AddComponent<CapsuleCollider>(); shell.direction = 2; shell.center = new Vector3(0,-18,-20);
        shell.radius = 18; shell.height = 156;
        PrefabUtility.SaveAsPrefabAssetAndConnect(rootObject, PrefabPath, InteractionMode.AutomatedAction);
        frame.route = route;

        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "Fishing Platform") Object.DestroyImmediate(root);
        var controller = FindInScene<SmoothFirstPersonController>(scene);
        if (controller)
        {
            controller.transform.position = frame.transform.TransformPoint(new Vector3(0,2,-15));
            controller.SetLook(frame.transform.eulerAngles.y, 8);
            var member = controller.gameObject.AddComponent<MovementFrameMember>();
            var recovery = rootObject.AddComponent<IslandRecovery>(); recovery.frame = frame; recovery.player = controller.GetComponent<Rigidbody>();
        }
        int index = 0;
        foreach (var creature in Object.FindObjectsByType<Creature>(FindObjectsSortMode.None))
        {
            if (creature.gameObject.scene != scene) continue;
            Vector3 local;
            if (creature is SeaAngelCreature) { local = new Vector3((index%4-1.5f)*3,2+(index%3),-5+(index/4)*5); index++; }
            else if (creature is FishingPoleCreature) local = new Vector3(1,1,-13);
            else local = frame.transform.InverseTransformPoint(creature.transform.position) + new Vector3(0,12,0);
            creature.transform.position = frame.transform.TransformPoint(local);
            if (!creature.TryGetComponent<MovementFrameMember>(out _)) creature.gameObject.AddComponent<MovementFrameMember>();
        }
        var scenery = new GameObject("Flight Reference Scenery");
        for (int i = 0; i < 10; i++)
        {
            float angle = i * Mathf.PI * 2 / 10;
            Part(scenery.transform, "Distant Spire", PrimitiveType.Cylinder,
                new Vector3(Mathf.Cos(angle)*210,-35,60+Mathf.Sin(angle)*190), new Vector3(12,40+i%3*8,12), material);
        }
        var overview = new GameObject("Island Overview Camera (enable for showcase)");
        var camera = overview.AddComponent<Camera>(); camera.enabled = false;
        overview.transform.position = new Vector3(180,130,-150); overview.transform.LookAt(new Vector3(0,15,60)); camera.farClipPlane = 1000;
        Undo.RegisterCreatedObjectUndo(rootObject, "Build sea angel island");
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets(); Selection.activeGameObject = routeObject;
    }
    static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (var item in Object.FindObjectsByType<T>(FindObjectsSortMode.None)) if (item.gameObject.scene == scene) return item;
        return null;
    }
    static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(type); part.name = name; part.transform.SetParent(parent, false);
        part.transform.localPosition = position; part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material; return part;
    }
}
