using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Repeatable goo asset creation and scene placement; existing art and tuning are retained.</summary>
public static class GooCreatureSetup
{
    public const string PrefabPath = "Assets/Prefabs/Creatures/GooCreature.prefab";
    public const string CoatingMaterialPath = "Assets/Materials/Creatures/MAT_Goo_Coating.mat";
    private const string PlayerPath = "Assets/Prefabs/PlayerObjects.prefab";
    private const string ScenePath = "Assets/Scenes/Scene_Yoyo.unity";

    [MenuItem("Tools/Creatures/Goo/Create Assets And Configure Player")]
    public static void CreateAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before configuring goo assets.");
        Material coating = GetMaterial(CoatingMaterialPath, "Transmutation/Goo Coating");
        Material blob = GetMaterial("Assets/Materials/Creatures/MAT_Goo_Blob.mat", "Universal Render Pipeline/Lit");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            var root = new GameObject("Goo Creature");
            try
            {
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.mass = 0.35f;
                body.constraints = RigidbodyConstraints.FreezeRotation;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                SphereCollider collider = root.AddComponent<SphereCollider>();
                collider.radius = 0.25f;
                collider.center = new Vector3(0f, 0.19f, 0f);
                var shape = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                shape.name = "Blob";
                shape.transform.SetParent(root.transform, false);
                shape.transform.localPosition = new Vector3(0f, 0.2f, 0f);
                shape.transform.localScale = new Vector3(0.6f, 0.4f, 0.55f);
                Object.DestroyImmediate(shape.GetComponent<Collider>());
                shape.GetComponent<Renderer>().sharedMaterial = blob;
                GooCreature goo = root.AddComponent<GooCreature>();
                using (var fields = new SerializedObject(goo))
                {
                    fields.FindProperty("blob").objectReferenceValue = shape.transform;
                    fields.FindProperty("creatureName").stringValue = "Goo";
                    fields.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        GameObject prefab = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            SmoothFirstPersonController player = prefab.GetComponentInChildren<SmoothFirstPersonController>(true);
            if (player == null) throw new InvalidOperationException("Player prefab has no movement controller.");
            if (player.GetComponent<PlayerGooCoating>() == null) player.gameObject.AddComponent<PlayerGooCoating>();
            foreach (FirstPersonBody body in prefab.GetComponentsInChildren<FirstPersonBody>(true))
            {
                using (var fields = new SerializedObject(body))
                {
                    if (fields.FindProperty("gooMaterial").objectReferenceValue == null)
                        fields.FindProperty("gooMaterial").objectReferenceValue = coating;
                    if (fields.FindProperty("controller").objectReferenceValue == null)
                        fields.FindProperty("controller").objectReferenceValue = player;
                    fields.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            foreach (HandClimber climber in prefab.GetComponentsInChildren<HandClimber>(true))
            {
                using (var fields = new SerializedObject(climber))
                {
                    if (fields.FindProperty("controller").objectReferenceValue == null)
                        fields.FindProperty("controller").objectReferenceValue = player;
                    fields.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            PrefabUtility.SaveAsPrefabAsset(prefab, PlayerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        AssetDatabase.SaveAssets();
    }

    private static Material GetMaterial(string path, string shaderName)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        Shader shader = Shader.Find(shaderName);
        if (shader == null) throw new InvalidOperationException("Missing goo shader: " + shaderName);
        material = new Material(shader);
        material.SetColor("_BaseColor", new Color(0.18f, 0.85f, 0.27f));
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.9f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    [MenuItem("Tools/Creatures/Goo/Add Three To Scene Yoyo")]
    public static void AddToYoyoScene()
    {
        CreateAssets();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        foreach (GameObject existing in scene.GetRootGameObjects())
            if (existing.name == "Goo Creatures") { Selection.activeGameObject = existing; return; }

        SmoothFirstPersonController player = null;
        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
        {
            player = sceneRoot.GetComponentInChildren<SmoothFirstPersonController>(true);
            if (player != null) break;
        }
        if (player == null) throw new InvalidOperationException("Scene_Yoyo needs a player for goo placement.");
        Vector3 forward = player.Orientation != null ? player.Orientation.forward : player.transform.forward;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 centre = player.transform.position + forward * 3f;
        HandClimber handClimber = player.transform.root.GetComponentInChildren<HandClimber>(true);
        int groundLayers = handClimber != null
            ? new SerializedObject(handClimber).FindProperty("climbLayers").intValue
            : (1 << 7) | (1 << 8);
        var physics = scene.GetPhysicsScene();
        Physics.SyncTransforms();
        // Prefer an existing nearby steep surface over introducing new level geometry.
        float nearestWall = 18f;
        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
        foreach (Collider surface in sceneRoot.GetComponentsInChildren<Collider>())
        {
            if (surface.isTrigger || !surface.enabled || (groundLayers & (1 << surface.gameObject.layer)) == 0 ||
                surface.GetComponentInParent<Creature>() != null) continue;
            Vector3 from = player.transform.position + Vector3.up * 0.4f;
            Vector3 direction = surface.ClosestPoint(from) - from;
            if (direction.sqrMagnitude < 0.01f || !surface.Raycast(new Ray(from, direction.normalized), out RaycastHit wall, nearestWall) ||
                Vector3.Angle(wall.normal, Vector3.up) < 55f) continue;
            nearestWall = wall.distance;
            centre = wall.point + wall.normal * 1.2f;
            right = Vector3.Cross(Vector3.up, wall.normal).normalized;
        }
        var group = new GameObject("Goo Creatures");
        SceneManager.MoveGameObjectToScene(group, scene);
        Undo.RegisterCreatedObjectUndo(group, "Place goo creatures");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        for (int i = 0; i < 3; i++)
        {
            Vector3 position = centre + right * ((i - 1) * 0.85f);
            if (physics.Raycast(position + Vector3.up * 30f, Vector3.down, out RaycastHit ground, 100f,
                groundLayers, QueryTriggerInteraction.Ignore)) position.y = ground.point.y + 0.08f;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.transform.SetParent(group.transform, true);
            instance.transform.position = position;
        }
        bool hasHud = false;
        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            hasHud |= sceneRoot.GetComponentInChildren<InteractionPromptHUD>(true) != null;
        if (!hasHud)
        {
            var hud = new GameObject("Interaction Prompts");
            SceneManager.MoveGameObjectToScene(hud, scene);
            hud.AddComponent<InteractionPromptHUD>();
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = group;
        Debug.Log("Goo assets configured; three portable goo creatures placed in Scene_Yoyo.");
    }
}
