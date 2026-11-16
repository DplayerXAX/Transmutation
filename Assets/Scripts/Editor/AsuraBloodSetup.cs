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
        var blood = AssetDatabase.LoadAssetAtPath<AsuraBlood>(BloodPath);
        foreach (string name in new[] { "MeleeAsura", "RangedAsura", "GeneralAsura", "RollingAsura" })
        {
            string path = AsuraPrototypeSetup.Folder + "/" + name + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var drop = root.GetComponent<AsuraBloodDrop>();
                if (drop == null) drop = root.AddComponent<AsuraBloodDrop>();
                Set(drop, "bloodPrefab", blood); PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
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
}
