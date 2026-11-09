using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Creates missing prototype assets and places one compost creature without resetting tuning.</summary>
public static class CompostCreatureSetup
{
    public const string PrefabPath = "Assets/Prefabs/Creatures/Compost/CompostCreature.prefab";
    public const string ScenePath = "Assets/Scenes/Scene_Yoyo.unity";

    [MenuItem("Tools/Creatures/Compost/Create Assets")]
    public static void CreateAssets() => RecycleSetup.CreateAssets();

    [MenuItem("Tools/Creatures/Compost/Redesign As Fungal Network")]
    public static void RedesignAsMossPlatform() => RecycleSetup.CreateAssets();

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
