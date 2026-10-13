using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Creates simple prototype assets without replacing existing Inspector tuning.</summary>
public static class BubbleFlowerSetup
{
    public const string FlowerPath = "Assets/Prefabs/Creatures/BubbleFlower.prefab";
    public const string BubblePath = "Assets/Prefabs/Creatures/TrappingBubble.prefab";
    public const string ScenePath = "Assets/Scenes/Scene_Yoyo.unity";
    private const string PendingSetup = "Library/BubbleFlowerSetup.pending";

    [InitializeOnLoadMethod]
    private static void CompletePendingSetup()
    {
        if (!System.IO.File.Exists(PendingSetup)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !System.IO.File.Exists(PendingSetup)) return;
            try
            {
                AddToYoyoScene();
                string validation = BubbleFlowerValidation.ValidateAssets();
                System.IO.Directory.CreateDirectory("Temp/BubbleFlowerValidation");
                System.IO.File.WriteAllText("Temp/BubbleFlowerValidation/setup.txt", "Flower placed in Scene_Yoyo. " + validation);
                System.IO.File.Delete(PendingSetup);
                Debug.Log(validation);
            }
            catch (Exception exception) { Debug.LogException(exception); }
        };
    }

    [MenuItem("Tools/Creatures/Create Bubble Flower Assets")]
    public static void CreateAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before creating assets.");
        EnsureFolder("Assets/Prefabs/Creatures");
        EnsureFolder("Assets/Materials/Creatures");
        Material stem = Material("BubbleFlowerStem", new Color(0.15f, 0.5f, 0.24f));
        Material petals = Material("BubbleFlowerPetals", new Color(0.66f, 0.4f, 0.88f));
        Material bubbleMaterial = Material("TrappingBubble", new Color(0.4f, 0.85f, 1f, 0.16f), true);
        const string physicsPath = "Assets/Materials/Creatures/TrappingBubble.physicMaterial";
        PhysicsMaterial physics = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(physicsPath);
        if (physics == null)
        {
            physics = new PhysicsMaterial("TrappingBubble") { bounciness = 0f, dynamicFriction = 0f, staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum };
            AssetDatabase.CreateAsset(physics, physicsPath);
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(BubblePath) == null)
        {
            GameObject root = new GameObject("TrappingBubble");
            try
            {
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.useGravity = false; body.mass = 0.2f;
                body.constraints = RigidbodyConstraints.FreezeRotation;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                SphereCollider solid = root.AddComponent<SphereCollider>();
                solid.radius = 0.4f; solid.sharedMaterial = physics;
                GameObject shell = Shape(root.transform, "Shell", PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.8f, bubbleMaterial);
                shell.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                shell.GetComponent<Renderer>().receiveShadows = false;
                GameObject trigger = new GameObject("CaptureTrigger");
                trigger.transform.SetParent(root.transform, false);
                SphereCollider capture = trigger.AddComponent<SphereCollider>();
                capture.isTrigger = true; capture.radius = 0.425f;
                TrappingBubble bubble = root.AddComponent<TrappingBubble>();
                var fields = new SerializedObject(bubble);
                fields.FindProperty("shell").objectReferenceValue = shell.transform;
                fields.FindProperty("creatureName").stringValue = "Bubble";
                fields.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, BubblePath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(FlowerPath) == null)
        {
            GameObject root = new GameObject("BubbleFlower");
            try
            {
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.isKinematic = false; body.useGravity = true;
                body.constraints = RigidbodyConstraints.FreezeRotation;
                CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
                collider.center = new Vector3(0f, 0.42f, 0f); collider.height = 0.84f; collider.radius = 0.26f;
                Shape(root.transform, "Stem", PrimitiveType.Cube, new Vector3(0f, 0.3f, 0f), new Vector3(0.12f, 0.6f, 0.12f), stem);
                Shape(root.transform, "Head", PrimitiveType.Sphere, new Vector3(0f, 0.65f, 0f), Vector3.one * 0.3f, petals);
                for (int i = 0; i < 4; i++)
                {
                    Quaternion rotation = Quaternion.Euler(0f, i * 90f, 0f);
                    GameObject petal = Shape(root.transform, "Petal " + i, PrimitiveType.Cube,
                        new Vector3(0f, 0.65f, 0f) + rotation * Vector3.forward * 0.24f, new Vector3(0.22f, 0.1f, 0.34f), petals);
                    petal.transform.localRotation = rotation;
                }
                Shape(root.transform, "Mouth", PrimitiveType.Sphere, new Vector3(0f, 0.77f, 0f), new Vector3(0.21f, 0.09f, 0.21f), bubbleMaterial);
                Transform muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(root.transform, false); muzzle.localPosition = new Vector3(0f, 0.85f, 0f);
                BubbleFlower flower = root.AddComponent<BubbleFlower>();
                var fields = new SerializedObject(flower);
                fields.FindProperty("bubblePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(BubblePath).GetComponent<TrappingBubble>();
                fields.FindProperty("muzzle").objectReferenceValue = muzzle;
                fields.FindProperty("creatureName").stringValue = "Bubble Flower";
                fields.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, FlowerPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        AssetDatabase.SaveAssets();
    }

    private static Material Material(string name, Color color, bool transparent = false)
    {
        string path = "Assets/Materials/Creatures/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("URP Lit shader is unavailable.");
        material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", transparent ? 0.9f : 0.25f);
        if (transparent)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f); material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetShaderPassEnabled("ShadowCaster", false);
        }
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static GameObject Shape(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        GameObject shape = GameObject.CreatePrimitive(type);
        shape.name = name; shape.transform.SetParent(parent, false);
        shape.transform.localPosition = position; shape.transform.localScale = scale;
        Object.DestroyImmediate(shape.GetComponent<Collider>());
        shape.GetComponent<Renderer>().sharedMaterial = material;
        return shape;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }

    [MenuItem("Tools/Creatures/Add Bubble Flower To Scene Yoyo")]
    public static void AddToYoyoScene()
    {
        CreateAssets();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        bool hasHud = false;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.GetComponentInChildren<InteractionPromptHUD>(true) != null) { hasHud = true; break; }
        if (!hasHud)
        {
            GameObject hud = new GameObject("Interaction Prompts");
            SceneManager.MoveGameObjectToScene(hud, scene);
            Undo.RegisterCreatedObjectUndo(hud, "Add interaction prompts");
            hud.AddComponent<InteractionPromptHUD>();
            EditorSceneManager.MarkSceneDirty(scene);
        }
        foreach (GameObject existing in scene.GetRootGameObjects())
            if (existing.GetComponent<BubbleFlower>() != null)
            {
                if (!hasHud) EditorSceneManager.SaveScene(scene);
                Selection.activeGameObject = existing;
                return;
            }
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FlowerPath), scene);
        Undo.RegisterCreatedObjectUndo(instance, "Place Bubble Flower");
        Vector3 position = new Vector3(2f, 0f, 4f);
        foreach (var player in Object.FindObjectsByType<SmoothFirstPersonController>(FindObjectsSortMode.None))
            if (player.gameObject.scene == scene)
            {
                Vector3 forward = player.Orientation != null ? player.Orientation.forward : Vector3.forward;
                position = player.transform.position + Vector3.ProjectOnPlane(forward, Vector3.up).normalized * 4f + Vector3.right * 2f;
                break;
            }
        if (Physics.Raycast(position + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 100f,
            LayerMask.GetMask("Ground", "Form"), QueryTriggerInteraction.Ignore)) position.y = hit.point.y;
        instance.transform.position = position;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = instance;
        Debug.Log("Bubble Flower placed in Scene_Yoyo; prototype prefabs are ready.");
    }
}
