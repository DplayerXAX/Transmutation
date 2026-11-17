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
        CreateChainsaw();
    }

    public static void Place(Scene scene, Transform parent)
    {
        PlaceOne(scene, parent, "Spear", new Vector3(-14.5f, 0.5f, -13.3f));
        PlaceOne(scene, parent, "Greatsword", new Vector3(-13f, 0.5f, -13.3f));
        PlaceOne(scene, parent, "Chainsaw", parent.Find("Greatsword").localPosition + Vector3.right * 1.5f);
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

    private static void CreateChainsaw()
    {
        string path = Folder + "/Chainsaw.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
        var root = new GameObject("Chainsaw");
        try
        {
            root.SetActive(false);
            root.AddComponent<Rigidbody>().collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            root.AddComponent<BoxCollider>().size = new Vector3(0.43f, 0.42f, 0.62f);
            var weapon = root.AddComponent<Chainsaw>();
            var metal = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Greatsword Material.mat");
            string pathOrange = Folder + "/Chainsaw Housing.mat";
            var orange = AssetDatabase.LoadAssetAtPath<Material>(pathOrange);
            if (orange == null)
            {
                orange = new Material(metal) { color = new Color(0.9f, 0.24f, 0.045f) };
                AssetDatabase.CreateAsset(orange, pathOrange);
            }
            void Part(string name, Vector3 position, Vector3 size, Material material, float tilt = 0f)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                part.name = name;
                part.transform.SetParent(root.transform, false);
                part.transform.localPosition = position;
                part.transform.localRotation = Quaternion.Euler(tilt, 0f, 0f);
                part.transform.localScale = size;
                Object.DestroyImmediate(part.GetComponent<Collider>());
                part.GetComponent<Renderer>().sharedMaterial = material;
            }
            Part("Motor housing", Vector3.zero, new Vector3(0.43f, 0.42f, 0.62f), orange);
            Part("Grip", new Vector3(0f, 0.28f, -0.22f), new Vector3(0.1f, 0.1f, 0.48f), metal);
            Part("Grip support", new Vector3(0f, 0.15f, -0.42f), new Vector3(0.1f, 0.26f, 0.09f), metal);
            Part("Guide bar", Vector3.forward * 0.80f, new Vector3(0.085f, 0.19f, 1.13f), metal);
            for (int i = 0; i < 9; i++)
            {
                float z = 0.32f + i * 0.128f;
                Part("Upper tooth", new Vector3(0f, 0.104f, z), new Vector3(0.105f, 0.065f, 0.08f), metal, 35f);
                Part("Lower tooth", new Vector3(0f, -0.104f, z), new Vector3(0.105f, 0.065f, 0.08f), metal, -35f);
            }
            var tip = new GameObject("Blade tip").transform;
            tip.SetParent(root.transform, false);
            tip.localPosition = Vector3.forward * 1.37f;
            var settings = new SerializedObject(weapon);
            settings.FindProperty("bladeTip").objectReferenceValue = tip;
            settings.FindProperty("initialPolarity").enumValueIndex = (int)CreaturePolarity.None;
            settings.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { Object.DestroyImmediate(root); }
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
