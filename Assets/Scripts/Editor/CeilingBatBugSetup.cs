using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class CeilingBatBugSetup
{
    public const string EggPath = "Assets/Prefabs/Creatures/CeilingBatBugEgg.prefab";
    [MenuItem("Tools/Creatures/Ceiling Bat Bugs/Install Assets And Update Scene Goo")]
    public static string Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var bugContents = PrefabUtility.LoadPrefabContents(GooSceneSetup.BugPath);
        try
        {
            if (bugContents.GetComponent<CeilingBatBugLife>() == null) bugContents.AddComponent<CeilingBatBugLife>();
            Edit(bugContents.GetComponent<CeilingBatBug>(), s => { F(s, "age", 65f); F(s, "ageRate", 1f); });
            Edit(bugContents.GetComponent<CeilingBatBugSurfaceMotor>(), s => { F(s, "roamRadius", 18f); F(s, "speed", 1.2f); });
            PrefabUtility.SaveAsPrefabAsset(bugContents, GooSceneSetup.BugPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(bugContents); }
        CreateEgg();
        var playerContents = PrefabUtility.LoadPrefabContents("Assets/Prefabs/PlayerObjects.prefab");
        try
        {
            var player = playerContents.GetComponentInChildren<SmoothFirstPersonController>();
            if (player.GetComponent<PlayerBugGrip>() == null) player.gameObject.AddComponent<PlayerBugGrip>();
            PrefabUtility.SaveAsPrefabAsset(playerContents, "Assets/Prefabs/PlayerObjects.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(playerContents); }
        if (SceneManager.GetActiveScene().path != GooSceneSetup.ScenePath) return "Assets installed; open Scene Goo and run again for placement.";
        Transform content = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == "Goo Level Content").transform;
        var world = content.GetComponentInChildren<ProceduralWorld>();
        if (!world.LayoutReady) world.Generate(true);
        Physics.SyncTransforms();
        var cave = Habitat(content, "Bat Bug Habitat — cavern", new Vector3(0f, -8f, 8f), new Vector3(34f, 24f, 26f), 18f, 10f, 7, world);
        var shaft = Habitat(content, "Bat Bug Habitat — shaft", new Vector3(4f, 11f, 66f), new Vector3(29f, 48f, 30f), 28f, 8f, 4, world);
        var bugs = content.GetComponentsInChildren<CeilingBatBug>();
        for (int i = 0; i < bugs.Length; i++)
        {
            var bug = bugs[i]; bool cavern = bug.name.StartsWith("Cavern");
            int n = int.Parse(bug.name.Substring(bug.name.LastIndexOf(' ') + 1)) - (cavern ? 1 : 25);
            Vector3 origin, direction;
            if (cavern)
            {
                if (n < 16) { origin = new Vector3((n % 4 - 1.5f) * 3.5f, -12f, 3f + (n / 4) * 3f); direction = Vector3.up; }
                else { origin = new Vector3(0f, -13f + (n % 3) * 2.5f, 3f + (n - 16) * 1.7f); direction = n % 2 == 0 ? Vector3.left : Vector3.right; }
            }
            else
            {
                int band = n / 4;
                float y = -5.5f + band * 13f + (n % 4) * 2f;
                float angle = (30f + band * 20f) * Mathf.Deg2Rad;
                origin = new Vector3(4f, y, 64f); direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }
            var habitat = cavern ? cave : shaft;
            RaycastHit hit = GooSceneSetup.FindTerrainHit(world, origin, direction, 35f);
            if (!habitat.Contains(hit.point + hit.normal * 0.23f)) hit = GooSceneSetup.FindTerrainHit(world, origin, Vector3.up, 35f);
            Vector3 heading = Vector3.ProjectOnPlane(Mathf.Abs(hit.normal.y) > 0.85f ? Quaternion.AngleAxis(n * 97f, Vector3.up) * Vector3.forward : Vector3.up, hit.normal).normalized;
            bug.transform.SetPositionAndRotation(hit.point + hit.normal * 0.23f, Quaternion.LookRotation(heading, hit.normal));
            var life = bug.GetComponent<CeilingBatBugLife>(); if (life == null) life = bug.gameObject.AddComponent<CeilingBatBugLife>();
            life.InitializeAdult(habitat, habitat.Home, 20f + (n * 17 % 41), 941 + i * 13);
            Edit(bug.GetComponent<CeilingBatBugSurfaceMotor>(), s => F(s, "speed", cavern ? 1.15f : 0.85f));
            PrefabUtility.RecordPrefabInstancePropertyModifications(bug.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(bug);
            PrefabUtility.RecordPrefabInstancePropertyModifications(life);
            PrefabUtility.RecordPrefabInstancePropertyModifications(bug.GetComponent<CeilingBatBugSurfaceMotor>());
            EditorUtility.SetDirty(life);
        }
        foreach (var player in SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SmoothFirstPersonController>()))
            if (player.GetComponent<PlayerBugGrip>() == null) player.gameObject.AddComponent<PlayerBugGrip>();
        var recycler = content.GetComponentsInChildren<CompostCreature>().First(c => c.name.Contains("ceiling"));
        Edit(recycler, s => F(s, "roamRadius", 4f));
        var colony = bugs.FirstOrDefault()?.transform.parent;
        if (colony != null) colony.name = "Ceiling bat bugs — local eggs and moving holds";
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        return "Updated bug/egg/player prefabs and 36 Scene Goo bugs; terrain preserved.";
    }
    private static CeilingBatBugHabitat Habitat(Transform parent, string name, Vector3 at, Vector3 size, float roam, float sensing, int limit, ProceduralWorld world)
    {
        var found = parent.Find(name);
        var go = found != null ? found.gameObject : new GameObject(name);
        go.transform.SetParent(parent, true); go.transform.position = at;
        var h = go.GetComponent<CeilingBatBugHabitat>(); if (h == null) h = go.AddComponent<CeilingBatBugHabitat>();
        h.world = world; h.bugPrefab = AssetDatabase.LoadAssetAtPath<CeilingBatBug>(GooSceneSetup.BugPath);
        h.eggPrefab = AssetDatabase.LoadAssetAtPath<CeilingBatBugEgg>(EggPath);
        h.localBounds = new Bounds(Vector3.zero, size); h.roamRadius = roam; h.sensingRadius = sensing; h.localLivingLimit = limit;
        EditorUtility.SetDirty(h); return h;
    }
    private static void CreateEgg()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(EggPath) != null)
        {
            var existing = PrefabUtility.LoadPrefabContents(EggPath);
            try
            {
                Edit(existing.GetComponent<CeilingBatBugEgg>(), s => { F(s, "age", 40f); F(s, "ageRate", 0f); F(s, "minimumImpactSpeed", 11f); });
                PrefabUtility.SaveAsPrefabAsset(existing, EggPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(existing); }
            return;
        }
        GameObject root = new GameObject("Ceiling Bat Bug Egg");
        try
        {
            var body = root.AddComponent<Rigidbody>(); body.mass = 0.12f; body.useGravity = true; body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            root.AddComponent<SphereCollider>().radius = 0.13f;
            var egg = root.AddComponent<CeilingBatBugEgg>();
            Material amber = AssetDatabase.LoadAssetAtPath<Material>(GooSceneSetup.Generated + "/BatBugAmber.mat");
            Transform shell = Shape(root.transform, "Developing shell", Vector3.zero, new Vector3(0.25f, 0.29f, 0.25f), amber);
            var broken = new GameObject("Cracked shell").transform; broken.SetParent(root.transform, false);
            for (int i = 0; i < 3; i++) Shape(broken, "Shell fragment", new Vector3((i - 1) * 0.08f, -0.04f, i % 2 * 0.06f), new Vector3(0.12f, 0.08f, 0.14f), amber);
            broken.gameObject.SetActive(false);
            Edit(egg, s => { F(s, "age", 40f); F(s, "ageRate", 0f); F(s, "minimumImpactSpeed", 11f); s.FindProperty("shell").objectReferenceValue = shell; s.FindProperty("brokenShell").objectReferenceValue = broken; });
            PrefabUtility.SaveAsPrefabAsset(root, EggPath);
        }
        finally { Object.DestroyImmediate(root); }
    }
    private static Transform Shape(Transform parent, string name, Vector3 position, Vector3 size, Material material)
    {
        var shape = GameObject.CreatePrimitive(PrimitiveType.Sphere); shape.name = name; shape.transform.SetParent(parent, false);
        shape.transform.localPosition = position; shape.transform.localScale = size; Object.DestroyImmediate(shape.GetComponent<Collider>());
        shape.GetComponent<Renderer>().sharedMaterial = material; return shape.transform;
    }
    private static void Edit(Object target, Action<SerializedObject> edit)
    { using (var s = new SerializedObject(target)) { edit(s); s.ApplyModifiedPropertiesWithoutUndo(); } }
    private static void F(SerializedObject s, string field, float value) => s.FindProperty(field).floatValue = value;
}
