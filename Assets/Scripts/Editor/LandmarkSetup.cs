using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One click setup for the landmarks: makes their materials and adds the Hanging Spire to the open
/// scene, plus SteleInscriptions on the Meditation object so learned glyphs show on the steles.
/// </summary>
public static class LandmarkSetup
{
    private const string Folder = "Assets/Materials/Landmarks/";

    [MenuItem("Tools/Capstone/Landmarks/Add Hanging Spire To Open Scene")]
    private static void AddSpire()
    {
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world == null)
        {
            EditorUtility.DisplayDialog("Landmarks", "No ProceduralWorld found in the open scene.", "OK");
            return;
        }

        // Pale stone with ink joints and script: the tower reads against the dark line-world terrain.
        Material spire = FromShader("MAT_Landmark_SpireStone", "Capstone/Landmarks/SpireStone", m =>
        {
            m.SetColor("_Color", new Color(0.8f, 0.77f, 0.7f, 1f));
            m.SetColor("_ShadowColor", new Color(0.42f, 0.4f, 0.38f, 1f));
            m.SetColor("_LineColor", new Color(0.07f, 0.07f, 0.08f, 1f));
            m.SetColor("_ScriptColor", new Color(0.1f, 0.09f, 0.09f, 1f));
            m.SetColor("_HatchColor", new Color(0.2f, 0.19f, 0.18f, 1f));
            m.SetColor("_GlowColor", new Color(0.15f, 0.75f, 0.8f, 1f));
            m.SetFloat("_GlowStrength", 1.2f);
            m.SetFloat("_HatchStrength", 0.6f);
            m.SetFloat("_ShadowLineDim", 1f);
        });
        // Inner world twin: no sun, near-black stone with violet joints and glow, like the cave.
        Material innerSpire = FromShader("MAT_Landmark_SpireStoneInner", "Capstone/Landmarks/SpireStone", m =>
        {
            m.SetColor("_Color", new Color(0.06f, 0.055f, 0.09f, 1f));
            m.SetColor("_ShadowColor", new Color(0.01f, 0.01f, 0.02f, 1f));
            m.SetColor("_LineColor", new Color(0.45f, 0.42f, 0.85f, 1f));
            m.SetColor("_ScriptColor", new Color(0.4f, 0.55f, 0.8f, 1f));
            m.SetColor("_GlowColor", new Color(0.7f, 0.5f, 1f, 1f));
            m.SetFloat("_MainLightAmount", 0f);
            m.SetFloat("_FakeLightAmount", 0.35f);
            m.SetFloat("_HatchStrength", 0.4f);
            m.SetFloat("_ShadowLineDim", 0.8f);
            m.SetFloat("_ScriptChance", 0.45f);
            m.SetFloat("_PulseSpeed", -2f);
            m.SetFloat("_PulseSpacing", 25f);
        });
        Material glyph = Unlit("MAT_Landmark_Glyph", new Color(0.9f, 0.9f, 0.86f, 1f));
        Material seed = Unlit("MAT_Landmark_Seed", new Color(0.42f, 0.74f, 0.76f, 1f));
        if (spire == null || glyph == null)
        {
            EditorUtility.DisplayDialog("Landmarks", "Could not make the landmark materials (SpireStone shader or URP Unlit not found; let Unity finish compiling).", "OK");
            return;
        }

        HangingSpire landmark = Object.FindFirstObjectByType<HangingSpire>();
        if (landmark == null)
        {
            var go = new GameObject("Landmark - Hanging Spire");
            Undo.RegisterCreatedObjectUndo(go, "Add Hanging Spire");
            landmark = go.AddComponent<HangingSpire>();
        }
        Undo.RecordObject(landmark, "Set Spire Materials");
        landmark.SetMaterials(spire, innerSpire, glyph, seed);
        AssignSeedModel(landmark);
        var serialized = new SerializedObject(landmark);
        serialized.FindProperty("world").objectReferenceValue = world;
        serialized.ApplyModifiedProperties();

        string note = "";
        // Tentacle creatures on the tower are copies of the one already in the scene.
        TentacleCreature tentacle = Object.FindFirstObjectByType<TentacleCreature>();
        if (tentacle != null) landmark.SetTentacleTemplate(tentacle);
        else note += " No TentacleCreature in the scene to copy, so the tower has no creatures.";

        // Pick up and drop creatures with a right click (the right hand reaches out).
        PlayerCreatureCarrier carrier = Object.FindFirstObjectByType<PlayerCreatureCarrier>();
        if (carrier != null)
        {
            var carrierSettings = new SerializedObject(carrier);
            carrierSettings.FindProperty("carryInput").enumValueIndex = (int)PlayerCreatureCarrier.CarryInput.ToggleRightMouse;
            carrierSettings.ApplyModifiedProperties();
        }

        MeditationController meditation = Object.FindFirstObjectByType<MeditationController>();
        if (meditation != null)
        {
            SteleInscriptions inscriptions = meditation.GetComponent<SteleInscriptions>();
            if (inscriptions == null) inscriptions = Undo.AddComponent<SteleInscriptions>(meditation.gameObject);
            Undo.RecordObject(inscriptions, "Set Inscription Material");
            inscriptions.SetMaterial(glyph);
            EditorUtility.SetDirty(inscriptions);
        }
        else
        {
            note = " No Meditation object found, so glyphs will not show on steles yet.";
        }

        EditorUtility.SetDirty(landmark);
        EditorSceneManager.MarkSceneDirty(landmark.gameObject.scene);
        Selection.activeGameObject = landmark.gameObject;
        Debug.Log("Hanging Spire added. It is built in Play mode around a sinkhole about 85 m from spawn. Save the scene to keep it." + note);
    }

    // The crown seed is a copy of Daniel's "Colorful" object in the open scene.
    private static void AssignSeedModel(HangingSpire landmark)
    {
        const string sourceName = "Colorful";
        GameObject source = null;
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name == sourceName && t.GetComponentInChildren<Renderer>(true) != null) { source = t.gameObject; break; }
        landmark.SetSeedModel(null, null, Vector3.zero);
        landmark.SetSeedSource(source);
        if (source == null)
            Debug.LogWarning("No object named " + sourceName + " with a renderer in the scene; the crown seed keeps the built-in pod shape.");
        else
            Debug.Log("Crown seed will be a copy of " + source.name + ".");
    }

    [MenuItem("Tools/Capstone/Landmarks/Reset Landmark Progress (Seeds And Glyphs)")]
    private static void ResetProgress()
    {
        LandmarkProgress.ResetAll();
        Debug.Log("Landmark progress cleared: " + LandmarkProgress.FilePath);
    }

    private static Material FromShader(string name, string shaderName, System.Action<Material> configure)
    {
        string path = Folder + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        Shader shader = Shader.Find(shaderName);
        if (shader == null) return null;
        Directory.CreateDirectory(Folder);
        material = new Material(shader) { name = name };
        configure?.Invoke(material);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Material Unlit(string name, Color color)
    {
        string path = Folder + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) return null;
        Directory.CreateDirectory(Folder);
        material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Cull", 0f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
