using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HeldWeaponSetup
{
    private const string Folder = "Assets/Prefabs/Creatures/Asura";

    [MenuItem("Tools/Creatures/Asura/Create Held Weapons")]
    public static void CreateAndPlace()
    {
        CreateAssets();
        Scene scene = EditorSceneManager.OpenScene(AsuraPrototypeSetup.Yoyo, OpenSceneMode.Single);
        GameObject root = GameObject.Find(AsuraPrototypeSetup.RootName);
        if (root == null) throw new System.InvalidOperationException("Create the Asura prototype scene first.");
        Place(scene, root.transform);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    public static void CreateAssets()
    {
        Create<Spear>("Spear", Color.gray);
        Create<Greatsword>("Greatsword", Color.gray);
    }

    public static void Place(Scene scene, Transform parent)
    {
        PlaceOne(scene, parent, "Spear", new Vector3(-14.5f, 0.5f, -13.3f));
        PlaceOne(scene, parent, "Greatsword", new Vector3(-13f, 0.5f, -13.3f));
    }

    private static void PlaceOne(Scene scene, Transform parent, string name, Vector3 localPosition)
    {
        if (parent.Find(name) != null) return;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + name + ".prefab");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.name = name;
        instance.transform.SetParent(parent, false);
        instance.transform.localPosition = localPosition;
    }

    private static void Create<T>(string name, Color color) where T : Creature
    {
        string path = Folder + "/" + name + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
        var root = new GameObject(name);
        root.SetActive(false);
        var body = root.AddComponent<Rigidbody>();
        body.mass = 1f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        root.AddComponent<SphereCollider>().radius = 0.25f;
        var weapon = root.AddComponent<T>();

        var settings = new SerializedObject(weapon);
        settings.FindProperty("initialPolarity").enumValueIndex = (int)CreaturePolarity.None;
        settings.ApplyModifiedPropertiesWithoutUndo();
        var visual = GameObject.CreatePrimitive(weapon is Spear ? PrimitiveType.Cylinder : PrimitiveType.Cube);
        visual.name = "Blade";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = new Vector3(0f, weapon is Spear ? 0.65f : 1.1f, 0f);
        visual.transform.localScale = weapon is Spear
            ? new Vector3(0.08f, 0.75f, 0.08f) : new Vector3(0.62f, 2.65f, 0.19f);
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
        string materialPath = Folder + "/" + name + " Material.mat";
        AssetDatabase.CreateAsset(material, materialPath);
        visual.GetComponent<Renderer>().sharedMaterial = material;
        root.SetActive(true);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }
}
