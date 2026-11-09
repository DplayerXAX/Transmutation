using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class AsuraFeelSetup
{
    public const string ScenePath = "Assets/Scenes/Scene_AsuraShowcase.unity";
    private const string Folder = AsuraPrototypeSetup.Folder;
    private static void Set(Object target, string field, float value)
    { var so = new SerializedObject(target); so.FindProperty(field).floatValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }

    [MenuItem("Tools/Creatures/Asura/Upgrade Contact Feel Assets")]
    public static void Upgrade()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        AsuraPrototypeSetup.CreateAssets();
        string[] names = { "MeleeAsura", "RangedAsura", "GeneralAsura" };
        float[] scales = { 0.8f, 1.1f, 1.5f };
        for (int role = 0; role < names.Length; role++)
        {
            string path = Folder + "/" + names[role] + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.transform.localScale = Vector3.one * scales[role];
                Set(root.GetComponent<AsuraMotor>(), "knockbackMultiplier", role == 0 ? 1f : role == 1 ? 0.6f : 0.25f);
                if (role == 0)
                {
                    Set(root.GetComponent<MeleeAttack>(), "interval", 0.9f);
                    Set(root.GetComponent<MeleeAttack>(), "windup", 0.1f);
                }
                var visual = root.transform.Find("Visual");
                var body = visual.Find("Body").GetComponent<Renderer>().sharedMaterial;
                var dark = visual.Find("Eye").GetComponent<Renderer>().sharedMaterial;
                if (visual.Find("Crest") == null)
                    Part(visual, "Crest", PrimitiveType.Cube, new Vector3(0f, role == 2 ? 2f : role == 1 ? 1.35f : 1.08f, 0f), new Vector3(role == 2 ? 1.5f : 0.45f, 0.16f, 0.25f), dark);
                if (role == 0 && visual.Find("Striking Arm") == null)
                {
                    for (int side = -1; side <= 1; side += 2)
                        Part(visual, "Striking Arm", PrimitiveType.Cube, new Vector3(side * 0.66f, 0.53f, 0.34f), new Vector3(0.28f, 0.55f, 0.38f), body);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        BuildBomber(); AssetDatabase.SaveAssets();
    }
    private static void BuildBomber()
    {
        string path = Folder + "/RollingAsura.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
        var root = new GameObject("RollingAsura"); root.SetActive(false);
        try
        {
            var rb = root.AddComponent<Rigidbody>(); rb.mass = 0.8f; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            var sphere = root.AddComponent<SphereCollider>(); sphere.radius = 0.45f; sphere.center = Vector3.up * 0.45f;
            sphere.sharedMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/Materials/Creatures/Asura/Locomotion.physicMaterial");
            root.AddComponent<RollingAsura>(); root.AddComponent<WebCapture>(); root.AddComponent<BubbleCapture>();
            var so = new SerializedObject(root.GetComponent<Creature>()); so.FindProperty("maxHealth").floatValue = 40f;
            so.FindProperty("initialPolarity").enumValueIndex = (int)CreaturePolarity.Light; so.ApplyModifiedPropertiesWithoutUndo();
            var visual = new GameObject("Visual").transform; visual.SetParent(root.transform, false); visual.localPosition = Vector3.up * 0.45f;
            var body = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Creatures/Asura/Body.mat");
            var eyes = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Creatures/Asura/Eyes.mat");
            Part(visual, "Shell", PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.9f, body);
            Part(visual, "Band", PrimitiveType.Cube, Vector3.zero, new Vector3(0.94f, 0.13f, 0.94f), eyes);
            Part(visual, "Core", PrimitiveType.Sphere, Vector3.forward * 0.4f, Vector3.one * 0.3f, body);
            root.AddComponent<AsuraPresentation>(); root.SetActive(true); PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { Object.DestroyImmediate(root); }
    }
    private static GameObject Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(type); part.name = name; part.transform.SetParent(parent, false);
        part.transform.localPosition = position; part.transform.localScale = scale;
        Object.DestroyImmediate(part.GetComponent<Collider>()); part.GetComponent<Renderer>().sharedMaterial = material; return part;
    }
    private static GameObject Spawn(string name, Transform parent, Vector3 position, CreaturePolarity polarity)
    {
        var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + name + ".prefab"), parent);
        root.transform.localPosition = position;
        root.transform.localRotation = Quaternion.LookRotation(position.x < 0f ? Vector3.right : Vector3.left);
        var so = new SerializedObject(root.GetComponent<Creature>()); so.FindProperty("initialPolarity").enumValueIndex = (int)polarity; so.ApplyModifiedPropertiesWithoutUndo();
        return root;
    }

    [MenuItem("Tools/Creatures/Asura/Create Contact Showcase")]
    public static void CreateShowcase()
    {
        Upgrade();
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return;
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
        var controller = new GameObject("Ashura contact showcase").AddComponent<AsuraShowcase>();
        controller.Templates = new GameObject[6];
        var floorMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Creatures/Asura/Arena.mat");
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Ground"; floor.layer = 7;
        floor.transform.position = Vector3.down * 0.5f; floor.transform.localScale = new Vector3(40f, 1f, 36f); floor.GetComponent<Renderer>().sharedMaterial = floorMat;
        var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2f; light.transform.rotation = Quaternion.Euler(48f, -28f, 0f);
        RenderSettings.ambientLight = new Color(0.5f, 0.55f, 0.65f);
        var observer = new GameObject("Observation camera").AddComponent<Camera>(); observer.transform.position = new Vector3(0f, 3.8f, -13f);
        observer.transform.LookAt(Vector3.up * 0.7f); observer.fieldOfView = 55f; observer.backgroundColor = new Color(0.1f, 0.14f, 0.2f); observer.clearFlags = CameraClearFlags.SolidColor;
        controller.Observer = observer;
        var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerObjects.prefab"), scene);
        player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        controller.Player = player.GetComponentInChildren<SmoothFirstPersonController>();
        controller.Player.transform.position = new Vector3(-9f, 1.5f, -7f);
        foreach (var camera in player.GetComponentsInChildren<Camera>()) camera.enabled = false;
        var text = new GameObject("Showcase controls").AddComponent<TextMesh>(); text.transform.position = new Vector3(0f, 5f, 4f);
        text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center; text.characterSize = 0.18f; text.fontSize = 48; text.color = Color.white;
        text.text = "ASHURA CONTACT SHOWCASE\n1-6: encounter   R: reset   TAB: side / overview / walk"; controller.Status = text;
        for (int station = 0; station < 6; station++)
        {
            var template = new GameObject("Station " + (station + 1)); template.SetActive(false); controller.Templates[station] = template;
            if (station == 0) { Spawn("MeleeAsura", template.transform, new Vector3(-2.8f, 0.05f, 0f), CreaturePolarity.Light); Spawn("MeleeAsura", template.transform, new Vector3(2.8f, 0.05f, 0f), CreaturePolarity.Dark); }
            if (station == 1)
            {
                string[] names = { "MeleeAsura", "RangedAsura", "GeneralAsura" };
                for (int i = 0; i < 3; i++) Spawn(names[i], template.transform, new Vector3(-4f + i * 4f, 0.05f, 0f), CreaturePolarity.None);
                foreach (string name in new[] { "GooCreature", "FireFlower" })
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/" + name + ".prefab");
                    if (prefab == null) continue;
                    var sample = (GameObject)PrefabUtility.InstantiatePrefab(prefab, template.transform); sample.transform.localPosition = new Vector3(name == "GooCreature" ? -2f : 2f, 0.5f, 4f);
                    var so = new SerializedObject(sample.GetComponent<Creature>()); so.FindProperty("initialPolarity").enumValueIndex = 0; so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            if (station == 2 || station == 3)
                for (int side = -1; side <= 1; side += 2)
                {
                    var polarity = side < 0 ? CreaturePolarity.Light : CreaturePolarity.Dark;
                    for (int i = 0; i < (station == 2 ? 6 : 7); i++) Spawn("MeleeAsura", template.transform, new Vector3(side * (4f + (i % 2) * 1.3f), 0.05f, -3f + i), polarity);
                    if (station == 3) { for (int i = 0; i < 2; i++) Spawn("RangedAsura", template.transform, new Vector3(side * 8f, 0.05f, -3f + i * 6f), polarity); Spawn("GeneralAsura", template.transform, new Vector3(side * 12f, 0.05f, 3f), polarity); }
                }
            if (station == 4)
            {
                Spawn("BlastCore", template.transform, new Vector3(-7f, 0.6f, -5f), CreaturePolarity.None);
                Box(template.transform, "Blast wall", new Vector3(4f, 1.5f, 0f), new Vector3(1f, 3f, 8f), floorMat);
                var ramp = Box(template.transform, "Slope", new Vector3(-3f, 0.6f, 4f), new Vector3(5f, 0.5f, 6f), floorMat); ramp.transform.rotation = Quaternion.Euler(-18f, 0f, 0f);
                Box(template.transform, "Ledge", new Vector3(0f, 1.5f, 9f), new Vector3(8f, 3f, 3f), floorMat);
            }
            if (station == 5) { Spawn("RollingAsura", template.transform, new Vector3(-5f, 0.05f, 0f), CreaturePolarity.Light); for (int i = 0; i < 3; i++) Spawn("MeleeAsura", template.transform, new Vector3(3f, 0.05f, -1.2f + i * 1.2f), CreaturePolarity.Dark); }
        }
        var effects = controller.gameObject.AddComponent<AsuraPrototypeEffects>();
        var settings = new SerializedObject(effects); settings.FindProperty("explosionMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Creatures/Asura/Explosion.mat"); settings.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.SaveScene(scene, ScenePath); EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(previous);
    }
    private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = name; box.layer = 7; box.transform.SetParent(parent, false);
        box.transform.localPosition = position; box.transform.localScale = scale; box.GetComponent<Renderer>().sharedMaterial = material; return box;
    }
}
