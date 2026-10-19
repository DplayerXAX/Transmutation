using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Creates missing prototype assets and places one compost creature without resetting tuning.</summary>
public static class CompostCreatureSetup
{
    public const string PrefabPath = "Assets/Prefabs/Creatures/CompostCreature.prefab";
    public const string ScenePath = "Assets/Scenes/Scene_Yoyo.unity";

    [MenuItem("Tools/Creatures/Compost/Create Assets")]
    public static void CreateAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before compost asset setup.");
        var gooAsset = AssetDatabase.LoadAssetAtPath<GameObject>(GooCreatureSetup.PrefabPath);
        if (gooAsset == null) throw new InvalidOperationException("Create the Goo Creature assets before compost setup.");
        Material shell = Material("Shell", new Color(0.23f, 0.34f, 0.17f));
        Material soil = Material("Back", new Color(0.22f, 0.12f, 0.065f));
        Material limb = Material("Legs", new Color(0.43f, 0.39f, 0.23f));
        Material eye = Material("Eyes", new Color(0.035f, 0.045f, 0.02f));
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            var root = new GameObject("Compost Creature");
            root.layer = LayerMask.NameToLayer("Ground");
            try
            {
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.mass = 500f; body.isKinematic = true; body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
                BoxCollider solid = root.AddComponent<BoxCollider>();
                solid.center = new Vector3(0f, 1.55f, 0f);
                solid.size = new Vector3(1f, 0.78f, 1.85f);
                Shape(root.transform, "Shell", PrimitiveType.Sphere, new Vector3(0f, 1.55f, 0f), new Vector3(1f, 0.75f, 1.85f), shell);
                GameObject deck = Shape(root.transform, "Flat Compost Back", PrimitiveType.Cube,
                    new Vector3(0f, 1.94f, 0f), new Vector3(1f, 0.12f, 1.8f), soil);
                BoxCollider back = deck.AddComponent<BoxCollider>();
                Shape(root.transform, "Head", PrimitiveType.Sphere, new Vector3(0f, 1.35f, 1.03f), new Vector3(0.45f, 0.35f, 0.45f), limb);
                Shape(root.transform, "Tail", PrimitiveType.Cube, new Vector3(0f, 1.25f, -1.03f), new Vector3(0.15f, 0.15f, 0.3f), limb);
                for (int side = -1; side <= 1; side += 2)
                    Shape(root.transform, "Eye", PrimitiveType.Sphere, new Vector3(side * 0.17f, 1.44f, 1.18f), Vector3.one * 0.055f, eye);
                var legs = new Transform[4];
                for (int i = 0; i < 4; i++)
                {
                    Transform leg = new GameObject("Leg " + (i + 1)).transform;
                    leg.SetParent(root.transform, false);
                    leg.localPosition = new Vector3(i < 2 ? -0.44f : 0.44f, 1.25f, i % 2 == 0 ? 0.63f : -0.63f);
                    Shape(leg, "Leg", PrimitiveType.Cube, new Vector3(0f, -0.55f, 0f), new Vector3(0.2f, 1.1f, 0.24f), limb);
                    Shape(leg, "Foot", PrimitiveType.Cube, new Vector3(0f, -1.12f, 0.02f), new Vector3(0.28f, 0.2f, 0.36f), limb);
                    legs[i] = leg;
                }
                CompostRideSurface ride = root.AddComponent<CompostRideSurface>();
                using (var fields = new SerializedObject(ride))
                {
                    fields.FindProperty("back").objectReferenceValue = back;
                    fields.ApplyModifiedPropertiesWithoutUndo();
                }
                CompostCreature compost = root.AddComponent<CompostCreature>();
                using (var fields = new SerializedObject(compost))
                {
                    fields.FindProperty("creatureName").stringValue = "Compost Creature";
                    fields.FindProperty("back").objectReferenceValue = back;
                    fields.FindProperty("gooPrefab").objectReferenceValue = gooAsset.GetComponent<GooCreature>();
                    SerializedProperty array = fields.FindProperty("legs");
                    array.arraySize = legs.Length;
                    for (int i = 0; i < legs.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = legs[i];
                    fields.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        AssetDatabase.SaveAssets();
    }

    private static GameObject Shape(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        GameObject shape = GameObject.CreatePrimitive(type);
        shape.name = name;
        shape.layer = LayerMask.NameToLayer("Ground");
        shape.transform.SetParent(parent, false);
        shape.transform.localPosition = position;
        shape.transform.localScale = scale;
        Object.DestroyImmediate(shape.GetComponent<Collider>());
        shape.GetComponent<Renderer>().sharedMaterial = material;
        return shape;
    }

    private static Material Material(string suffix, Color color)
    {
        string path = "Assets/Materials/Creatures/MAT_Compost_" + suffix + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Compost " + suffix };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.18f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    [MenuItem("Tools/Creatures/Compost/Add One To Scene Yoyo")]
    public static void AddToYoyoScene()
    {
        CreateAssets();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        SmoothFirstPersonController player = null;
        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
        {
            CompostCreature existing = sceneRoot.GetComponentInChildren<CompostCreature>(true);
            if (existing != null) { Selection.activeGameObject = existing.gameObject; return; }
            if (player == null) player = sceneRoot.GetComponentInChildren<SmoothFirstPersonController>(true);
        }
        if (player == null) throw new InvalidOperationException("Scene_Yoyo needs a player for compost placement.");
        Vector3 forward = player.Orientation != null ? player.Orientation.forward : Vector3.forward;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        Vector3 position = player.transform.position + forward * 7f + Vector3.Cross(Vector3.up, forward) * 4f;
        var hits = new RaycastHit[32];
        int count = scene.GetPhysicsScene().Raycast(position + Vector3.up * 25f, Vector3.down, hits, 100f,
            LayerMask.GetMask("Ground", "Wall"), QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.GetComponentInParent<Creature>() != null || hits[i].distance >= nearest) continue;
            nearest = hits[i].distance;
            position.y = hits[i].point.y;
        }
        if (nearest == float.MaxValue) throw new InvalidOperationException("No ground at compost placement point.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        Undo.RegisterCreatedObjectUndo(instance, "Place compost creature");
        instance.transform.position = position;
        instance.transform.rotation = Quaternion.LookRotation(forward);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = instance;
        Debug.Log("Compost Creature placed in Scene_Yoyo. Existing death states are unchanged.");
    }
}
