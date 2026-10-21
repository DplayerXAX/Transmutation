using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Editor-only authoring. Nothing spawns or repopulates combatants at runtime.</summary>
public static class AsuraPrototypeSetup
{
    public const string Folder = "Assets/Prefabs/Creatures/Asura";
    public const string Yoyo = "Assets/Scenes/Scene_Yoyo.unity";
    public const string RootName = "Asura Prototype";
    private const string Materials = "Assets/Materials/Creatures/Asura";
    private static Material bodyMaterial, eyeMaterial, floorMaterial, explosionMaterial;
    private static PhysicsMaterial friction;

    [MenuItem("Tools/Creatures/Asura/Create Assets")]
    public static void CreateAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before authoring assets.");
        EnsureFolder(Folder); EnsureFolder(Materials);
        bodyMaterial = Material("Body", new Color(0.96f, 0.78f, 0.3f));
        eyeMaterial = Material("Eyes", new Color(0.025f, 0.035f, 0.055f));
        floorMaterial = Material("Arena", new Color(0.2f, 0.25f, 0.29f));
        explosionMaterial = Material("Explosion", new Color(1f, 0.42f, 0.06f), true);
        friction = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(Materials + "/Locomotion.physicMaterial");
        if (friction == null)
        {
            friction = new PhysicsMaterial("Asura Locomotion") { staticFriction = 0f, dynamicFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum };
            AssetDatabase.CreateAsset(friction, Materials + "/Locomotion.physicMaterial");
        }
        BuildProjectile(false); BuildProjectile(true); BuildCore();
        BuildAsura("MeleeAsura", 0); BuildAsura("RangedAsura", 1); BuildAsura("GeneralAsura", 2);
        AssetDatabase.SaveAssets();
    }

    private static void BuildProjectile(bool explosive)
    {
        string name = explosive ? "ExplosiveProjectile" : "BasicProjectile";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + name + ".prefab") != null) return;
        GameObject root = new GameObject(name); root.SetActive(false);
        try
        {
            Rigidbody body = Body(root, explosive ? 0.2f : 0.08f); body.useGravity = false;
            root.AddComponent<SphereCollider>().radius = explosive ? 0.18f : 0.1f;
            Creature creature = explosive ? (Creature)root.AddComponent<ExplosiveProjectile>() : root.AddComponent<BasicProjectile>();
            ConfigureCreature(creature, explosive ? 10f : 5f, CreaturePolarity.None, 20f, 1f);
            root.AddComponent<WebCapture>(); root.AddComponent<BubbleCapture>();
            Transform visual = Child(root.transform, "Visual", Vector3.zero);
            Part(visual, explosive ? PrimitiveType.Sphere : PrimitiveType.Capsule, "Seed",
                Vector3.zero, explosive ? Vector3.one * 0.36f : new Vector3(0.13f, 0.26f, 0.13f), bodyMaterial);
            root.AddComponent<AsuraPresentation>();
            Save(root, name);
        }
        finally { Object.DestroyImmediate(root); }
    }
    private static void BuildCore()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/BlastCore.prefab") != null) return;
        GameObject root = new GameObject("BlastCore"); root.SetActive(false);
        try
        {
            Body(root, 0.5f); root.AddComponent<SphereCollider>().radius = 0.3f;
            BlastCore core = root.AddComponent<BlastCore>();
            ConfigureCreature(core, 100f, CreaturePolarity.Random, 90f, 1f);
            ConfigureAttack(core.GetComponent<ProjectileAttack>(), 16f, 60f, 1f, 0f, true);
            root.AddComponent<WebCapture>(); root.AddComponent<BubbleCapture>();
            Transform visual = Child(root.transform, "Visual", Vector3.zero);
            Part(visual, PrimitiveType.Sphere, "Core", Vector3.zero, Vector3.one * 0.6f, bodyMaterial);
            Part(visual, PrimitiveType.Cube, "Ring", Vector3.zero, new Vector3(0.75f, 0.09f, 0.16f), eyeMaterial);
            Transform muzzle = Child(root.transform, "Muzzle", new Vector3(0f, 0f, 0.45f));
            Set(core, fields => fields.FindProperty("muzzle").objectReferenceValue = muzzle);
            root.AddComponent<AsuraPresentation>(); Save(root, "BlastCore");
        }
        finally { Object.DestroyImmediate(root); }
    }
    private static void BuildAsura(string name, int role)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + name + ".prefab") != null) return;
        GameObject root = new GameObject(name); root.SetActive(false);
        try
        {
            Body(root, role == 2 ? 5f : 1f);
            var collider = root.AddComponent<BoxCollider>();
            Vector3 size = role == 0 ? new Vector3(1.2f, 1f, 1.1f) :
                role == 1 ? new Vector3(0.65f, 1.2f, 0.75f) : new Vector3(1.7f, 1.8f, 1.6f);
            collider.size = size; collider.center = Vector3.up * size.y * 0.5f; collider.sharedMaterial = friction;
            Creature creature = role == 0 ? (Creature)root.AddComponent<MeleeAsura>() :
                role == 1 ? root.AddComponent<RangedAsura>() : root.AddComponent<GeneralAsura>();
            ConfigureCreature(creature, role == 0 ? 100f : role == 1 ? 80f : 300f, CreaturePolarity.Light, 40f, 0f);
            float reach = role == 0 ? 1.5f : role == 1 ? 12f : 16f;
            ConfigureAttack(creature.GetComponent<CreatureAttack>(), reach, role == 0 ? 20f : role == 1 ? 12f : 60f,
                role == 0 ? 1f : role == 1 ? 1.2f : 3.5f, role == 0 ? 0.2f : role == 1 ? 0.25f : 0.6f, role == 2);
            Transform pivot = Child(root.transform, "Firing Pivot", Vector3.up * size.y * 0.65f);
            Transform muzzle = Child(pivot, "Muzzle", Vector3.forward * (size.z * 0.5f + 0.24f));
            Set(creature.GetComponent<CreatureCombat>(), fields =>
            {
                fields.FindProperty("moveSpeed").floatValue = role == 0 ? 3f : role == 1 ? 2.5f : 1.5f;
                fields.FindProperty("firingPivot").objectReferenceValue = pivot;
                fields.FindProperty("muzzle").objectReferenceValue = muzzle;
            });
            if (role == 2) Set(creature, fields => fields.FindProperty("blastCorePrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<BlastCore>(Folder + "/BlastCore.prefab"));
            root.AddComponent<WebCapture>(); root.AddComponent<BubbleCapture>();
            Transform visual = Child(root.transform, "Visual", Vector3.zero);
            Part(visual, PrimitiveType.Cube, "Body", collider.center, size * 0.92f, bodyMaterial);
            for (int side = -1; side <= 1; side += 2)
            {
                Part(visual, PrimitiveType.Cube, "Foot", new Vector3(side * size.x * 0.27f, 0.12f, 0.12f),
                    new Vector3(size.x * 0.3f, 0.24f, size.z * 0.85f), bodyMaterial);
                Part(visual, PrimitiveType.Sphere, "Eye", new Vector3(side * size.x * 0.22f, size.y * 0.77f, size.z * 0.47f),
                    Vector3.one * (role == 2 ? 0.2f : 0.12f), eyeMaterial);
                if (role == 0) Part(visual, PrimitiveType.Cube, "Shoulder", new Vector3(side * 0.65f, 0.65f, 0.1f),
                    new Vector3(0.3f, 0.5f, 0.65f), bodyMaterial);
            }
            if (role > 0)
                Part(pivot, role == 2 ? PrimitiveType.Sphere : PrimitiveType.Cube, "Firing Organ", Vector3.forward * size.z * 0.38f,
                    role == 2 ? Vector3.one * 0.7f : new Vector3(0.22f, 0.2f, 0.7f), bodyMaterial);
            root.AddComponent<AsuraPresentation>(); Save(root, name);
        }
        finally { Object.DestroyImmediate(root); }
    }

    [MenuItem("Tools/Creatures/Asura/Add Prototype To Scene Yoyo")]
    public static void AddToYoyo()
    {
        CreateAssets();
        Scene scene = SceneManager.GetSceneByPath(Yoyo);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(Yoyo, OpenSceneMode.Additive);
        foreach (var existing in scene.GetRootGameObjects()) if (existing.name == RootName)
        {
            PlacePlayer(scene, existing.transform.Find("Player Test Start"));
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = existing; return;
        }
        var root = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        Undo.RegisterCreatedObjectUndo(root, "Place Asura Prototype");
        // A separate elevated arena avoids the scene's flock and existing ground experiments.
        root.transform.position = new Vector3(75f, 8f, -15f);
        var floor = Part(root.transform, PrimitiveType.Cube, "Arena Floor", new Vector3(0f, -0.5f, 0f),
            new Vector3(48f, 1f, 40f), floorMaterial);
        AddBox(floor.gameObject, 7);
        var obstacle = Part(root.transform, PrimitiveType.Cube, "Shot Obstacle", new Vector3(0f, 1.5f, 1f),
            new Vector3(3f, 3f, 1f), floorMaterial); AddBox(obstacle.gameObject, 8);
        obstacle = Part(root.transform, PrimitiveType.Cube, "Low Obstacle", new Vector3(-9f, 0.6f, 6f),
            new Vector3(2f, 1.2f, 4f), floorMaterial); AddBox(obstacle.gameObject, 8);
        string[] names = { "MeleeAsura", "RangedAsura", "GeneralAsura" };
        for (int role = 0; role < 3; role++)
        {
            for (int side = 0; side < 2; side++)
            {
                GameObject unit = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + names[role] + ".prefab"), scene);
                unit.transform.SetParent(root.transform, false);
                unit.transform.localPosition = new Vector3(side == 0 ? -7f : 7f, 0.08f, -10f + role * 11f);
                unit.transform.localRotation = Quaternion.LookRotation(side == 0 ? Vector3.right : Vector3.left);
                unit.name = names[role] + (side == 0 ? " Light" : " Dark");
                Set(unit.GetComponent<Creature>(), fields => fields.FindProperty("initialPolarity").enumValueIndex = (int)(side == 0 ? CreaturePolarity.Light : CreaturePolarity.Dark));
            }
        }
        PlaceWitness(scene, root.transform, "GooCreature", new Vector3(-15f, 0.45f, 8f), CreaturePolarity.None);
        PlaceWitness(scene, root.transform, "CorruptSeed", new Vector3(14f, 0.3f, -5f), CreaturePolarity.Dark);
        GameObject core = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/BlastCore.prefab"), scene);
        core.transform.SetParent(root.transform, false); core.transform.localPosition = new Vector3(-16f, 0.5f, -13.3f);
        var effects = root.AddComponent<AsuraPrototypeEffects>();
        Set(effects, fields => fields.FindProperty("explosionMaterial").objectReferenceValue = explosionMaterial);
        var marker = Child(root.transform, "Player Test Start", new Vector3(-18f, 1.5f, -15f));
        marker.rotation = Quaternion.LookRotation(new Vector3(18f, 0f, 15f));
        PlacePlayer(scene, marker);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;
        Debug.Log("Asura Prototype authored in Scene_Yoyo: six manual Asuras, two ecology witnesses, and one Blast Core.");
    }
    private static void PlacePlayer(Scene scene, Transform marker)
    {
        if (marker == null) return;
        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
        {
            var player = sceneRoot.GetComponentInChildren<SmoothFirstPersonController>(true);
            if (player == null) continue;
            Undo.RecordObject(player.transform, "Place player at Asura arena start");
            player.transform.position = marker.position;
            Transform orientation = player.transform.Find("Orientation");
            if (orientation != null)
            {
                Undo.RecordObject(orientation, "Face Asura arena");
                orientation.rotation = marker.rotation;
                PrefabUtility.RecordPrefabInstancePropertyModifications(orientation);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
            break;
        }
        EditorSceneManager.MarkSceneDirty(scene);
    }
    private static void PlaceWitness(Scene scene, Transform parent, string name, Vector3 point, CreaturePolarity polarity)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/" + name + ".prefab");
        if (prefab == null) return;
        GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        root.transform.SetParent(parent, false); root.transform.localPosition = point;
        Set(root.GetComponent<Creature>(), fields => fields.FindProperty("initialPolarity").enumValueIndex = (int)polarity);
        root.name = name + " " + polarity + " Witness";
    }
    private static Rigidbody Body(GameObject root, float mass)
    {
        Rigidbody body = root.AddComponent<Rigidbody>(); body.mass = mass; body.useGravity = true;
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        return body;
    }
    private static void ConfigureCreature(Creature creature, float health, CreaturePolarity polarity, float age, float rate)
    {
        Set(creature, fields =>
        {
            fields.FindProperty("maxHealth").floatValue = health;
            fields.FindProperty("initialPolarity").enumValueIndex = (int)polarity;
            fields.FindProperty("age").floatValue = age; fields.FindProperty("ageRate").floatValue = rate;
        });
    }
    private static void ConfigureAttack(CreatureAttack attack, float range, float damage, float interval, float windup, bool explosive)
    {
        Set(attack, fields =>
        {
            fields.FindProperty("range").floatValue = range; fields.FindProperty("damage").floatValue = damage;
            fields.FindProperty("interval").floatValue = interval; fields.FindProperty("windup").floatValue = windup;
            if (attack is ProjectileAttack)
            {
                fields.FindProperty("projectileSpeed").floatValue = explosive ? 12f : 18f;
                fields.FindProperty("projectilePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ProjectileCreature>(Folder + "/" +
                    (explosive ? "ExplosiveProjectile" : "BasicProjectile") + ".prefab");
            }
        });
    }
    private static void Save(GameObject root, string name)
    { root.SetActive(true); PrefabUtility.SaveAsPrefabAsset(root, Folder + "/" + name + ".prefab"); }
    private static void Set(Object target, Action<SerializedObject> configure)
    { var fields = new SerializedObject(target); configure(fields); fields.ApplyModifiedPropertiesWithoutUndo(); }
    private static Transform Child(Transform parent, string name, Vector3 point)
    { var child = new GameObject(name).transform; child.SetParent(parent, false); child.localPosition = point; return child; }
    private static Transform Part(Transform parent, PrimitiveType type, string name, Vector3 point, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(type); part.name = name; part.transform.SetParent(parent, false);
        part.transform.localPosition = point; part.transform.localScale = scale;
        Object.DestroyImmediate(part.GetComponent<Collider>()); part.GetComponent<Renderer>().sharedMaterial = material;
        return part.transform;
    }
    private static void AddBox(GameObject root, int layer) { root.layer = layer; root.AddComponent<BoxCollider>(); }
    private static Material Material(string name, Color color, bool transparent = false)
    {
        string path = Materials + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Asura " + name, enableInstancing = true };
        material.SetColor("_BaseColor", transparent ? new Color(color.r, color.g, color.b, 0.2f) : color);
        material.SetFloat("_Smoothness", 0.15f);
        if (transparent)
        {
            material.SetFloat("_Surface", 1f); material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue = 3000;
        }
        AssetDatabase.CreateAsset(material, path); return material;
    }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, split));
        AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
    }
}
