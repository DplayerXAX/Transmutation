using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Replaces the temporary primitive shell with rock and void in the actual terrain density.</summary>
public static class DanielProceduralOpening
{
    private const string Marker = "Procedural opening v5";

    [MenuItem("Tools/Capstone/Daniel/Replace Opening With Procedural Cave")]
    public static void Apply()
    {
        if (Application.isPlaying || SceneManager.GetActiveScene().path != "Assets/Scenes/Scene_Daniel.unity")
            throw new InvalidOperationException("Open Daniel in Edit Mode.");
        Transform root = GameObject.Find("Daniel Color Experiment").transform;
        if (root.Find(Marker) != null) return;
        Transform cave = root.Find("Opening Cave");
        var world = Object.FindFirstObjectByType<ProceduralWorld>();
        world.Generate(true);
        Undo.RegisterCompleteObjectUndo(world.SculptData, "Sculpt the procedural opening cave");
        Vector3 localMin = new Vector3(-18, -10, -13), localMax = new Vector3(18, 25, 47);
        var bounds = new Bounds(cave.TransformPoint((localMin + localMax) * .5f), Vector3.zero);
        for (int corner = 0; corner < 8; corner++)
            bounds.Encapsulate(cave.TransformPoint(new Vector3(
                (corner & 1) == 0 ? localMin.x : localMax.x,
                (corner & 2) == 0 ? localMin.y : localMax.y,
                (corner & 4) == 0 ? localMin.z : localMax.z)));
        world.SculptDensity(bounds, (point, density) =>
        {
            Vector3 p = cave.InverseTransformPoint(point);
            if (p.x < localMin.x || p.x > localMax.x || p.y < localMin.y || p.y > localMax.y || p.z < localMin.z || p.z > localMax.z)
                return density;
            float rock = Mathf.Max(Ellipsoid(p, new Vector3(0, 3, 3), new Vector3(11, 13, 16)),
                Ellipsoid(p, new Vector3(0, 4, 21), new Vector3(15, 18, 22)));
            float waviness = Mathf.Sin(p.z * .71f + p.y * .37f) * Mathf.Sin(p.x * .83f) * .3f;
            float result = Mathf.Max(density, rock + waviness);
            // A modest arrival hall opens into the wider mushroom chamber.
            float hall = Ellipsoid(p, new Vector3(0, 3.7f, 3), new Vector3(4.4f, 4.4f, 10));
            float chamber = Ellipsoid(p, new Vector3(0, 6, 19), new Vector3(8.7f, 9.5f, 12.5f));
            result = Mathf.Min(result, -(Mathf.Max(hall, chamber) + waviness * .45f));
            // Density solids make the floor and raised exit shelf; no extra meshes or colliders.
            result = Mathf.Max(result, RoundedSolid(p, new Vector3(0, -4.5f, 10), new Vector3(9, 4.5f, 18), .4f));
            result = Mathf.Max(result, RoundedSolid(p, new Vector3(0, 1.5f, 35), new Vector3(6.8f, 7, 8), .65f));
            float portal = Ellipsoid(p, new Vector3(0, 12, 36), new Vector3(4.8f, 3.5f, 12));
            result = Mathf.Min(result, -portal);
            return result;
        });
        // Remove only the temporary shell pieces, retaining the mushrooms and authored lights.
        foreach (Transform child in cave.Cast<Transform>().ToArray())
            if (child.GetComponent<Renderer>() != null && child.GetComponent<Collider>() != null &&
                child.GetComponent<Creature>() == null && child.GetComponentInChildren<Creature>() == null)
                Undo.DestroyObjectImmediate(child.gameObject);
        int index = 0;
        foreach (var shroom in cave.GetComponentsInChildren<BounceShroom>())
        {
            Undo.RecordObject(shroom.transform, "Place mushrooms in the terrain chamber");
            shroom.transform.localPosition = new Vector3((index++ - 1) * 4, .5f, 23);
            PrefabUtility.RecordPrefabInstancePropertyModifications(shroom.transform);
        }
        Undo.RegisterCreatedObjectUndo(new GameObject(Marker), "Mark procedural opening");
        GameObject.Find(Marker).transform.SetParent(root, false);
        EditorUtility.SetDirty(world.SculptData);
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        EditorSceneManager.SaveScene(root.gameObject.scene);
        AssetDatabase.SaveAssets();
    }

    private static float Ellipsoid(Vector3 p, Vector3 center, Vector3 radii)
    {
        Vector3 d = p - center;
        return (1 - new Vector3(d.x / radii.x, d.y / radii.y, d.z / radii.z).magnitude) * Mathf.Min(radii.x, radii.y, radii.z);
    }

    private static float RoundedSolid(Vector3 p, Vector3 center, Vector3 halfSize, float round)
    {
        Vector3 d = p - center;
        Vector3 q = new Vector3(Mathf.Abs(d.x), Mathf.Abs(d.y), Mathf.Abs(d.z)) - halfSize + Vector3.one * round;
        return -(Vector3.Max(q, Vector3.zero).magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0) - round);
    }
}
