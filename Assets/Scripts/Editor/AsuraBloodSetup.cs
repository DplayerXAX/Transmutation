using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Idempotent authoring of blood assets and the existing contact showcase.</summary>
public static class AsuraBloodSetup
{
    public const string BloodPath = AsuraPrototypeSetup.Folder + "/Resources/AsuraBlood.prefab";
    public const string ShellPath = "Assets/Materials/Creatures/Asura/BloodShell.mat";
    private static void Set(Object target, string field, Object value)
    {
        var so = new SerializedObject(target); so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    [MenuItem("Tools/Creatures/Asura/Install Blood In Showcase")]
    public static string Install()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        if (!AssetDatabase.IsValidFolder(AsuraPrototypeSetup.Folder + "/Resources"))
            AssetDatabase.CreateFolder(AsuraPrototypeSetup.Folder, "Resources");
        Material shell = AssetDatabase.LoadAssetAtPath<Material>(ShellPath);
        if (shell == null)
        {
            shell = new Material(Shader.Find("Transmutation/Asura Blood Shell"));
            shell.SetColor("_BaseColor", new Color(0.96f, 0.78f, 0.3f, 0.27f));
            AssetDatabase.CreateAsset(shell, ShellPath);
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(BloodPath) == null)
        {
            var root = new GameObject("AsuraBlood"); root.SetActive(false);
            try
            {
                var body = root.AddComponent<Rigidbody>(); body.mass = 0.2f; body.linearDamping = 1.5f;
                body.constraints = RigidbodyConstraints.FreezeRotation;
                var collider = root.AddComponent<SphereCollider>(); collider.radius = 0.28f;
                root.AddComponent<AsuraBlood>();
                var so = new SerializedObject(root.GetComponent<AsuraBlood>());
                so.FindProperty("initialPolarity").enumValueIndex = (int)CreaturePolarity.None;
                so.FindProperty("creatureName").stringValue = "Asura Blood";
                so.FindProperty("ageRate").floatValue = 0f; so.ApplyModifiedPropertiesWithoutUndo();
                var pool = GameObject.CreatePrimitive(PrimitiveType.Sphere); pool.name = "Blood pool";
                pool.transform.SetParent(root.transform, false); pool.transform.localScale = new Vector3(0.8f, 0.18f, 0.7f);
                pool.transform.localPosition = Vector3.down * 0.18f;
                Object.DestroyImmediate(pool.GetComponent<Collider>());
                pool.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Creatures/Asura/Body.mat");
                root.SetActive(true); PrefabUtility.SaveAsPrefabAsset(root, BloodPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        InstallRewards();
        var scene = SceneManager.GetSceneByPath(AsuraFeelSetup.ScenePath);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(AsuraFeelSetup.ScenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        var showcase = Object.FindFirstObjectByType<AsuraShowcase>();
        var player = showcase.Player;
        var stats = player.GetComponent<PlayerCreatureStats>();
        if (stats == null) stats = Undo.AddComponent<PlayerCreatureStats>(player.gameObject);
        // The showcase starts with its player cameras disabled; Camera.main is unavailable in Awake.
        var playerCamera = player.transform.root.GetComponentInChildren<Camera>(true);
        foreach (var body in player.transform.root.GetComponentsInChildren<FirstPersonBody>(true))
        {
            Set(body, "bloodCoatMaterial", shell); Set(body, "controller", player); Set(body, "viewCamera", playerCamera);
        }
        foreach (var interactor in player.transform.root.GetComponentsInChildren<PlayerCreatureInteractor>(true))
            Set(interactor, "interactionCamera", playerCamera);
        var carrier = player.transform.root.GetComponentInChildren<PlayerCreatureCarrier>(true);
        if (carrier != null) Set(carrier, "carryCamera", playerCamera);
        var prompt = Object.FindFirstObjectByType<InteractionPromptHUD>();
        if (prompt == null) prompt = new GameObject("Interaction Prompt HUD").AddComponent<InteractionPromptHUD>();
        Set(prompt, "carrier", carrier);
        Set(prompt, "interactor", player.transform.root.GetComponentInChildren<PlayerCreatureInteractor>(true));
        var hud = Object.FindFirstObjectByType<BloodCoatHUD>();
        if (hud == null) hud = new GameObject("Blood Coat HUD").AddComponent<BloodCoatHUD>();
        Set(hud, "player", stats);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        return "Blood prefab, four Asura drops, body shell and globe installed in Scene_AsuraShowcase.";
    }

    [MenuItem("Tools/Creatures/Asura/Install Bloodstone Rewards")]
    public static void InstallRewards()
    {
        const string folder = AsuraPrototypeSetup.Folder + "/Resources";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(AsuraPrototypeSetup.Folder, "Resources");
        if (AssetDatabase.LoadAssetAtPath<Material>(folder + "/BloodDroplet.mat") == null)
            AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")), folder + "/BloodDroplet.mat");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/Bloodstone.prefab") == null)
        {
            var stone = new GameObject("Bloodstone");
            try
            {
                var rigid = stone.AddComponent<Rigidbody>();
                rigid.mass = 0.35f;
                rigid.linearDamping = 1.5f;
                rigid.angularDamping = 3f;
                rigid.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                stone.AddComponent<SphereCollider>().radius = 0.18f;
                stone.AddComponent<Bloodstone>();
                var facet = GameObject.CreatePrimitive(PrimitiveType.Cube);
                facet.name = "Crystal";
                facet.transform.SetParent(stone.transform, false);
                facet.transform.localRotation = Quaternion.Euler(32, 18, 45);
                facet.transform.localScale = new Vector3(0.22f, 0.30f, 0.20f);
                Object.DestroyImmediate(facet.GetComponent<Collider>());
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.34f, 0.065f, 0.10f) };
                AssetDatabase.CreateAsset(material, folder + "/Bloodstone.mat");
                facet.GetComponent<Renderer>().sharedMaterial = material;
                PrefabUtility.SaveAsPrefabAsset(stone, folder + "/Bloodstone.prefab");
            }
            finally { Object.DestroyImmediate(stone); }
        }
        var blood = AssetDatabase.LoadAssetAtPath<AsuraBlood>(BloodPath);
        foreach (var role in new[] { ("MeleeAsura", 25), ("RollingAsura", 25), ("RangedAsura", 50), ("GeneralAsura", 100) })
        {
            string path = AsuraPrototypeSetup.Folder + "/" + role.Item1 + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!root.TryGetComponent<AsuraBloodDrop>(out var drop)) drop = root.AddComponent<AsuraBloodDrop>();
                Set(drop, "bloodPrefab", blood);
                if (!root.TryGetComponent<AsuraBloodPointReward>(out var reward)) reward = root.AddComponent<AsuraBloodPointReward>();
                var fields = new SerializedObject(reward);
                fields.FindProperty("bloodPointValue").intValue = role.Item2;
                fields.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
    }
}
