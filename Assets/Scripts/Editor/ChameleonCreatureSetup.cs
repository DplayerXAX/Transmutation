using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Repeatable prefab creation and placement in the regular Yoyo scene.</summary>
public static class ChameleonCreatureSetup
{
    public const string PrefabPath = "Assets/Prefabs/Creatures/ChameleonCreature.prefab";
    private const string ScenePath = "Assets/Scenes/Scene_Yoyo.unity";

    [MenuItem("Tools/Creatures/Chameleon/Create Assets")]
    public static void CreateAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        Material skin = GetMaterial("Assets/Materials/Creatures/MAT_Chameleon_Skin.mat", new Color(0.22f, 0.58f, 0.16f));
        Material pupil = GetMaterial("Assets/Materials/Creatures/MAT_Chameleon_Pupil.mat", new Color(0.015f, 0.02f, 0.01f));
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
        var root = new GameObject("Chameleon Creature");
        try
        {
            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.mass = 0.4f;
            rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.27f, -0.13f);
            collider.size = new Vector3(0.53f, 0.54f, 1.28f);
            var visual = new GameObject("Chameleon Shape").transform;
            visual.SetParent(root.transform, false);
            var skins = new List<Renderer>();
            Shape(visual, "Body", PrimitiveType.Sphere, new Vector3(0f, 0.26f, 0f), new Vector3(0.3f, 0.32f, 0.62f), skin, skins);
            Shape(visual, "Head", PrimitiveType.Sphere, new Vector3(0f, 0.36f, 0.33f), new Vector3(0.27f, 0.27f, 0.32f), skin, skins);
            for (int side = -1; side <= 1; side += 2)
            {
                Shape(visual, "Eye Turret", PrimitiveType.Sphere, new Vector3(side * 0.13f, 0.42f, 0.39f), Vector3.one * 0.15f, skin, skins);
                Shape(visual, "Pupil", PrimitiveType.Sphere, new Vector3(side * 0.181f, 0.432f, 0.432f), Vector3.one * 0.064f, pupil, null);
                for (int leg = -1; leg <= 1; leg += 2)
                {
                    Vector3 shoulder = new Vector3(side * 0.1f, 0.24f, leg * 0.18f);
                    Vector3 elbow = new Vector3(side * 0.24f, 0.12f, leg * 0.22f);
                    Vector3 foot = new Vector3(side * 0.2f, 0.045f, leg * 0.29f);
                    Segment(visual, "Upper Leg", shoulder, elbow, 0.065f, skin, skins);
                    Segment(visual, "Lower Leg", elbow, foot, 0.045f, skin, skins);
                    Shape(visual, "Foot", PrimitiveType.Sphere, foot, new Vector3(0.09f, 0.065f, 0.12f), skin, skins);
                }
            }
            Vector3 previous = new Vector3(0f, 0.26f, -0.27f);
            for (int i = 0; i <= 15; i++)
            {
                float angle = i / 15f * 300f * Mathf.Deg2Rad;
                float radius = Mathf.Lerp(0.23f, 0.12f, i / 15f);
                Vector3 point = new Vector3(0f, 0.26f + Mathf.Sin(angle) * radius, -0.70f + Mathf.Cos(angle) * radius);
                Segment(visual, "Curled Tail " + i, previous, point, Mathf.Lerp(0.075f, 0.025f, i / 15f), skin, skins);
                previous = point;
            }
            ChameleonCreature creature = root.AddComponent<ChameleonCreature>();
            using (var fields = new SerializedObject(creature))
            {
                fields.FindProperty("visualRoot").objectReferenceValue = visual;
                var parts = fields.FindProperty("skinRenderers");
                parts.arraySize = skins.Count;
                for (int i = 0; i < skins.Count; i++) parts.GetArrayElementAtIndex(i).objectReferenceValue = skins[i];
                fields.FindProperty("creatureName").stringValue = "Chameleon";
                fields.FindProperty("initialPolarity").enumValueIndex = (int)CreaturePolarity.None;
                fields.ApplyModifiedPropertiesWithoutUndo();
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
        AssetDatabase.SaveAssets();
    }

    private static Material GetMaterial(string path, Color color)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("URP/Lit shader is missing.");
        material = new Material(shader);
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.25f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Transform Shape(Transform parent, string name, PrimitiveType type, Vector3 position,
        Vector3 scale, Material material, List<Renderer> skins)
    {
        var part = GameObject.CreatePrimitive(type);
        part.name = name;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        var renderer = part.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        skins?.Add(renderer);
        return part.transform;
    }

    private static void Segment(Transform parent, string name, Vector3 from, Vector3 to,
        float thickness, Material material, List<Renderer> skins)
    {
        Vector3 delta = to - from;
        Transform part = Shape(parent, name, PrimitiveType.Capsule, (from + to) * 0.5f,
            new Vector3(thickness, delta.magnitude * 0.5f + thickness * 0.25f, thickness), material, skins);
        part.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
    }

    [MenuItem("Tools/Creatures/Chameleon/Add To Scene Yoyo")]
    public static void AddToYoyoScene()
    {
        CreateAssets();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        SmoothFirstPersonController player = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.GetComponentInChildren<ChameleonCreature>(true) != null) return;
            if (player == null) player = root.GetComponentInChildren<SmoothFirstPersonController>(true);
        }
        if (player == null) throw new InvalidOperationException("Scene_Yoyo needs a player start.");
        Vector3 forward = Vector3.ProjectOnPlane(player.Orientation != null ? player.Orientation.forward : player.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        Vector3 position = player.transform.position + forward * 3f;
        Physics.SyncTransforms();
        var hits = new RaycastHit[128];
        int count = scene.GetPhysicsScene().Raycast(position + Vector3.up * 10f, Vector3.down, hits, 30f, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        bool grounded = false;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.normal.y < 0.5f || hit.distance >= nearest ||
                hit.collider.GetComponentInParent<Creature>() != null ||
                hit.collider.GetComponentInParent<SmoothFirstPersonController>() != null) continue;
            nearest = hit.distance;
            position.y = hit.point.y + 0.015f;
            grounded = true;
        }
        if (!grounded) throw new InvalidOperationException("No ground found three metres ahead of the player.");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
        Undo.RegisterCreatedObjectUndo(instance, "Place chameleon");
        instance.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = instance;
    }
}
