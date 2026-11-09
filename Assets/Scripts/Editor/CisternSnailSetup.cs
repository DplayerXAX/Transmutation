using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Editor-only authoring for the Cistern Snail: adds one to the open scene. It builds its own body.</summary>
public static class CisternSnailSetup
{
    [MenuItem("Tools/Creatures/Cistern Snail/Add Cistern Snail To Open Scene")]
    public static void AddToScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before editing the scene.");
        var go = new GameObject("Creature - Cistern Snail");
        Undo.RegisterCreatedObjectUndo(go, "Add Cistern Snail");
        go.transform.position = PlacementPoint() + Vector3.up * 0.5f;
        Rigidbody body = go.AddComponent<Rigidbody>();
        body.mass = 3f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        var collider = go.AddComponent<SphereCollider>();
        collider.radius = 0.45f;
        collider.center = Vector3.up * 0.45f;
        var snail = go.AddComponent<CisternSnail>();
        var fields = new SerializedObject(snail);
        fields.FindProperty("maxHealth").floatValue = 60f;
        fields.FindProperty("initialPolarity").enumValueIndex = (int)CreaturePolarity.None;
        fields.FindProperty("age").floatValue = 900f;
        fields.FindProperty("ageRate").floatValue = 0.5f;
        fields.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        if (Object.FindFirstObjectByType<ErosiveFluidSystem>() == null)
            EditorUtility.DisplayDialog("Cistern Snail", "No ErosiveFluidSystem in this scene, so there is no water for the snail to drink here.", "OK");
        Debug.Log("Cistern Snail added under the Scene view's centre. Save the scene to keep it.", go);
    }

    [MenuItem("Tools/Creatures/Cistern Snail/Fill Every Snail (Play Mode)")]
    public static void FillAll()
    {
        foreach (CisternSnail snail in Object.FindObjectsByType<CisternSnail>(FindObjectsSortMode.None)) snail.SetStored(snail.Capacity);
    }

    [MenuItem("Tools/Creatures/Cistern Snail/Fill Every Snail (Play Mode)", true)]
    private static bool CanFill() => Application.isPlaying;

    /// <summary>Under the Scene view's centre, on Ground if there is any.</summary>
    private static Vector3 PlacementPoint()
    {
        Vector3 pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        return Physics.Raycast(pivot + Vector3.up * 200f, Vector3.down, out RaycastHit hit, 600f, 1 << 7, QueryTriggerInteraction.Ignore) ? hit.point : pivot;
    }
}
