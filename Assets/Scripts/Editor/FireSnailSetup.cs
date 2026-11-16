using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Editor-only authoring for the Fire Snail: its prefab, its lava material, and adding one to a scene.</summary>
public static class FireSnailSetup
{
    public const string PrefabPath = "Assets/Prefabs/Creatures/Fire/Fire Snail.prefab";
    public const string LavaMaterialPath = "Assets/Materials/Creatures/MAT_Lava.mat";

    /// <summary>The Fire Snail prefab, made (with the lava material) if it does not exist yet.</summary>
    [MenuItem("Tools/Creatures/Fire Snail/Make Or Update Prefab")]
    public static GameObject Prefab()
    {
        Material lava = LavaMaterial();
        var go = new GameObject("Fire Snail");
        try
        {
            Rigidbody body = go.AddComponent<Rigidbody>();
            body.mass = 3f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            var collider = go.AddComponent<SphereCollider>();
            collider.radius = 0.45f;
            collider.center = Vector3.up * 0.45f;
            var snail = go.AddComponent<FireSnail>();
            var fields = new SerializedObject(snail);
            fields.FindProperty("lavaMaterial").objectReferenceValue = lava;
            fields.FindProperty("maxHealth").floatValue = 80f;
            fields.FindProperty("initialPolarity").enumValueIndex = (int)CreaturePolarity.None;
            fields.FindProperty("age").floatValue = 900f;
            fields.FindProperty("ageRate").floatValue = 0f;
            fields.ApplyModifiedPropertiesWithoutUndo();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Debug.Log("Fire Snail prefab saved to " + PrefabPath + ".", prefab);
            return prefab;
        }
        finally { Object.DestroyImmediate(go); }
    }

    public static Material LavaMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(LavaMaterialPath);
        if (material != null) return material;
        Shader shader = Shader.Find("Capstone/Fluid/Lava");
        if (shader == null) throw new InvalidOperationException("The Capstone/Fluid/Lava shader is missing.");
        material = new Material(shader) { name = "MAT_Lava" };
        AssetDatabase.CreateAsset(material, LavaMaterialPath);
        return material;
    }

    [MenuItem("Tools/Creatures/Fire Snail/Add Fire Snail To Open Scene")]
    public static void AddToScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before editing the scene.");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) prefab = Prefab();
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(go, "Add Fire Snail");
        go.transform.position = PlacementPoint() + Vector3.up * 0.3f;
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        Debug.Log("Fire Snail added under the Scene view's centre. Save the scene to keep it.", go);
    }

    [MenuItem("Tools/Creatures/Fire Snail/Fill Every Snail (Play Mode)")]
    public static void FillAll()
    {
        foreach (FireSnail snail in Object.FindObjectsByType<FireSnail>(FindObjectsSortMode.None)) snail.AddHeat(snail.Capacity);
    }

    [MenuItem("Tools/Creatures/Fire Snail/Fill Every Snail (Play Mode)", true)]
    private static bool CanFill() => Application.isPlaying;

    /// <summary>Under the Scene view's centre, on Ground if there is any.</summary>
    private static Vector3 PlacementPoint()
    {
        Vector3 pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        return Physics.Raycast(pivot + Vector3.up * 200f, Vector3.down, out RaycastHit hit, 600f, 1 << 7, QueryTriggerInteraction.Ignore) ? hit.point : pivot;
    }
}
